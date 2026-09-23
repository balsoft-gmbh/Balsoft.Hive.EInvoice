using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Balsoft.Hive.EInvoice.Ubl;
using static Balsoft.Hive.EInvoice.Reading.ReaderHelpers;

namespace Balsoft.Hive.EInvoice.Reading;

/// <summary>Reads a UBL 2.1 Invoice or CreditNote (XRechnung, Peppol BIS, EN 16931) into an <see cref="Invoice"/>.</summary>
public static class UblReader
{
    private static readonly XNamespace Cac = UblWriter.Cac;
    private static readonly XNamespace Cbc = UblWriter.Cbc;
    private static readonly Regex SubjectNote = new(@"^#([A-Z]{3})#(.*)$", RegexOptions.Singleline | RegexOptions.CultureInvariant);

    /// <summary>Reads UBL XML.</summary>
    public static ReadResult Read(byte[] xml) => Read(new MemoryStream(xml));

    /// <summary>Reads UBL XML.</summary>
    public static ReadResult Read(Stream xml) => Read(XDocument.Load(xml, LoadOptions.PreserveWhitespace));

    /// <summary>Reads a parsed UBL document.</summary>
    public static ReadResult Read(XDocument doc)
    {
        var root = doc.Root ?? throw new FormatException("Empty document.");
        bool credit = root.Name == XName.Get("CreditNote", UblWriter.CreditNoteNs);
        if (!credit && root.Name != XName.Get("Invoice", UblWriter.InvoiceNs))
            throw new FormatException("Not a UBL Invoice or CreditNote document.");

        XElement? B(XElement? parent, string name) => parent?.Element(Cbc + name);
        XElement? A(XElement? parent, string name) => parent?.Element(Cac + name);

        var inv = new Invoice { BusinessProcess = B(root, "ProfileID").Value() };
        string spec = B(root, "CustomizationID").Value() ?? "";
        inv.Number = B(root, "ID").Value() ?? "";
        inv.IssueDate = Date(B(root, "IssueDate").Value()) ?? default;
        inv.DueDate = Date(B(root, "DueDate").Value());
        inv.TypeCode = TypeCode(B(root, credit ? "CreditNoteTypeCode" : "InvoiceTypeCode").Value());
        foreach (var note in root.Elements(Cbc + "Note"))
        {
            if (note.Value() is not { } text) continue;
            var m = SubjectNote.Match(text);
            inv.Notes.Add(m.Success ? new InvoiceNote(m.Groups[2].Value, m.Groups[1].Value) : new InvoiceNote(text));
        }
        inv.TaxPointDate = Date(B(root, "TaxPointDate").Value());
        inv.Currency = B(root, "DocumentCurrencyCode").Value() ?? "EUR";
        inv.TaxCurrency = B(root, "TaxCurrencyCode").Value();
        inv.BuyerAccountingReference = B(root, "AccountingCost").Value();
        inv.BuyerReference = B(root, "BuyerReference").Value();
        if (A(root, "InvoicePeriod") is { } period)
        {
            var start = Date(B(period, "StartDate").Value());
            var end = Date(B(period, "EndDate").Value());
            if (start is not null || end is not null) inv.InvoicingPeriod = new Period(start, end);
            inv.TaxPointDateCode = VatPointCodeToCii(B(period, "DescriptionCode").Value());
        }
        if (A(root, "OrderReference") is { } order)
        {
            string? po = B(order, "ID").Value();
            inv.PurchaseOrderReference = po == "NA" ? null : po;
            inv.SalesOrderReference = B(order, "SalesOrderID").Value();
        }
        foreach (var br in root.Elements(Cac + "BillingReference"))
        {
            var r = A(br, "InvoiceDocumentReference");
            inv.PrecedingInvoices.Add(new PrecedingInvoiceReference { Number = B(r, "ID").Value() ?? "", IssueDate = Date(B(r, "IssueDate").Value()) });
        }
        inv.DespatchAdviceReference = B(A(root, "DespatchDocumentReference"), "ID").Value();
        inv.ReceivingAdviceReference = B(A(root, "ReceiptDocumentReference"), "ID").Value();
        inv.TenderOrLotReference = B(A(root, "OriginatorDocumentReference"), "ID").Value();
        inv.ContractReference = B(A(root, "ContractDocumentReference"), "ID").Value();
        inv.ProjectReference = B(A(root, "ProjectReference"), "ID").Value();
        foreach (var r in root.Elements(Cac + "AdditionalDocumentReference"))
        {
            switch (B(r, "DocumentTypeCode").Value())
            {
                case "130":
                    inv.InvoicedObjectIdentifier = Id(B(r, "ID"));
                    break;
                case "50":
                    inv.ProjectReference = B(r, "ID").Value();
                    break;
                default:
                    var att = A(r, "Attachment");
                    var bin = B(att, "EmbeddedDocumentBinaryObject");
                    inv.SupportingDocuments.Add(new SupportingDocument
                    {
                        Reference = B(r, "ID").Value() ?? "",
                        Description = B(r, "DocumentDescription").Value(),
                        ExternalLocation = B(A(att, "ExternalReference"), "URI").Value(),
                        Content = bin.Value() is { } b64 ? Convert.FromBase64String(b64) : null,
                        MimeCode = bin.Attr("mimeCode"),
                        FileName = bin.Attr("filename"),
                    });
                    break;
            }
        }

        inv.Seller = Party(A(A(root, "AccountingSupplierParty"), "Party"), inv, seller: true);
        inv.Buyer = Party(A(A(root, "AccountingCustomerParty"), "Party"), inv, seller: false);
        if (A(root, "PayeeParty") is { } payee)
        {
            var payeeIds = payee.Elements(Cac + "PartyIdentification").Select(p => B(p, "ID")).ToList();
            inv.Payee = new Payee
            {
                Name = B(A(payee, "PartyName"), "Name").Value() ?? "",
                Identifier = payeeIds.Where(i => i.Attr("schemeID") != "SEPA").Select(Id).FirstOrDefault(),
                LegalRegistrationIdentifier = Id(B(A(payee, "PartyLegalEntity"), "CompanyID")),
            };
            if (payeeIds.FirstOrDefault(i => i.Attr("schemeID") == "SEPA").Value() is { } creditor)
                EnsureDirectDebit(inv).CreditorIdentifier = creditor;
        }
        if (A(root, "TaxRepresentativeParty") is { } rep)
        {
            inv.SellerTaxRepresentative = new TaxRepresentative
            {
                Name = B(A(rep, "PartyName"), "Name").Value() ?? "",
                Address = Address(A(rep, "PostalAddress")),
                VatIdentifier = B(A(rep, "PartyTaxScheme"), "CompanyID").Value() ?? "",
            };
        }
        if (A(root, "Delivery") is { } delivery)
        {
            var info = new DeliveryInformation { ActualDeliveryDate = Date(B(delivery, "ActualDeliveryDate").Value()) };
            if (A(delivery, "DeliveryLocation") is { } location)
            {
                info.LocationIdentifier = Id(B(location, "ID"));
                if (A(location, "Address") is { } address) info.Address = Address(address);
            }
            info.PartyName = B(A(A(delivery, "DeliveryParty"), "PartyName"), "Name").Value();
            inv.Delivery = info;
        }

        foreach (var means in root.Elements(Cac + "PaymentMeans"))
        {
            var code = B(means, "PaymentMeansCode");
            inv.DueDate ??= Date(B(means, "PaymentDueDate").Value());
            // A credit note's bare "due date only" payment means is not a payment instruction.
            if (credit && code.Value() == PaymentMeansCode.NotDefined && means.Elements().Count() == 2) continue;
            var pay = EnsurePayment(inv);
            pay.MeansCode = code.Value() ?? pay.MeansCode;
            pay.MeansText = code.Attr("name") ?? pay.MeansText;
            pay.RemittanceInformation = B(means, "PaymentID").Value() ?? pay.RemittanceInformation;
            if (A(means, "CardAccount") is { } card)
                pay.Card = new PaymentCard { PrimaryAccountNumber = B(card, "PrimaryAccountNumberID").Value() ?? "", HolderName = B(card, "HolderName").Value() };
            if (A(means, "PayeeFinancialAccount") is { } account)
            {
                pay.CreditTransfers.Add(new CreditTransferAccount
                {
                    AccountIdentifier = B(account, "ID").Value() ?? "",
                    AccountName = B(account, "Name").Value(),
                    ServiceProviderIdentifier = B(A(account, "FinancialInstitutionBranch"), "ID").Value(),
                });
            }
            if (A(means, "PaymentMandate") is { } mandate)
            {
                var dd = EnsureDirectDebit(inv);
                dd.MandateReference = B(mandate, "ID").Value() ?? dd.MandateReference;
                dd.DebitedAccountIdentifier = B(A(mandate, "PayerFinancialAccount"), "ID").Value() ?? dd.DebitedAccountIdentifier;
            }
        }
        if (A(root, "PaymentTerms") is { } terms) ApplyPaymentTerms(inv, terms.Element(Cbc + "Note")?.Value);

        foreach (var ac in root.Elements(Cac + "AllowanceCharge"))
            inv.AllowancesAndCharges.Add(AllowanceCharge(ac));

        var stated = new StatedTotals();
        foreach (var total in root.Elements(Cac + "TaxTotal"))
        {
            var amount = B(total, "TaxAmount");
            if (amount.Attr("currencyID") is { } cur && cur != inv.Currency && cur == inv.TaxCurrency)
            {
                inv.VatTotalInTaxCurrency = amount.Decimal();
                continue;
            }
            stated.VatTotal = amount.Decimal();
            foreach (var sub in total.Elements(Cac + "TaxSubtotal"))
            {
                var cat = A(sub, "TaxCategory");
                var category = Category(B(cat, "ID").Value());
                string? reason = B(cat, "TaxExemptionReason").Value();
                string? code = B(cat, "TaxExemptionReasonCode").Value();
                if ((reason is not null || code is not null) && inv.VatExemptions.All(x => x.Category != category))
                    inv.VatExemptions.Add(new VatExemption { Category = category, Reason = reason, ReasonCode = code });
            }
        }
        var sum = A(root, "LegalMonetaryTotal");
        stated.LineNetTotal = B(sum, "LineExtensionAmount").Decimal();
        stated.TaxExclusiveAmount = B(sum, "TaxExclusiveAmount").Decimal();
        stated.TaxInclusiveAmount = B(sum, "TaxInclusiveAmount").Decimal();
        stated.AllowanceTotal = B(sum, "AllowanceTotalAmount").Decimal();
        stated.ChargeTotal = B(sum, "ChargeTotalAmount").Decimal();
        inv.PrepaidAmount = B(sum, "PrepaidAmount").Decimal();
        inv.RoundingAmount = B(sum, "PayableRoundingAmount").Decimal();
        stated.AmountDue = B(sum, "PayableAmount").Decimal();

        foreach (var line in root.Elements(Cac + (credit ? "CreditNoteLine" : "InvoiceLine")))
            inv.Lines.Add(Line(line, credit));

        InvoiceProfile? profile = InvoiceProfileExtensions.TryFromSpecificationIdentifier(spec, out var p) ? p : null;
        return new ReadResult(inv, InvoiceSyntax.Ubl, spec, profile, stated);
    }

    internal static string? VatPointCodeToCii(string? ublCode) => ublCode switch
    {
        "3" => "5",
        "35" => "29",
        "432" => "72",
        _ => ublCode,
    };

    private static Party Party(XElement? e, Invoice inv, bool seller)
    {
        var p = new Party();
        if (e is null) return p;
        p.ElectronicAddress = Id(e.Element(Cbc + "EndpointID"));
        foreach (var id in e.Elements(Cac + "PartyIdentification").Select(x => x.Element(Cbc + "ID")))
        {
            if (id.Attr("schemeID") == "SEPA")
            {
                if (seller) EnsureDirectDebit(inv).CreditorIdentifier = id.Value();
                continue;
            }
            if (Id(id) is { } sid) p.Identifiers.Add(sid);
        }
        p.TradingName = e.Element(Cac + "PartyName")?.Element(Cbc + "Name").Value();
        p.Address = Address(e.Element(Cac + "PostalAddress"));
        foreach (var scheme in e.Elements(Cac + "PartyTaxScheme"))
        {
            string? company = scheme.Element(Cbc + "CompanyID").Value();
            if (scheme.Element(Cac + "TaxScheme")?.Element(Cbc + "ID").Value() == "VAT") p.VatIdentifier = company;
            else p.TaxRegistrationIdentifier = company;
        }
        var legal = e.Element(Cac + "PartyLegalEntity");
        p.Name = legal?.Element(Cbc + "RegistrationName").Value() ?? p.TradingName ?? "";
        p.LegalRegistrationIdentifier = Id(legal?.Element(Cbc + "CompanyID"));
        p.AdditionalLegalInformation = legal?.Element(Cbc + "CompanyLegalForm").Value();
        if (e.Element(Cac + "Contact") is { } c)
        {
            p.Contact = new Contact
            {
                Name = c.Element(Cbc + "Name").Value(),
                Phone = c.Element(Cbc + "Telephone").Value(),
                Email = c.Element(Cbc + "ElectronicMail").Value(),
            };
        }
        return p;
    }

    private static PostalAddress Address(XElement? e) => new()
    {
        Line1 = e?.Element(Cbc + "StreetName").Value(),
        Line2 = e?.Element(Cbc + "AdditionalStreetName").Value(),
        Line3 = e?.Element(Cac + "AddressLine")?.Element(Cbc + "Line").Value(),
        City = e?.Element(Cbc + "CityName").Value(),
        PostCode = e?.Element(Cbc + "PostalZone").Value(),
        CountrySubdivision = e?.Element(Cbc + "CountrySubentity").Value(),
        CountryCode = e?.Element(Cac + "Country")?.Element(Cbc + "IdentificationCode").Value() ?? "",
    };

    private static AllowanceCharge AllowanceCharge(XElement e)
    {
        var tax = e.Element(Cac + "TaxCategory");
        return new AllowanceCharge
        {
            IsCharge = Truthy(e.Element(Cbc + "ChargeIndicator").Value()),
            ReasonCode = e.Element(Cbc + "AllowanceChargeReasonCode").Value(),
            Reason = e.Element(Cbc + "AllowanceChargeReason").Value(),
            Percentage = e.Element(Cbc + "MultiplierFactorNumeric").Decimal(),
            Amount = e.Element(Cbc + "Amount").Decimal(),
            BaseAmount = e.Element(Cbc + "BaseAmount").Decimal(),
            VatCategory = Category(tax?.Element(Cbc + "ID").Value()),
            VatRate = tax?.Element(Cbc + "Percent").Decimal(),
        };
    }

    private static InvoiceLine Line(XElement e, bool credit)
    {
        var line = new InvoiceLine
        {
            Id = e.Element(Cbc + "ID").Value() ?? "",
            Note = e.Element(Cbc + "Note").Value(),
            BuyerAccountingReference = e.Element(Cbc + "AccountingCost").Value(),
            OrderLineReference = e.Element(Cac + "OrderLineReference")?.Element(Cbc + "LineID").Value(),
        };
        var quantity = e.Element(Cbc + (credit ? "CreditedQuantity" : "InvoicedQuantity"));
        line.Quantity = quantity.Decimal() ?? 0m;
        line.UnitCode = quantity.Attr("unitCode") ?? UnitCode.One;
        if (e.Element(Cac + "InvoicePeriod") is { } period)
            line.Period = new Period(Date(period.Element(Cbc + "StartDate").Value()), Date(period.Element(Cbc + "EndDate").Value()));
        if (e.Element(Cac + "DocumentReference") is { } dr && dr.Element(Cbc + "DocumentTypeCode").Value() == "130")
            line.ObjectIdentifier = Id(dr.Element(Cbc + "ID"));
        foreach (var ac in e.Elements(Cac + "AllowanceCharge"))
            line.AllowancesAndCharges.Add(AllowanceCharge(ac));

        var item = e.Element(Cac + "Item");
        line.Item.Description = item?.Element(Cbc + "Description").Value();
        line.Item.Name = item?.Element(Cbc + "Name").Value() ?? "";
        line.Item.BuyersIdentifier = item?.Element(Cac + "BuyersItemIdentification")?.Element(Cbc + "ID").Value();
        line.Item.SellersIdentifier = item?.Element(Cac + "SellersItemIdentification")?.Element(Cbc + "ID").Value();
        line.Item.StandardIdentifier = Id(item?.Element(Cac + "StandardItemIdentification")?.Element(Cbc + "ID"));
        line.Item.OriginCountryCode = item?.Element(Cac + "OriginCountry")?.Element(Cbc + "IdentificationCode").Value();
        foreach (var c in item?.Elements(Cac + "CommodityClassification") ?? Enumerable.Empty<XElement>())
        {
            var code = c.Element(Cbc + "ItemClassificationCode");
            line.Item.Classifications.Add(new ItemClassification { Code = code.Value() ?? "", Scheme = code.Attr("listID") ?? "", SchemeVersion = code.Attr("listVersionID") });
        }
        var tax = item?.Element(Cac + "ClassifiedTaxCategory");
        line.VatCategory = Category(tax?.Element(Cbc + "ID").Value());
        line.VatRate = tax?.Element(Cbc + "Percent").Decimal();
        foreach (var a in item?.Elements(Cac + "AdditionalItemProperty") ?? Enumerable.Empty<XElement>())
            line.Item.Attributes.Add(new ItemAttribute { Name = a.Element(Cbc + "Name").Value() ?? "", Value = a.Element(Cbc + "Value").Value() ?? "" });

        var price = e.Element(Cac + "Price");
        line.NetPrice = price?.Element(Cbc + "PriceAmount").Decimal() ?? 0m;
        if (price?.Element(Cbc + "BaseQuantity") is { } baseQuantity)
        {
            line.PriceBaseQuantity = baseQuantity.Decimal();
            string? unit = baseQuantity.Attr("unitCode");
            line.PriceBaseQuantityUnitCode = unit == line.UnitCode ? null : unit;
        }
        if (price?.Element(Cac + "AllowanceCharge") is { } priceDiscount)
        {
            line.GrossPrice = priceDiscount.Element(Cbc + "BaseAmount").Decimal();
            line.PriceDiscount = priceDiscount.Element(Cbc + "Amount").Decimal();
        }
        return line;
    }
}
