using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Hive.EInvoice.Cii;
using static Hive.EInvoice.Reading.ReaderHelpers;

namespace Hive.EInvoice.Reading;

/// <summary>
/// Reads a UN/CEFACT CII document (XRechnung, ZUGFeRD / Factur-X, EN 16931) into an
/// <see cref="Invoice"/>. Business terms outside EN 16931 (EXTENDED-only elements) are ignored.
/// </summary>
public static class CiiReader
{
    private static readonly XNamespace Rsm = CiiWriter.Rsm;
    private static readonly XNamespace Ram = CiiWriter.Ram;
    private static readonly XNamespace Udt = CiiWriter.Udt;
    private static readonly XNamespace Qdt = CiiWriter.Qdt;

    /// <summary>Reads CII XML.</summary>
    public static ReadResult Read(byte[] xml) => Read(new MemoryStream(xml));

    /// <summary>Reads CII XML.</summary>
    public static ReadResult Read(Stream xml) => Read(XDocument.Load(xml, LoadOptions.PreserveWhitespace));

    /// <summary>Reads a parsed CII document.</summary>
    public static ReadResult Read(XDocument doc)
    {
        var root = doc.Root ?? throw new FormatException("Empty document.");
        if (root.Name != Rsm + "CrossIndustryInvoice") throw new FormatException("Not a CII CrossIndustryInvoice document.");

        var inv = new Invoice { BusinessProcess = null };
        var context = root.Element(Rsm + "ExchangedDocumentContext");
        inv.BusinessProcess = context?.Element(Ram + "BusinessProcessSpecifiedDocumentContextParameter")?.Element(Ram + "ID").Value();
        string spec = context?.Element(Ram + "GuidelineSpecifiedDocumentContextParameter")?.Element(Ram + "ID").Value() ?? "";

        var exchanged = root.Element(Rsm + "ExchangedDocument");
        inv.Number = exchanged?.Element(Ram + "ID").Value() ?? "";
        inv.TypeCode = TypeCode(exchanged?.Element(Ram + "TypeCode").Value());
        inv.IssueDate = DateTimeOf(exchanged?.Element(Ram + "IssueDateTime")) ?? default;
        foreach (var note in exchanged?.Elements(Ram + "IncludedNote") ?? Enumerable.Empty<XElement>())
        {
            if (note.Element(Ram + "Content").Value() is { } text)
                inv.Notes.Add(new InvoiceNote(text, note.Element(Ram + "SubjectCode").Value()));
        }

        var transaction = root.Element(Rsm + "SupplyChainTradeTransaction");
        foreach (var line in transaction?.Elements(Ram + "IncludedSupplyChainTradeLineItem") ?? Enumerable.Empty<XElement>())
            inv.Lines.Add(Line(line));

        Agreement(inv, transaction?.Element(Ram + "ApplicableHeaderTradeAgreement"));
        Delivery(inv, transaction?.Element(Ram + "ApplicableHeaderTradeDelivery"));
        var stated = Settlement(inv, transaction?.Element(Ram + "ApplicableHeaderTradeSettlement"));

        InvoiceProfile? profile = InvoiceProfileExtensions.TryFromSpecificationIdentifier(spec, out var p) ? p : null;
        return new ReadResult(inv, InvoiceSyntax.Cii, spec, profile, stated);
    }

    private static DateTime? DateTimeOf(XElement? container)
        => Date(container?.Element(Udt + "DateTimeString").Value() ?? container?.Element(Udt + "DateString").Value()
                ?? container?.Element(Qdt + "DateTimeString").Value());

    private static InvoiceLine Line(XElement item)
    {
        var line = new InvoiceLine();
        var doc = item.Element(Ram + "AssociatedDocumentLineDocument");
        line.Id = doc?.Element(Ram + "LineID").Value() ?? "";
        line.Note = doc?.Element(Ram + "IncludedNote")?.Element(Ram + "Content").Value();

        var product = item.Element(Ram + "SpecifiedTradeProduct");
        line.Item.StandardIdentifier = Id(product?.Element(Ram + "GlobalID"));
        line.Item.SellersIdentifier = product?.Element(Ram + "SellerAssignedID").Value();
        line.Item.BuyersIdentifier = product?.Element(Ram + "BuyerAssignedID").Value();
        line.Item.Name = product?.Element(Ram + "Name").Value() ?? "";
        line.Item.Description = product?.Element(Ram + "Description").Value();
        foreach (var c in product?.Elements(Ram + "ApplicableProductCharacteristic") ?? Enumerable.Empty<XElement>())
            line.Item.Attributes.Add(new ItemAttribute { Name = c.Element(Ram + "Description").Value() ?? "", Value = c.Element(Ram + "Value").Value() ?? "" });
        foreach (var c in product?.Elements(Ram + "DesignatedProductClassification") ?? Enumerable.Empty<XElement>())
        {
            var code = c.Element(Ram + "ClassCode");
            line.Item.Classifications.Add(new ItemClassification { Code = code.Value() ?? "", Scheme = code.Attr("listID") ?? "", SchemeVersion = code.Attr("listVersionID") });
        }
        line.Item.OriginCountryCode = product?.Element(Ram + "OriginTradeCountry")?.Element(Ram + "ID").Value();

        var agreement = item.Element(Ram + "SpecifiedLineTradeAgreement");
        line.OrderLineReference = agreement?.Element(Ram + "BuyerOrderReferencedDocument")?.Element(Ram + "LineID").Value();
        var gross = agreement?.Element(Ram + "GrossPriceProductTradePrice");
        if (gross is not null)
        {
            line.GrossPrice = gross.Element(Ram + "ChargeAmount").Decimal();
            line.PriceDiscount = gross.Element(Ram + "AppliedTradeAllowanceCharge")?.Element(Ram + "ActualAmount").Decimal();
        }
        var net = agreement?.Element(Ram + "NetPriceProductTradePrice");
        line.NetPrice = net?.Element(Ram + "ChargeAmount").Decimal() ?? 0m;
        var basis = net?.Element(Ram + "BasisQuantity") ?? gross?.Element(Ram + "BasisQuantity");
        if (basis is not null)
        {
            line.PriceBaseQuantity = basis.Decimal();
            line.PriceBaseQuantityUnitCode = basis.Attr("unitCode");
        }

        var billed = item.Element(Ram + "SpecifiedLineTradeDelivery")?.Element(Ram + "BilledQuantity");
        line.Quantity = billed.Decimal() ?? 0m;
        line.UnitCode = billed.Attr("unitCode") ?? UnitCode.One;
        if (line.PriceBaseQuantityUnitCode == line.UnitCode) line.PriceBaseQuantityUnitCode = null;

        var settlement = item.Element(Ram + "SpecifiedLineTradeSettlement");
        var tax = settlement?.Element(Ram + "ApplicableTradeTax");
        line.VatCategory = Category(tax?.Element(Ram + "CategoryCode").Value());
        line.VatRate = tax?.Element(Ram + "RateApplicablePercent").Decimal();
        line.Period = Period(settlement?.Element(Ram + "BillingSpecifiedPeriod"));
        foreach (var ac in settlement?.Elements(Ram + "SpecifiedTradeAllowanceCharge") ?? Enumerable.Empty<XElement>())
            line.AllowancesAndCharges.Add(AllowanceCharge(ac));
        foreach (var r in settlement?.Elements(Ram + "AdditionalReferencedDocument") ?? Enumerable.Empty<XElement>())
        {
            if (r.Element(Ram + "TypeCode").Value() == "130")
                line.ObjectIdentifier = new SchemedIdentifier(r.Element(Ram + "IssuerAssignedID").Value() ?? "", r.Element(Ram + "ReferenceTypeCode").Value());
        }
        line.BuyerAccountingReference = settlement?.Element(Ram + "ReceivableSpecifiedTradeAccountingAccount")?.Element(Ram + "ID").Value();
        return line;
    }

    private static Period? Period(XElement? e)
    {
        if (e is null) return null;
        return new Period(DateTimeOf(e.Element(Ram + "StartDateTime")), DateTimeOf(e.Element(Ram + "EndDateTime")));
    }

    private static AllowanceCharge AllowanceCharge(XElement e)
    {
        var tax = e.Element(Ram + "CategoryTradeTax");
        return new AllowanceCharge
        {
            IsCharge = Truthy(e.Element(Ram + "ChargeIndicator")?.Element(Udt + "Indicator").Value()),
            Percentage = e.Element(Ram + "CalculationPercent").Decimal(),
            BaseAmount = e.Element(Ram + "BasisAmount").Decimal(),
            Amount = e.Element(Ram + "ActualAmount").Decimal(),
            ReasonCode = e.Element(Ram + "ReasonCode").Value(),
            Reason = e.Element(Ram + "Reason").Value(),
            VatCategory = Category(tax?.Element(Ram + "CategoryCode").Value()),
            VatRate = tax?.Element(Ram + "RateApplicablePercent").Decimal(),
        };
    }

    private static void Agreement(Invoice inv, XElement? a)
    {
        if (a is null) return;
        inv.BuyerReference = a.Element(Ram + "BuyerReference").Value();
        inv.Seller = Party(a.Element(Ram + "SellerTradeParty"));
        inv.Buyer = Party(a.Element(Ram + "BuyerTradeParty"));
        if (a.Element(Ram + "SellerTaxRepresentativeTradeParty") is { } rep)
        {
            inv.SellerTaxRepresentative = new TaxRepresentative
            {
                Name = rep.Element(Ram + "Name").Value() ?? "",
                Address = Address(rep.Element(Ram + "PostalTradeAddress")),
                VatIdentifier = rep.Elements(Ram + "SpecifiedTaxRegistration").Select(t => t.Element(Ram + "ID"))
                    .FirstOrDefault(i => i.Attr("schemeID") == "VA").Value() ?? "",
            };
        }
        inv.SalesOrderReference = a.Element(Ram + "SellerOrderReferencedDocument")?.Element(Ram + "IssuerAssignedID").Value();
        inv.PurchaseOrderReference = a.Element(Ram + "BuyerOrderReferencedDocument")?.Element(Ram + "IssuerAssignedID").Value();
        inv.ContractReference = a.Element(Ram + "ContractReferencedDocument")?.Element(Ram + "IssuerAssignedID").Value();
        foreach (var r in a.Elements(Ram + "AdditionalReferencedDocument"))
        {
            string? id = r.Element(Ram + "IssuerAssignedID").Value();
            switch (r.Element(Ram + "TypeCode").Value())
            {
                case "50":
                    inv.TenderOrLotReference = id;
                    break;
                case "130":
                    inv.InvoicedObjectIdentifier = new SchemedIdentifier(id ?? "", r.Element(Ram + "ReferenceTypeCode").Value());
                    break;
                default:
                    var att = r.Element(Ram + "AttachmentBinaryObject");
                    inv.SupportingDocuments.Add(new SupportingDocument
                    {
                        Reference = id ?? "",
                        ExternalLocation = r.Element(Ram + "URIID").Value(),
                        Description = r.Element(Ram + "Name").Value(),
                        Content = att.Value() is { } b64 ? Convert.FromBase64String(b64) : null,
                        MimeCode = att.Attr("mimeCode"),
                        FileName = att.Attr("filename"),
                    });
                    break;
            }
        }
        inv.ProjectReference = a.Element(Ram + "SpecifiedProcuringProject")?.Element(Ram + "ID").Value();
    }

    private static Party Party(XElement? e)
    {
        var p = new Party();
        if (e is null) return p;
        foreach (var id in e.Elements(Ram + "ID")) p.Identifiers.Add(new SchemedIdentifier(id.Value() ?? ""));
        foreach (var id in e.Elements(Ram + "GlobalID")) p.Identifiers.Add(new SchemedIdentifier(id.Value() ?? "", id.Attr("schemeID")));
        p.Name = e.Element(Ram + "Name").Value() ?? "";
        p.AdditionalLegalInformation = e.Element(Ram + "Description").Value();
        var legal = e.Element(Ram + "SpecifiedLegalOrganization");
        p.LegalRegistrationIdentifier = Id(legal?.Element(Ram + "ID"));
        p.TradingName = legal?.Element(Ram + "TradingBusinessName").Value();
        if (e.Element(Ram + "DefinedTradeContact") is { } c)
        {
            p.Contact = new Contact
            {
                Name = c.Element(Ram + "PersonName").Value() ?? c.Element(Ram + "DepartmentName").Value(),
                Phone = c.Element(Ram + "TelephoneUniversalCommunication")?.Element(Ram + "CompleteNumber").Value(),
                Email = c.Element(Ram + "EmailURIUniversalCommunication")?.Element(Ram + "URIID").Value(),
            };
        }
        p.Address = Address(e.Element(Ram + "PostalTradeAddress"));
        p.ElectronicAddress = Id(e.Element(Ram + "URIUniversalCommunication")?.Element(Ram + "URIID"));
        foreach (var t in e.Elements(Ram + "SpecifiedTaxRegistration").Select(t => t.Element(Ram + "ID")))
        {
            if (t.Attr("schemeID") == "VA") p.VatIdentifier = t.Value();
            else if (t.Attr("schemeID") == "FC") p.TaxRegistrationIdentifier = t.Value();
        }
        return p;
    }

    private static PostalAddress Address(XElement? e) => new()
    {
        PostCode = e?.Element(Ram + "PostcodeCode").Value(),
        Line1 = e?.Element(Ram + "LineOne").Value(),
        Line2 = e?.Element(Ram + "LineTwo").Value(),
        Line3 = e?.Element(Ram + "LineThree").Value(),
        City = e?.Element(Ram + "CityName").Value(),
        CountryCode = e?.Element(Ram + "CountryID").Value() ?? "",
        CountrySubdivision = e?.Element(Ram + "CountrySubDivisionName").Value(),
    };

    private static void Delivery(Invoice inv, XElement? d)
    {
        if (d is null) return;
        var shipTo = d.Element(Ram + "ShipToTradeParty");
        var date = DateTimeOf(d.Element(Ram + "ActualDeliverySupplyChainEvent")?.Element(Ram + "OccurrenceDateTime"));
        if (shipTo is not null || date is not null)
        {
            var info = new DeliveryInformation { ActualDeliveryDate = date };
            if (shipTo is not null)
            {
                info.PartyName = shipTo.Element(Ram + "Name").Value();
                info.LocationIdentifier = Id(shipTo.Element(Ram + "GlobalID")) ?? Id(shipTo.Element(Ram + "ID"));
                if (shipTo.Element(Ram + "PostalTradeAddress") is { } addr) info.Address = Address(addr);
            }
            inv.Delivery = info;
        }
        inv.DespatchAdviceReference = d.Element(Ram + "DespatchAdviceReferencedDocument")?.Element(Ram + "IssuerAssignedID").Value();
        inv.ReceivingAdviceReference = d.Element(Ram + "ReceivingAdviceReferencedDocument")?.Element(Ram + "IssuerAssignedID").Value();
    }

    private static StatedTotals Settlement(Invoice inv, XElement? s)
    {
        var stated = new StatedTotals();
        if (s is null) return stated;

        if (s.Element(Ram + "CreditorReferenceID").Value() is { } creditor) EnsureDirectDebit(inv).CreditorIdentifier = creditor;
        if (s.Element(Ram + "PaymentReference").Value() is { } reference) EnsurePayment(inv).RemittanceInformation = reference;
        inv.TaxCurrency = s.Element(Ram + "TaxCurrencyCode").Value();
        inv.Currency = s.Element(Ram + "InvoiceCurrencyCode").Value() ?? "EUR";

        if (s.Element(Ram + "PayeeTradeParty") is { } payee)
        {
            inv.Payee = new Payee
            {
                Name = payee.Element(Ram + "Name").Value() ?? "",
                Identifier = Id(payee.Element(Ram + "GlobalID")) ?? Id(payee.Element(Ram + "ID")),
                LegalRegistrationIdentifier = Id(payee.Element(Ram + "SpecifiedLegalOrganization")?.Element(Ram + "ID")),
            };
        }

        foreach (var means in s.Elements(Ram + "SpecifiedTradeSettlementPaymentMeans"))
        {
            var pay = EnsurePayment(inv);
            pay.MeansCode = means.Element(Ram + "TypeCode").Value() ?? pay.MeansCode;
            pay.MeansText = means.Element(Ram + "Information").Value() ?? pay.MeansText;
            if (means.Element(Ram + "ApplicableTradeSettlementFinancialCard") is { } card)
                pay.Card = new PaymentCard { PrimaryAccountNumber = card.Element(Ram + "ID").Value() ?? "", HolderName = card.Element(Ram + "CardholderName").Value() };
            if (means.Element(Ram + "PayerPartyDebtorFinancialAccount")?.Element(Ram + "IBANID").Value() is { } debtor)
                EnsureDirectDebit(inv).DebitedAccountIdentifier = debtor;
            if (means.Element(Ram + "PayeePartyCreditorFinancialAccount") is { } account)
            {
                pay.CreditTransfers.Add(new CreditTransferAccount
                {
                    AccountIdentifier = account.Element(Ram + "IBANID").Value() ?? account.Element(Ram + "ProprietaryID").Value() ?? "",
                    AccountName = account.Element(Ram + "AccountName").Value(),
                    ServiceProviderIdentifier = means.Element(Ram + "PayeeSpecifiedCreditorFinancialInstitution")?.Element(Ram + "BICID").Value(),
                });
            }
        }

        foreach (var tax in s.Elements(Ram + "ApplicableTradeTax"))
        {
            var category = Category(tax.Element(Ram + "CategoryCode").Value());
            string? reason = tax.Element(Ram + "ExemptionReason").Value();
            string? code = tax.Element(Ram + "ExemptionReasonCode").Value();
            if ((reason is not null || code is not null) && inv.VatExemptions.All(x => x.Category != category))
                inv.VatExemptions.Add(new VatExemption { Category = category, Reason = reason, ReasonCode = code });
            inv.TaxPointDate ??= Date(tax.Element(Ram + "TaxPointDate")?.Element(Udt + "DateString").Value());
            inv.TaxPointDateCode ??= tax.Element(Ram + "DueDateTypeCode").Value();
        }

        inv.InvoicingPeriod = Period(s.Element(Ram + "BillingSpecifiedPeriod"));
        foreach (var ac in s.Elements(Ram + "SpecifiedTradeAllowanceCharge"))
            inv.AllowancesAndCharges.Add(AllowanceCharge(ac));

        foreach (var terms in s.Elements(Ram + "SpecifiedTradePaymentTerms"))
        {
            if (terms.Element(Ram + "Description") is { } description && !string.IsNullOrWhiteSpace(description.Value))
                ApplyPaymentTerms(inv, description.Value);
            inv.DueDate ??= DateTimeOf(terms.Element(Ram + "DueDateDateTime"));
            if (terms.Element(Ram + "DirectDebitMandateID").Value() is { } mandate) EnsureDirectDebit(inv).MandateReference = mandate;
        }

        var sum = s.Element(Ram + "SpecifiedTradeSettlementHeaderMonetarySummation");
        stated.LineNetTotal = sum?.Element(Ram + "LineTotalAmount").Decimal();
        stated.ChargeTotal = sum?.Element(Ram + "ChargeTotalAmount").Decimal();
        stated.AllowanceTotal = sum?.Element(Ram + "AllowanceTotalAmount").Decimal();
        stated.TaxExclusiveAmount = sum?.Element(Ram + "TaxBasisTotalAmount").Decimal();
        foreach (var t in sum?.Elements(Ram + "TaxTotalAmount") ?? Enumerable.Empty<XElement>())
        {
            if (t.Attr("currencyID") is { } cur && inv.TaxCurrency is not null && cur == inv.TaxCurrency && cur != inv.Currency)
                inv.VatTotalInTaxCurrency = t.Decimal();
            else
                stated.VatTotal = t.Decimal();
        }
        inv.RoundingAmount = sum?.Element(Ram + "RoundingAmount").Decimal();
        stated.TaxInclusiveAmount = sum?.Element(Ram + "GrandTotalAmount").Decimal();
        inv.PrepaidAmount = sum?.Element(Ram + "TotalPrepaidAmount").Decimal();
        stated.AmountDue = sum?.Element(Ram + "DuePayableAmount").Decimal();

        foreach (var r in s.Elements(Ram + "InvoiceReferencedDocument"))
        {
            inv.PrecedingInvoices.Add(new PrecedingInvoiceReference
            {
                Number = r.Element(Ram + "IssuerAssignedID").Value() ?? "",
                IssueDate = Date(r.Element(Ram + "FormattedIssueDateTime")?.Element(Qdt + "DateTimeString").Value()),
            });
        }
        inv.BuyerAccountingReference = s.Element(Ram + "ReceivableSpecifiedTradeAccountingAccount")?.Element(Ram + "ID").Value();
        return stated;
    }
}
