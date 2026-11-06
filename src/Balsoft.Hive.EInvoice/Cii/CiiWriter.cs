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
            _totals = InvoiceCalculator.Calculate(invoice, profile);
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
            var lx = _extended ? line.Extended : null;
            Start("IncludedSupplyChainTradeLineItem");

            Start("AssociatedDocumentLineDocument");
            Text("LineID", string.IsNullOrWhiteSpace(line.Id) ? (index + 1).ToString(CultureInfo.InvariantCulture) : line.Id);
            Text("ParentLineID", lx?.ParentLineId);
            Text("LineStatusCode", lx?.LineStatusCode);
            Text("LineStatusReasonCode", lx?.LineStatusReasonCode);
            if (!string.IsNullOrWhiteSpace(line.Note))
            {
                Start("IncludedNote");
                Text("ContentCode", lx?.NoteContentCode);
                Text("Content", line.Note);
                Text("SubjectCode", lx?.NoteSubjectCode);
                End();
            }
            End();

            var item = line.Item;
            Start("SpecifiedTradeProduct");
            Text("ID", lx?.ItemIdentifier);
            if (item.StandardIdentifier is { } gid && !string.IsNullOrWhiteSpace(gid.Value))
                Text("GlobalID", gid.Value, "schemeID", gid.Scheme);
            if (_full)
            {
                Text("SellerAssignedID", item.SellersIdentifier);
                Text("BuyerAssignedID", item.BuyersIdentifier);
            }
            Text("IndustryAssignedID", lx?.IndustryAssignedIdentifier);
            Text("ModelID", lx?.ModelIdentifier);
            Text("Name", item.Name);
            if (_full)
            {
                Text("Description", item.Description);
                if (lx is not null)
                {
                    foreach (string batch in lx.BatchIdentifiers) Text("BatchID", batch);
                    Text("BrandName", lx.BrandName);
                    Text("ModelName", lx.ModelName);
                }
                foreach (var a in item.Attributes)
                {
                    Start("ApplicableProductCharacteristic");
                    if (_extended) Text("TypeCode", a.TypeCode);
                    Text("Description", a.Name);
                    // BR-FXEXT-BR-54-2: a value or a measure, not both.
                    if (_extended && a.ValueMeasure is { } measure) Text("ValueMeasure", Quantity(measure), "unitCode", a.ValueMeasureUnitCode);
                    else Text("Value", a.Value);
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
                    if (_extended) Text("ClassName", c.ClassName);
                    End();
                }
                if (lx is not null)
                    foreach (var instance in lx.ProductInstances)
                    {
                        Start("IndividualTradeProductInstance");
                        Text("BatchID", instance.BatchIdentifier);
                        Text("SupplierAssignedSerialID", instance.SerialNumber);
                        End();
                    }
                if (!string.IsNullOrWhiteSpace(item.OriginCountryCode))
                {
                    Start("OriginTradeCountry");
                    Text("ID", item.OriginCountryCode);
                    End();
                }
                if (lx is not null)
                {
                    ExtendedPartyElement("ManufacturerTradeParty", lx.Manufacturer, lineLevel: true);
                    foreach (var included in lx.IncludedProducts)
                    {
                        Start("IncludedReferencedProduct");
                        Text("ID", included.Identifier);
                        foreach (var id in included.GlobalIdentifiers.Where(i => Has(i.Value)))
                            Text("GlobalID", id.Value, "schemeID", id.Scheme);
                        Text("SellerAssignedID", included.SellersIdentifier);
                        Text("BuyerAssignedID", included.BuyersIdentifier);
                        Text("IndustryAssignedID", included.IndustryAssignedIdentifier);
                        Text("Name", included.Name);
                        Text("Description", included.Description);
                        if (included.Quantity is { } unitQuantity) Text("UnitQuantity", Quantity(unitQuantity), "unitCode", included.QuantityUnitCode);
                        End();
                    }
                }
            }
            End();

            Start("SpecifiedLineTradeAgreement");
            DeliveryTermsElement(lx?.DeliveryTerms);
            LineReference("SellerOrderReferencedDocument", lx?.SalesOrder);
            if ((_full && !string.IsNullOrWhiteSpace(line.OrderLineReference)) || Has(lx?.PurchaseOrderNumber) || lx?.PurchaseOrderDate is not null)
            {
                Start("BuyerOrderReferencedDocument");
                Text("IssuerAssignedID", lx?.PurchaseOrderNumber);
                Text("LineID", line.OrderLineReference);
                if (lx?.PurchaseOrderDate is { } orderDate) FormattedDate(orderDate);
                End();
            }
            LineReference("QuotationReferencedDocument", lx?.Quotation);
            LineReference("ContractReferencedDocument", lx?.Contract);
            if (lx is not null)
                foreach (var document in lx.SupportingDocuments)
                {
                    Start("AdditionalReferencedDocument");
                    Text("IssuerAssignedID", document.Number);
                    Text("URIID", document.ExternalLocation);
                    Text("LineID", document.LineId);
                    Text("TypeCode", document.TypeCode);
                    Text("Name", document.Description);
                    if (document.Content is { Length: > 0 } content) Attachment(content, document.MimeCode, document.FileName ?? document.Number);
                    Text("ReferenceTypeCode", document.ReferenceTypeCode);
                    if (document.Date is { } documentDate) FormattedDate(documentDate);
                    End();
                }
            if (line.GrossPrice is { } gross)
            {
                Start("GrossPriceProductTradePrice");
                Text("ChargeAmount", Price(gross));
                BaseQuantity(line);
                if (lx is { GrossPriceAllowancesAndCharges.Count: > 0 })
                {
                    foreach (var ac in lx.GrossPriceAllowancesAndCharges)
                    {
                        Start("AppliedTradeAllowanceCharge");
                        Indicator(ac.IsCharge);
                        if (ac.Percentage is { } pricePercent) Text("CalculationPercent", Percent(pricePercent));
                        if (ac.BaseAmount is { } priceBase) Text("BasisAmount", Price(priceBase));
                        Text("ActualAmount", Price(ac.Amount ?? InvoiceCalculator.AmountOf(ac)));
                        Text("ReasonCode", ac.ReasonCode, "listID", ac.ReasonCodeListId);
                        Text("Reason", ac.Reason);
                        End();
                    }
                }
                else
                {
                    decimal discount = line.PriceDiscount ?? gross - line.NetPrice;
                    if (discount != 0m)
                    {
                        Start("AppliedTradeAllowanceCharge");
                        Indicator(false);
                        Text("ActualAmount", Price(discount));
                        End();
                    }
                }
                End();
            }
            Start("NetPriceProductTradePrice");
            Text("ChargeAmount", Price(line.NetPrice));
            BaseQuantity(line);
            if (lx?.NetPriceIncludedTax is { } includedTax) ExtendedTaxElement("IncludedTradeTax", includedTax);
            End();
            ExtendedPartyElement("ItemSellerTradeParty", lx?.ItemSeller, lineLevel: true);
            if (lx is not null)
                foreach (var order in lx.UltimateCustomerOrders)
                    LineReference("UltimateCustomerOrderReferencedDocument", order);
            End();

            Start("SpecifiedLineTradeDelivery");
            Text("BilledQuantity", Quantity(line.Quantity), "unitCode", line.UnitCode);
            if (lx?.ChargeFreeQuantity is { } free) Text("ChargeFreeQuantity", Quantity(free), "unitCode", lx.ChargeFreeQuantityUnitCode);
            if (lx?.PackageQuantity is { } packages) Text("PackageQuantity", Quantity(packages), "unitCode", lx.PackageQuantityUnitCode);
            if (lx?.PerPackageUnitQuantity is { } perPackage) Text("PerPackageUnitQuantity", Quantity(perPackage), "unitCode", lx.PerPackageUnitCode);
            ExtendedPartyElement("ShipToTradeParty", lx?.ShipTo, lineLevel: true);
            ExtendedPartyElement("UltimateShipToTradeParty", lx?.UltimateShipTo, lineLevel: true);
            if (lx?.DeliveryDate is { } delivered)
            {
                Start("ActualDeliverySupplyChainEvent");
                DateTimeElement("OccurrenceDateTime", delivered);
                End();
            }
            LineReference("DespatchAdviceReferencedDocument", lx?.DespatchAdvice);
            LineReference("ReceivingAdviceReferencedDocument", lx?.ReceivingAdvice);
            LineReference("DeliveryNoteReferencedDocument", lx?.DeliveryNote);
            End();

            Start("SpecifiedLineTradeSettlement");
            Start("ApplicableTradeTax");
            if (lx?.VatAmount is { } lineVat) Text("CalculatedAmount", Amount(lineVat));
            Text("TypeCode", "VAT");
            Text("ExemptionReason", lx?.VatExemptionReason);
            Text("CategoryCode", line.VatCategory.ToCode());
            Text("ExemptionReasonCode", lx?.VatExemptionReasonCode);
            Text("DueDateTypeCode", lx?.VatPointDateCode);
            if (InvoiceCalculator.EffectiveRate(line.VatCategory, line.VatRate) is { } rate)
                Text("RateApplicablePercent", Percent(rate));
            End();
            Period("BillingSpecifiedPeriod", line.Period);
            foreach (var ac in line.AllowancesAndCharges)
                AllowanceCharge(ac, withTax: false);
            Start("SpecifiedTradeSettlementLineMonetarySummation");
            Text("LineTotalAmount", Amount(netAmount));
            if (lx is not null)
            {
                if (lx.ChargeTotal is { } lineCharges) Text("ChargeTotalAmount", Amount(lineCharges));
                if (lx.AllowanceTotal is { } lineAllowances) Text("AllowanceTotalAmount", Amount(lineAllowances));
                if (lx.VatTotal is { } lineVatTotal) Text("TaxTotalAmount", Amount(lineVatTotal), "currencyID", lx.VatTotalCurrency);
                if (lx.VatTotalInTaxCurrency is { } lineVatInTax) Text("TaxTotalAmount", Amount(lineVatInTax), "currencyID", lx.VatTotalInTaxCurrencyCode);
                if (lx.GrandTotal is { } lineGrand) Text("GrandTotalAmount", Amount(lineGrand));
                if (lx.AllowanceChargeTotal is { } lineAc) Text("TotalAllowanceChargeAmount", Amount(lineAc));
            }
            End();
            if (lx?.PrecedingInvoice is { } preceding)
            {
                Start("InvoiceReferencedDocument");
                Text("IssuerAssignedID", preceding.Number);
                Text("LineID", preceding.LineId);
                Text("TypeCode", lx.PrecedingInvoiceTypeCode);
                if (preceding.Date is { } precedingDate) FormattedDate(precedingDate);
                End();
            }
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
                Text("TypeCode", lx?.AccountingTypeCode);
                End();
            }
            End();

            End();
        }

        private void LineReference(string element, LineDocumentReference? reference)
        {
            if (reference is null) return;
            Start(element);
            Text("IssuerAssignedID", reference.Number);
            Text("LineID", reference.LineId);
            if (reference.Date is { } date) FormattedDate(date);
            End();
        }

        private void Attachment(byte[] content, string? mimeCode, string fileName)
        {
            _x.WriteStartElement("ram", "AttachmentBinaryObject", Ram);
            _x.WriteAttributeString("mimeCode", mimeCode ?? "application/pdf");
            _x.WriteAttributeString("filename", fileName);
            _x.WriteString(Convert.ToBase64String(content));
            _x.WriteEndElement();
        }

        private void DeliveryTermsElement(DeliveryTerms? terms)
        {
            if (terms is null) return;
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
            DeliveryTermsElement(_ext?.DeliveryTerms);

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

        private void ExtendedPartyElement(string element, ExtendedParty? party, bool lineLevel = false)
        {
            if (party is null) return;
            Start(element);
            Text("ID", party.Identifier);
            foreach (var id in party.GlobalIdentifiers.Where(i => Has(i.Value)))
                Text("GlobalID", id.Value, "schemeID", id.Scheme);
            Text("Name", party.Name);
            Text("RoleCode", party.RoleCode);
            Text("Description", party.Description);
            bool hasLegalId = party.LegalRegistrationIdentifier is { } l && Has(l.Value);
            // The official list has the legal address at document level only.
            var legalAddress = lineLevel ? null : party.LegalAddress;
            if (hasLegalId || Has(party.TradingName) || legalAddress is not null)
            {
                Start("SpecifiedLegalOrganization");
                if (hasLegalId) Text("ID", party.LegalRegistrationIdentifier!.Value, "schemeID", party.LegalRegistrationIdentifier.Scheme);
                Text("TradingBusinessName", party.TradingName);
                if (legalAddress is not null) Address(legalAddress);
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
            TaxRegistration("FC", party.TaxRegistrationIdentifier);
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
            if (_ext?.TaxCurrencyExchange is { } exchange)
            {
                Start("TaxApplicableTradeCurrencyExchange");
                Text("SourceCurrencyCode", exchange.SourceCurrencyCode);
                Text("TargetCurrencyCode", exchange.TargetCurrencyCode);
                Text("ConversionRate", exchange.ConversionRate.ToString("0.######", CultureInfo.InvariantCulture));
                if (exchange.Date is { } exchangeDate) DateTimeElement("ConversionRateDateTime", exchangeDate);
                End();
            }

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
                    if (_ext?.WriteVatBasisDetails == true)
                    {
                        decimal lines = _inv.Lines.Where(l => l.VatCategory == b.Category && InvoiceCalculator.EffectiveRate(l.VatCategory, l.VatRate) == b.Rate).Sum(InvoiceCalculator.LineNetAmount);
                        Text("LineTotalBasisAmount", Amount(lines));
                        Text("AllowanceChargeBasisAmount", Amount(b.TaxableAmount - lines));
                    }
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

                Period("BillingSpecifiedPeriod", _inv.InvoicingPeriod, _ext?.InvoicingPeriodDescription);
                foreach (var ac in _inv.AllowancesAndCharges)
                    AllowanceCharge(ac, withTax: true);
                if (_ext is not null)
                    foreach (var charge in _ext.LogisticsCharges)
                    {
                        Start("SpecifiedLogisticsServiceCharge");
                        Text("Description", charge.Description);
                        Text("AppliedAmount", Amount(charge.Amount));
                        foreach (var tax in charge.Taxes) ExtendedTaxElement("AppliedTradeTax", tax);
                        End();
                    }
            }

            string? terms = PaymentTermsText();
            string? mandate = pay?.DirectDebit?.MandateReference;
            if (_ext is { PaymentTerms.Count: > 0 })
            {
                foreach (var term in _ext.PaymentTerms) PaymentTermElement(term);
            }
            else if (!_minimum && (terms is not null || _inv.DueDate.HasValue || Has(mandate)))
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
                if (_inv.AllowancesAndCharges.Any(a => a.IsCharge) || _ext is { LogisticsCharges.Count: > 0 }) Text("ChargeTotalAmount", Amount(_totals.ChargeTotal));
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

            if (_ext is not null)
                foreach (var adjustment in _ext.FinancialAdjustments)
                {
                    Start("SpecifiedFinancialAdjustment");
                    Text("Reason", adjustment.Reason);
                    Text("ActualAmount", Amount(adjustment.Amount));
                    End();
                }

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
                    Text("TypeCode", _ext?.BuyerAccountingTypeCode);
                    End();
                }
            }
            if (_ext is not null)
                foreach (var payment in _ext.AdvancePayments)
                {
                    Start("SpecifiedAdvancePayment");
                    Text("PaidAmount", Amount(payment.PaidAmount));
                    if (payment.ReceivedDate is { } received) FormattedDate(received, "FormattedReceivedDateTime");
                    foreach (var tax in payment.Taxes) ExtendedTaxElement("IncludedTradeTax", tax);
                    if (Has(payment.InvoiceNumber))
                    {
                        Start("InvoiceSpecifiedReferencedDocument");
                        Text("IssuerAssignedID", payment.InvoiceNumber);
                        Text("TypeCode", payment.InvoiceTypeCode);
                        if (payment.InvoiceDate is { } invoiceDate) FormattedDate(invoiceDate);
                        End();
                    }
                    End();
                }
            End();
        }

        private void ExtendedTaxElement(string element, ExtendedTax tax)
        {
            Start(element);
            if (tax.CalculatedAmount is { } calculated) Text("CalculatedAmount", Amount(calculated));
            Text("TypeCode", "VAT");
            Text("ExemptionReason", tax.ExemptionReason);
            Text("CategoryCode", tax.Category.ToCode());
            Text("ExemptionReasonCode", tax.ExemptionReasonCode);
            if (InvoiceCalculator.EffectiveRate(tax.Category, tax.Rate) is { } rate) Text("RateApplicablePercent", Percent(rate));
            End();
        }

        private void PaymentTermElement(ExtendedPaymentTerm term)
        {
            Start("SpecifiedTradePaymentTerms");
            if (Has(term.Description))
            {
                _x.WriteStartElement("ram", "Description", Ram);
                _x.WriteString(term.Description!);
                _x.WriteEndElement();
            }
            if (term.DueDate is { } due) DateTimeElement("DueDateDateTime", due);
            Text("DirectDebitMandateID", term.MandateReference);
            if (term.PartialPaymentAmount is { } partial) Text("PartialPaymentAmount", Amount(partial));
            PaymentAdjustmentElement("ApplicableTradePaymentPenaltyTerms", "ActualPenaltyAmount", term.Penalty);
            PaymentAdjustmentElement("ApplicableTradePaymentDiscountTerms", "ActualDiscountAmount", term.Discount);
            ExtendedPartyElement("PayeeTradeParty", term.Payee);
            End();
        }

        private void PaymentAdjustmentElement(string element, string amountElement, PaymentAdjustment? adjustment)
        {
            if (adjustment is null) return;
            Start(element);
            if (adjustment.BasisDate is { } basisDate) DateTimeElement("BasisDateTime", basisDate);
            if (adjustment.Period is { } period) Text("BasisPeriodMeasure", Quantity(period), "unitCode", adjustment.PeriodUnitCode);
            if (adjustment.BaseAmount is { } baseAmount) Text("BasisAmount", Amount(baseAmount));
            if (adjustment.Percentage is { } percentage) Text("CalculationPercent", Percent(percentage));
            if (adjustment.Amount is { } amount) Text(amountElement, Amount(amount));
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
                    if (_extended) Text("AccountName", pay.DirectDebit.DebitedAccountName);
                    End();
                }
            }

            void DebtorInstitution()
            {
                if (_extended && Has(pay.DirectDebit?.DebitedServiceProviderIdentifier))
                {
                    Start("PayerSpecifiedDebtorFinancialInstitution");
                    Text("BICID", pay.DirectDebit!.DebitedServiceProviderIdentifier);
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
                DebtorInstitution();
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
                DebtorInstitution();
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
            if (_extended && ac.SequenceNumber is { } sequence) Text("SequenceNumeric", Quantity(sequence));
            // BASIC has the base and percentage at document level only (withTax).
            if ((_full || withTax) && ac.Percentage is { } pct) Text("CalculationPercent", Percent(pct));
            if ((_full || withTax) && ac.BaseAmount is { } basis) Text("BasisAmount", Amount(basis));
            if (_extended && ac.BaseQuantity is { } baseQuantity) Text("BasisQuantity", Quantity(baseQuantity), "unitCode", ac.BaseQuantityUnitCode);
            Text("ActualAmount", Amount(InvoiceCalculator.AmountOf(ac)));
            Text("ReasonCode", ac.ReasonCode, "listID", _extended ? ac.ReasonCodeListId : null);
            Text("Reason", ac.Reason);
            if (withTax)
            {
                Start("CategoryTradeTax");
                Text("TypeCode", "VAT");
                if (_extended) Text("ExemptionReason", ac.ExemptionReason);
                Text("CategoryCode", ac.VatCategory.ToCode());
                if (_extended) Text("ExemptionReasonCode", ac.ExemptionReasonCode);
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

        private void Period(string element, Period? period, string? description = null)
        {
            if (!Has(description) && (period is null || (period.Start is null && period.End is null))) return;
            Start(element);
            Text("Description", description);
            if (period?.Start is { } s) DateTimeElement("StartDateTime", s);
            if (period?.End is { } e) DateTimeElement("EndDateTime", e);
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

        private void FormattedDate(DateTime date, string element = "FormattedIssueDateTime")
        {
            Start(element);
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
