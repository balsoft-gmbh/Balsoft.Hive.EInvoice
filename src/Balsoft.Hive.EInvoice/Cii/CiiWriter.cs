using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Balsoft.Hive.EInvoice.Validation;

namespace Balsoft.Hive.EInvoice.Cii;

/// <summary>Options for <see cref="CiiWriter"/> and the UBL writer.</summary>
public sealed class WriterOptions
{
    /// <summary>Indent the XML. Default true.</summary>
    public bool Indent { get; set; } = true;

    /// <summary>
    /// Run <see cref="InvoiceValidator"/> before writing and throw
    /// <see cref="InvoiceValidationException"/> when it reports errors. Default true.
    /// </summary>
    public bool ValidateBeforeWriting { get; set; } = true;
}

/// <summary>
/// Writes an <see cref="Invoice"/> as UN/CEFACT Cross Industry Invoice (CII, D16B), the
/// syntax of ZUGFeRD / Factur-X and one of the two syntaxes of XRechnung. Elements are
/// written in the order the D16B schema requires; the profile decides which business terms
/// are written (MINIMUM and BASIC WL carry no lines, BASIC no item details).
/// </summary>
public static class CiiWriter
{
    internal const string Rsm = "urn:un:unece:uncefact:data:standard:CrossIndustryInvoice:100";
    internal const string Ram = "urn:un:unece:uncefact:data:standard:ReusableAggregateBusinessInformationEntity:100";
    internal const string Udt = "urn:un:unece:uncefact:data:standard:UnqualifiedDataType:100";
    internal const string Qdt = "urn:un:unece:uncefact:data:standard:QualifiedDataType:100";

    /// <summary>The mandatory CII project name written when <see cref="Invoice.ProjectName"/> is empty.</summary>
    internal const string DefaultProjectName = "Project reference";

    /// <summary>Writes the invoice and returns the UTF-8 XML bytes.</summary>
    public static byte[] Write(Invoice invoice, InvoiceProfile profile, WriterOptions? options = null)
    {
        using var ms = new MemoryStream();
        Write(invoice, profile, ms, options);
        return ms.ToArray();
    }

    /// <summary>Writes the invoice and returns the XML as a string.</summary>
    public static string WriteToString(Invoice invoice, InvoiceProfile profile, WriterOptions? options = null)
        => new UTF8Encoding(false).GetString(Write(invoice, profile, options));

    /// <summary>Writes the invoice as UTF-8 XML to <paramref name="output"/>.</summary>
    public static void Write(Invoice invoice, InvoiceProfile profile, Stream output, WriterOptions? options = null)
    {
        options ??= new WriterOptions();
        if (options.ValidateBeforeWriting)
            InvoiceValidator.Validate(invoice, profile).ThrowIfInvalid();

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = options.Indent,
            IndentChars = "  ",
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.None,
        };
        using var xml = XmlWriter.Create(output, settings);
        new Emitter(xml, invoice, profile).Document();
    }

    private sealed class Emitter
    {
        private readonly XmlWriter _x;
        private readonly Invoice _inv;
        private readonly InvoiceProfile _profile;
        private readonly InvoiceTotals _totals;
        private readonly bool _minimum;
        private readonly bool _lines;
        private readonly bool _full;
        private readonly InvoiceExtension? _ext;
        private readonly bool _extended;

        public Emitter(XmlWriter x, Invoice invoice, InvoiceProfile profile)
        {
            _x = x;
            _inv = invoice;
            _profile = profile;
            _totals = InvoiceCalculator.Calculate(invoice);
            _minimum = profile == InvoiceProfile.FacturXMinimum;
            _lines = profile.HasLines();
            _full = profile is InvoiceProfile.XRechnung or InvoiceProfile.EN16931 or InvoiceProfile.FacturXExtended
                or InvoiceProfile.PeppolBis3;
            _extended = profile == InvoiceProfile.FacturXExtended;
            _ext = _extended ? invoice.Extended : null;
        }

        public void Document()
        {
            _x.WriteStartDocument();
            _x.WriteStartElement("rsm", "CrossIndustryInvoice", Rsm);
            _x.WriteAttributeString("xmlns", "qdt", null, Qdt);
            _x.WriteAttributeString("xmlns", "ram", null, Ram);
            _x.WriteAttributeString("xmlns", "udt", null, Udt);

            // ExchangedDocumentContext: BT-23 and BT-24
            _x.WriteStartElement("rsm", "ExchangedDocumentContext", Rsm);
            if (_ext?.IsTest is { } isTest) UdtIndicator("TestIndicator", isTest);
            if (!string.IsNullOrWhiteSpace(_inv.BusinessProcess))
            {
                Start("BusinessProcessSpecifiedDocumentContextParameter");
                Text("ID", _inv.BusinessProcess);
                End();
            }
            Start("GuidelineSpecifiedDocumentContextParameter");
            Text("ID", _profile.SpecificationIdentifier());
            End();
            _x.WriteEndElement();

            // ExchangedDocument: BT-1, BT-3, BT-2, BG-1
            _x.WriteStartElement("rsm", "ExchangedDocument", Rsm);
            Text("ID", _inv.Number);
            Text("Name", _ext?.DocumentName);
            Text("TypeCode", _inv.TypeCode.ToCode());
            DateTimeElement("IssueDateTime", _inv.IssueDate);
            if (_ext?.IsCopy is { } isCopy) UdtIndicator("CopyIndicator", isCopy);
            Text("LanguageID", _ext?.LanguageCode);
            if (!_minimum)
            {
                foreach (var note in _inv.Notes.Where(n => !string.IsNullOrWhiteSpace(n.Text)))
                {
                    Start("IncludedNote");
                    if (_extended) Text("ContentCode", note.ContentCode);
                    Text("Content", note.Text);
                    Text("SubjectCode", note.SubjectCode);
                    End();
                }
            }
            if (_ext?.ContractualDueDate is { } contractualDue)
            {
                Start("EffectiveSpecifiedPeriod");
                DateTimeElement("CompleteDateTime", contractualDue);
                End();
            }
            _x.WriteEndElement();

            _x.WriteStartElement("rsm", "SupplyChainTradeTransaction", Rsm);
            if (_lines)
            {
                for (int i = 0; i < _inv.Lines.Count; i++)
                    Line(_inv.Lines[i], _totals.LineNetAmounts[i], i);
            }
            HeaderAgreement();
            HeaderDelivery();
            HeaderSettlement();
            _x.WriteEndElement();

            _x.WriteEndElement();
            _x.WriteEndDocument();
        }

        // ── lines ─────────────────────────────────────────────────────────────

        private void Line(InvoiceLine line, decimal netAmount, int index)
        {
            Start("IncludedSupplyChainTradeLineItem");

            Start("AssociatedDocumentLineDocument");
            Text("LineID", string.IsNullOrWhiteSpace(line.Id) ? (index + 1).ToString(CultureInfo.InvariantCulture) : line.Id);
            if (!string.IsNullOrWhiteSpace(line.Note))
            {
                Start("IncludedNote");
                Text("Content", line.Note);
                End();
            }
            End();

            var item = line.Item;
            Start("SpecifiedTradeProduct");
            if (item.StandardIdentifier is { } gid && !string.IsNullOrWhiteSpace(gid.Value))
                Text("GlobalID", gid.Value, "schemeID", gid.Scheme);
            if (_full)
            {
                Text("SellerAssignedID", item.SellersIdentifier);
                Text("BuyerAssignedID", item.BuyersIdentifier);
            }
            Text("Name", item.Name);
            if (_full)
            {
                Text("Description", item.Description);
                foreach (var a in item.Attributes)
                {
                    Start("ApplicableProductCharacteristic");
                    Text("Description", a.Name);
                    Text("Value", a.Value);
                    End();
                }
                foreach (var c in item.Classifications)
                {
                    Start("DesignatedProductClassification");
                    _x.WriteStartElement("ram", "ClassCode", Ram);
                    _x.WriteAttributeString("listID", c.Scheme);
                    if (!string.IsNullOrWhiteSpace(c.SchemeVersion)) _x.WriteAttributeString("listVersionID", c.SchemeVersion);
                    _x.WriteString(c.Code);
                    _x.WriteEndElement();
                    End();
                }
                if (!string.IsNullOrWhiteSpace(item.OriginCountryCode))
                {
                    Start("OriginTradeCountry");
                    Text("ID", item.OriginCountryCode);
                    End();
                }
            }
            End();

            Start("SpecifiedLineTradeAgreement");
            if (_full && !string.IsNullOrWhiteSpace(line.OrderLineReference))
            {
                Start("BuyerOrderReferencedDocument");
                Text("LineID", line.OrderLineReference);
                End();
            }
            if (line.GrossPrice is { } gross)
            {
                Start("GrossPriceProductTradePrice");
                Text("ChargeAmount", Price(gross));
                BaseQuantity(line);
                decimal discount = line.PriceDiscount ?? gross - line.NetPrice;
                if (discount != 0m)
                {
                    Start("AppliedTradeAllowanceCharge");
                    Indicator(false);
                    Text("ActualAmount", Price(discount));
                    End();
                }
                End();
            }
            Start("NetPriceProductTradePrice");
            Text("ChargeAmount", Price(line.NetPrice));
            BaseQuantity(line);
            End();
            End();

            Start("SpecifiedLineTradeDelivery");
            Text("BilledQuantity", Quantity(line.Quantity), "unitCode", line.UnitCode);
            End();

            Start("SpecifiedLineTradeSettlement");
            Start("ApplicableTradeTax");
            Text("TypeCode", "VAT");
            Text("CategoryCode", line.VatCategory.ToCode());
            if (InvoiceCalculator.EffectiveRate(line.VatCategory, line.VatRate) is { } rate)
                Text("RateApplicablePercent", Percent(rate));
            End();
            Period("BillingSpecifiedPeriod", line.Period);
            foreach (var ac in line.AllowancesAndCharges)
                AllowanceCharge(ac, withTax: false);
            Start("SpecifiedTradeSettlementLineMonetarySummation");
            Text("LineTotalAmount", Amount(netAmount));
            End();
            if (_full && line.ObjectIdentifier is { } obj && !string.IsNullOrWhiteSpace(obj.Value))
            {
                Start("AdditionalReferencedDocument");
                Text("IssuerAssignedID", obj.Value);
                Text("TypeCode", "130");
                Text("ReferenceTypeCode", obj.Scheme);
                End();
            }
            if (_full && !string.IsNullOrWhiteSpace(line.BuyerAccountingReference))
            {
                Start("ReceivableSpecifiedTradeAccountingAccount");
                Text("ID", line.BuyerAccountingReference);
                End();
            }
            End();

            End();
        }

        private void BaseQuantity(InvoiceLine line)
        {
            if (line.PriceBaseQuantity is { } q)
                Text("BasisQuantity", Quantity(q), "unitCode", line.PriceBaseQuantityUnitCode ?? line.UnitCode);
        }

        // ── header ────────────────────────────────────────────────────────────

        private void HeaderAgreement()
        {
            Start("ApplicableHeaderTradeAgreement");
            Text("BuyerReference", _inv.BuyerReference);
            TradeParty("SellerTradeParty", _inv.Seller, seller: true);
            TradeParty("BuyerTradeParty", _inv.Buyer, seller: false);

            ExtendedPartyElement("SalesAgentTradeParty", _ext?.SalesAgent);
            ExtendedPartyElement("BuyerTaxRepresentativeTradeParty", _ext?.BuyerTaxRepresentative);
            if (_ext?.SellerTaxRepresentative is not null)
            {
                ExtendedPartyElement("SellerTaxRepresentativeTradeParty", _ext.SellerTaxRepresentative);
            }
            else if (!_minimum && _inv.SellerTaxRepresentative is { } rep)
            {
                Start("SellerTaxRepresentativeTradeParty");
                Text("Name", rep.Name);
                Address(rep.Address);
                TaxRegistration("VA", rep.VatIdentifier);
                End();
            }

            ExtendedPartyElement("ProductEndUserTradeParty", _ext?.ProductEndUser);
            if (_ext?.DeliveryTerms is { } terms)
            {
                Start("ApplicableTradeDeliveryTerms");
                Text("DeliveryTypeCode", terms.Code);
                if (Has(terms.LocationCountryCode) || Has(terms.LocationName))
                {
                    Start("RelevantTradeLocation");
                    Text("CountryID", terms.LocationCountryCode);
                    Text("Name", terms.LocationName);
                    End();
                }
                End();
            }

            if (_full) ReferencedId("SellerOrderReferencedDocument", _inv.SalesOrderReference, _ext?.SalesOrderDate);
            ReferencedId("BuyerOrderReferencedDocument", _inv.PurchaseOrderReference, _ext?.PurchaseOrderDate);
            if (_ext?.Quotation is { } quotation) ReferencedId("QuotationReferencedDocument", quotation.Number, quotation.Date);
            if (!_minimum) ReferencedId("ContractReferencedDocument", _inv.ContractReference, _ext?.ContractDate, _ext?.ContractTypeCode);

            if (_full)
            {
                foreach (var doc in _inv.SupportingDocuments)
                {
                    Start("AdditionalReferencedDocument");
                    Text("IssuerAssignedID", doc.Reference);
                    Text("URIID", doc.ExternalLocation);
                    Text("TypeCode", "916");
                    Text("Name", doc.Description);
                    if (doc.Content is { Length: > 0 } content)
                    {
                        _x.WriteStartElement("ram", "AttachmentBinaryObject", Ram);
                        _x.WriteAttributeString("mimeCode", doc.MimeCode ?? "application/pdf");
                        _x.WriteAttributeString("filename", doc.FileName ?? doc.Reference);
                        _x.WriteString(Convert.ToBase64String(content));
                        _x.WriteEndElement();
                    }
                    if (_extended && doc.IssueDate is { } docDate) FormattedDate(docDate);
                    End();
                }
                if (!string.IsNullOrWhiteSpace(_inv.TenderOrLotReference))
                {
                    Start("AdditionalReferencedDocument");
                    Text("IssuerAssignedID", _inv.TenderOrLotReference);
                    Text("TypeCode", "50");
                    if (_ext?.TenderOrLotDate is { } tenderDate) FormattedDate(tenderDate);
                    End();
                }
                if (_inv.InvoicedObjectIdentifier is { } obj && !string.IsNullOrWhiteSpace(obj.Value))
                {
                    Start("AdditionalReferencedDocument");
                    Text("IssuerAssignedID", obj.Value);
                    Text("TypeCode", "130");
                    Text("ReferenceTypeCode", obj.Scheme);
                    if (_ext?.InvoicedObjectDate is { } objectDate) FormattedDate(objectDate);
                    End();
                }
                ExtendedPartyElement("BuyerAgentTradeParty", _ext?.BuyerAgent);
                if (!string.IsNullOrWhiteSpace(_inv.ProjectReference))
                {
                    Start("SpecifiedProcuringProject");
                    Text("ID", _inv.ProjectReference);
                    Text("Name", Has(_inv.ProjectName) ? _inv.ProjectName : DefaultProjectName);
                    End();
                }
                if (_ext is not null)
                    foreach (var order in _ext.UltimateCustomerOrders)
                        ReferencedId("UltimateCustomerOrderReferencedDocument", order.Number, order.Date);
            }
            End();
        }

        private void TradeParty(string element, Party party, bool seller)
        {
            Start(element);
            if (!_minimum)
            {
                // BT-29 is repeatable for the seller, BT-46 occurs once for the buyer.
                var ids = seller ? party.Identifiers : party.Identifiers.Take(1).ToList();
                foreach (var id in ids.Where(i => string.IsNullOrWhiteSpace(i.Scheme)))
                    Text("ID", id.Value);
                foreach (var id in ids.Where(i => !string.IsNullOrWhiteSpace(i.Scheme)))
                    Text("GlobalID", id.Value, "schemeID", id.Scheme);
            }
            Text("Name", party.Name);
            if (_extended) Text("RoleCode", party.RoleCode);
            if ((seller && _full) || _extended) Text("Description", party.AdditionalLegalInformation);

            bool tradingName = !_minimum && (seller || _full) && !string.IsNullOrWhiteSpace(party.TradingName);
            var legalAddress = _extended ? party.LegalAddress : null;
            if (party.LegalRegistrationIdentifier is { } legal && !string.IsNullOrWhiteSpace(legal.Value) || tradingName || legalAddress is not null)
            {
                Start("SpecifiedLegalOrganization");
                if (party.LegalRegistrationIdentifier is { } lr && !string.IsNullOrWhiteSpace(lr.Value))
                    Text("ID", lr.Value, "schemeID", lr.Scheme);
                if (tradingName) Text("TradingBusinessName", party.TradingName);
                if (legalAddress is not null) Address(legalAddress);
                End();
            }

            if (_full && party.Contact is { } c) ContactElement(c);
            if (_extended)
                foreach (var additional in party.AdditionalContacts)
                    ContactElement(additional);

            // MINIMUM carries the seller with its country only and the buyer without an address.
            if (_minimum)
            {
                if (seller)
                {
                    Start("PostalTradeAddress");
                    Text("CountryID", party.Address.CountryCode);
                    End();
                }
            }
            else
            {
                Address(party.Address);
            }

            if (!_minimum && party.ElectronicAddress is { } ea && Has(ea.Value))
            {
                Start("URIUniversalCommunication");
                Text("URIID", ea.Value, "schemeID", ea.Scheme);
                End();
            }

            if (seller || !_minimum) TaxRegistration("VA", party.VatIdentifier);
            if (seller) TaxRegistration("FC", party.TaxRegistrationIdentifier);
            End();
        }

        private void ContactElement(Contact c)
        {
            bool typeOrFax = _extended && (Has(c.TypeCode) || Has(c.Fax));
            if (!Has(c.Name) && !Has(c.Department) && !Has(c.Phone) && !Has(c.Email) && !typeOrFax) return;
            Start("DefinedTradeContact");
            Text("PersonName", c.Name);
            Text("DepartmentName", c.Department);
            if (_extended) Text("TypeCode", c.TypeCode);
            if (Has(c.Phone))
            {
                Start("TelephoneUniversalCommunication");
                Text("CompleteNumber", c.Phone);
                End();
            }
            if (_extended && Has(c.Fax))
            {
                Start("FaxUniversalCommunication");
                Text("CompleteNumber", c.Fax);
                End();
            }
            if (Has(c.Email))
            {
                Start("EmailURIUniversalCommunication");
                Text("URIID", c.Email);
                End();
            }
            End();
        }

        private void ExtendedPartyElement(string element, ExtendedParty? party)
        {
            if (party is null) return;
            Start(element);
            Text("ID", party.Identifier);
            foreach (var id in party.GlobalIdentifiers.Where(i => Has(i.Value)))
                Text("GlobalID", id.Value, "schemeID", id.Scheme);
            Text("Name", party.Name);
            Text("RoleCode", party.RoleCode);
            bool hasLegalId = party.LegalRegistrationIdentifier is { } l && Has(l.Value);
            if (hasLegalId || Has(party.TradingName) || party.LegalAddress is not null)
            {
                Start("SpecifiedLegalOrganization");
                if (hasLegalId) Text("ID", party.LegalRegistrationIdentifier!.Value, "schemeID", party.LegalRegistrationIdentifier.Scheme);
                Text("TradingBusinessName", party.TradingName);
                if (party.LegalAddress is not null) Address(party.LegalAddress);
                End();
            }
            foreach (var contact in party.Contacts) ContactElement(contact);
            if (party.Address is not null) Address(party.Address);
            if (party.ElectronicAddress is { } ea && Has(ea.Value))
            {
                Start("URIUniversalCommunication");
                Text("URIID", ea.Value, "schemeID", ea.Scheme);
                End();
            }
            TaxRegistration("VA", party.VatIdentifier);
            End();
        }

        private void Address(PostalAddress a)
        {
            Start("PostalTradeAddress");
            Text("PostcodeCode", a.PostCode);
            Text("LineOne", a.Line1);
            Text("LineTwo", a.Line2);
            Text("LineThree", a.Line3);
            Text("CityName", a.City);
            Text("CountryID", a.CountryCode);
            Text("CountrySubDivisionName", a.CountrySubdivision);
            End();
        }

        private void TaxRegistration(string scheme, string? value)
        {
            if (!Has(value)) return;
            Start("SpecifiedTaxRegistration");
            Text("ID", value, "schemeID", scheme);
            End();
        }

        private void HeaderDelivery()
        {
            Start("ApplicableHeaderTradeDelivery");
            if (_ext is { TransportModeCodes.Count: > 0 })
            {
                Start("RelatedSupplyChainConsignment");
                foreach (string mode in _ext.TransportModeCodes)
                {
                    Start("SpecifiedLogisticsTransportMovement");
                    Text("ModeCode", mode);
                    End();
                }
                End();
            }
            if (_ext?.ShipTo is not null) ExtendedPartyElement("ShipToTradeParty", _ext.ShipTo);
            if (!_minimum && _inv.Delivery is { } d)
            {
                bool party = _ext?.ShipTo is null && (Has(d.PartyName) || d.LocationIdentifier is not null || d.Address is not null);
                if (party)
                {
                    Start("ShipToTradeParty");
                    if (d.LocationIdentifier is { } loc && Has(loc.Value))
                    {
                        if (Has(loc.Scheme)) Text("GlobalID", loc.Value, "schemeID", loc.Scheme);
                        else Text("ID", loc.Value);
                    }
                    Text("Name", d.PartyName);
                    if (d.Address is not null) Address(d.Address);
                    End();
                }
            }
            ExtendedPartyElement("UltimateShipToTradeParty", _ext?.UltimateShipTo);
            ExtendedPartyElement("ShipFromTradeParty", _ext?.ShipFrom);
            if (!_minimum && _inv.Delivery is { } delivered)
            {
                if (delivered.ActualDeliveryDate is { } date)
                {
                    Start("ActualDeliverySupplyChainEvent");
                    DateTimeElement("OccurrenceDateTime", date);
                    End();
                }
            }
            if (!_minimum)
            {
                ReferencedId("DespatchAdviceReferencedDocument", _inv.DespatchAdviceReference, _ext?.DespatchAdviceDate);
                if (_full) ReferencedId("ReceivingAdviceReferencedDocument", _inv.ReceivingAdviceReference, _ext?.ReceivingAdviceDate);
                if (_ext?.DeliveryNote is { } note) ReferencedId("DeliveryNoteReferencedDocument", note.Number, note.Date);
            }
            End();
        }

        private void HeaderSettlement()
        {
            Start("ApplicableHeaderTradeSettlement");
            var pay = _inv.PaymentInstructions;
            if (!_minimum)
            {
                Text("CreditorReferenceID", pay?.DirectDebit?.CreditorIdentifier);
                Text("PaymentReference", pay?.RemittanceInformation);
                Text("TaxCurrencyCode", _inv.TaxCurrency);
            }
            Text("InvoiceCurrencyCode", _inv.Currency);
            Text("InvoiceIssuerReference", _ext?.InvoiceIssuerReference);
            ExtendedPartyElement("InvoicerTradeParty", _ext?.Invoicer);
            ExtendedPartyElement("InvoiceeTradeParty", _ext?.Invoicee);

            if (_ext?.Payee is not null)
            {
                ExtendedPartyElement("PayeeTradeParty", _ext.Payee);
            }
            else if (!_minimum && _inv.Payee is { } payee)
            {
                Start("PayeeTradeParty");
                if (payee.Identifier is { } pid && Has(pid.Value))
                {
                    if (Has(pid.Scheme)) Text("GlobalID", pid.Value, "schemeID", pid.Scheme);
                    else Text("ID", pid.Value);
                }
                Text("Name", payee.Name);
                if (payee.LegalRegistrationIdentifier is { } plr && Has(plr.Value))
                {
                    Start("SpecifiedLegalOrganization");
                    Text("ID", plr.Value, "schemeID", plr.Scheme);
                    End();
                }
                End();
            }

            ExtendedPartyElement("PayerTradeParty", _ext?.Payer);

            if (!_minimum && pay is not null) PaymentMeans(pay);

            if (!_minimum)
            {
                foreach (var b in _totals.VatBreakdown)
                {
                    Start("ApplicableTradeTax");
                    Text("CalculatedAmount", Amount(b.TaxAmount));
                    Text("TypeCode", "VAT");
                    Text("ExemptionReason", b.ExemptionReason);
                    Text("BasisAmount", Amount(b.TaxableAmount));
                    Text("CategoryCode", b.Category.ToCode());
                    Text("ExemptionReasonCode", b.ExemptionReasonCode);
                    if (_full && _inv.TaxPointDate is { } tp)
                    {
                        Start("TaxPointDate");
                        _x.WriteStartElement("udt", "DateString", Udt);
                        _x.WriteAttributeString("format", "102");
                        _x.WriteString(tp.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
                        _x.WriteEndElement();
                        End();
                    }
                    Text("DueDateTypeCode", _inv.TaxPointDateCode);
                    if (b.Rate is { } rate) Text("RateApplicablePercent", Percent(rate));
                    End();
                }

                Period("BillingSpecifiedPeriod", _inv.InvoicingPeriod);
                foreach (var ac in _inv.AllowancesAndCharges)
                    AllowanceCharge(ac, withTax: true);
            }

            string? terms = PaymentTermsText();
            string? mandate = pay?.DirectDebit?.MandateReference;
            if (!_minimum && (terms is not null || _inv.DueDate.HasValue || Has(mandate)))
            {
                Start("SpecifiedTradePaymentTerms");
                if (terms is not null)
                {
                    // Written as-is: the XRechnung Skonto syntax needs its line breaks,
                    // including the final one (BR-DE-18).
                    _x.WriteStartElement("ram", "Description", Ram);
                    _x.WriteString(terms);
                    _x.WriteEndElement();
                }
                if (_inv.DueDate is { } due) DateTimeElement("DueDateDateTime", due);
                Text("DirectDebitMandateID", mandate);
                End();
            }

            Start("SpecifiedTradeSettlementHeaderMonetarySummation");
            if (!_minimum)
            {
                Text("LineTotalAmount", Amount(_totals.LineNetTotal));
                if (_inv.AllowancesAndCharges.Any(a => a.IsCharge)) Text("ChargeTotalAmount", Amount(_totals.ChargeTotal));
                if (_inv.AllowancesAndCharges.Any(a => !a.IsCharge)) Text("AllowanceTotalAmount", Amount(_totals.AllowanceTotal));
            }
            Text("TaxBasisTotalAmount", Amount(_totals.TaxExclusiveAmount));
            Text("TaxTotalAmount", Amount(_totals.VatTotal), "currencyID", _inv.Currency);
            if (Has(_inv.TaxCurrency) && _inv.VatTotalInTaxCurrency is { } vatInTaxCurrency)
                Text("TaxTotalAmount", Amount(vatInTaxCurrency), "currencyID", _inv.TaxCurrency);
            if (_full && _inv.RoundingAmount is { } rounding) Text("RoundingAmount", Amount(rounding));
            Text("GrandTotalAmount", Amount(_totals.TaxInclusiveAmount));
            if (!_minimum && _inv.PrepaidAmount is { } prepaid) Text("TotalPrepaidAmount", Amount(prepaid));
            Text("DuePayableAmount", Amount(_full || _minimum ? _totals.AmountDue : _totals.AmountDue - (_totals.RoundingAmount ?? 0m)));
            End();

            if (!_minimum)
            {
                foreach (var p in _inv.PrecedingInvoices.Where(p => Has(p.Number)))
                {
                    Start("InvoiceReferencedDocument");
                    Text("IssuerAssignedID", p.Number);
                    if (_extended) Text("TypeCode", p.TypeCode);
                    if (p.IssueDate is { } d) FormattedDate(d);
                    End();
                }
                if (Has(_inv.BuyerAccountingReference))
                {
                    Start("ReceivableSpecifiedTradeAccountingAccount");
                    Text("ID", _inv.BuyerAccountingReference);
                    End();
                }
            }
            End();
        }

        private void PaymentMeans(PaymentInstructions pay)
        {
            void Header()
            {
                Text("TypeCode", pay.MeansCode);
                if (_full) Text("Information", pay.MeansText);
            }

            void Card()
            {
                if (_full && pay.Card is { } card && Has(card.PrimaryAccountNumber))
                {
                    Start("ApplicableTradeSettlementFinancialCard");
                    Text("ID", card.PrimaryAccountNumber);
                    Text("CardholderName", card.HolderName);
                    End();
                }
            }

            void Debtor()
            {
                if (Has(pay.DirectDebit?.DebitedAccountIdentifier))
                {
                    Start("PayerPartyDebtorFinancialAccount");
                    Text("IBANID", Iban.Compact(pay.DirectDebit!.DebitedAccountIdentifier!));
                    End();
                }
            }

            var accounts = pay.CreditTransfers.Where(a => Has(a.AccountIdentifier)).ToList();
            if (accounts.Count == 0)
            {
                Start("SpecifiedTradeSettlementPaymentMeans");
                Header();
                Card();
                Debtor();
                End();
                return;
            }

            foreach (var account in accounts)
            {
                Start("SpecifiedTradeSettlementPaymentMeans");
                Header();
                Card();
                Debtor();
                Start("PayeePartyCreditorFinancialAccount");
                string compact = Iban.Compact(account.AccountIdentifier);
                if (Iban.IsValid(compact)) Text("IBANID", compact);
                if (_full) Text("AccountName", account.AccountName);
                if (!Iban.IsValid(compact)) Text("ProprietaryID", account.AccountIdentifier.Trim());
                End();
                if (_full && Has(account.ServiceProviderIdentifier))
                {
                    Start("PayeeSpecifiedCreditorFinancialInstitution");
                    Text("BICID", account.ServiceProviderIdentifier!.Trim());
                    End();
                }
                End();
            }
        }

        /// <summary>BT-20 with the cash discounts appended in the XRechnung Skonto syntax.</summary>
        private string? PaymentTermsText() => PaymentTermsFormatter.Format(_inv);

        private void AllowanceCharge(AllowanceCharge ac, bool withTax)
        {
            Start("SpecifiedTradeAllowanceCharge");
            Indicator(ac.IsCharge);
            // BASIC has the base and percentage at document level only (withTax).
            if ((_full || withTax) && ac.Percentage is { } pct) Text("CalculationPercent", Percent(pct));
            if ((_full || withTax) && ac.BaseAmount is { } basis) Text("BasisAmount", Amount(basis));
            Text("ActualAmount", Amount(InvoiceCalculator.AmountOf(ac)));
            Text("ReasonCode", ac.ReasonCode);
            Text("Reason", ac.Reason);
            if (withTax)
            {
                Start("CategoryTradeTax");
                Text("TypeCode", "VAT");
                Text("CategoryCode", ac.VatCategory.ToCode());
                if (InvoiceCalculator.EffectiveRate(ac.VatCategory, ac.VatRate) is { } rate)
                    Text("RateApplicablePercent", Percent(rate));
                End();
            }
            End();
        }

        private void Indicator(bool value)
        {
            Start("ChargeIndicator");
            _x.WriteStartElement("udt", "Indicator", Udt);
            _x.WriteString(value ? "true" : "false");
            _x.WriteEndElement();
            End();
        }

        private void Period(string element, Period? period)
        {
            if (period is null || (period.Start is null && period.End is null)) return;
            Start(element);
            if (period.Start is { } s) DateTimeElement("StartDateTime", s);
            if (period.End is { } e) DateTimeElement("EndDateTime", e);
            End();
        }

        private void ReferencedId(string element, string? id, DateTime? date = null, string? referenceTypeCode = null)
        {
            if (!Has(id)) return;
            Start(element);
            Text("IssuerAssignedID", id);
            Text("ReferenceTypeCode", referenceTypeCode);
            if (date is { } d) FormattedDate(d);
            End();
        }

        private void FormattedDate(DateTime date)
        {
            Start("FormattedIssueDateTime");
            _x.WriteStartElement("qdt", "DateTimeString", Qdt);
            _x.WriteAttributeString("format", "102");
            _x.WriteString(date.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
            _x.WriteEndElement();
            End();
        }

        private void UdtIndicator(string element, bool value)
        {
            _x.WriteStartElement("ram", element, Ram);
            _x.WriteStartElement("udt", "Indicator", Udt);
            _x.WriteString(value ? "true" : "false");
            _x.WriteEndElement();
            _x.WriteEndElement();
        }

        // ── primitives ────────────────────────────────────────────────────────

        private void Start(string name) => _x.WriteStartElement("ram", name, Ram);

        private void End() => _x.WriteEndElement();

        private void Text(string name, string? value, string? attribute = null, string? attributeValue = null)
        {
            if (!Has(value)) return;
            _x.WriteStartElement("ram", name, Ram);
            if (attribute is not null && Has(attributeValue)) _x.WriteAttributeString(attribute, attributeValue!.Trim());
            _x.WriteString(value!.Trim());
            _x.WriteEndElement();
        }

        private void DateTimeElement(string name, DateTime date)
        {
            Start(name);
            _x.WriteStartElement("udt", "DateTimeString", Udt);
            _x.WriteAttributeString("format", "102");
            _x.WriteString(date.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
            _x.WriteEndElement();
            End();
        }

        private static bool Has(string? s) => !string.IsNullOrWhiteSpace(s);
    }

    internal static string Amount(decimal value) => InvoiceCalculator.Round(value).ToString("0.00", CultureInfo.InvariantCulture);

    internal static string Price(decimal value) => value.ToString("0.00####", CultureInfo.InvariantCulture);

    internal static string Quantity(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    internal static string Percent(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
