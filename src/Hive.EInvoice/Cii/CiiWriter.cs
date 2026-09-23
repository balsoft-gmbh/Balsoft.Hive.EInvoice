using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Hive.EInvoice.Validation;

namespace Hive.EInvoice.Cii;

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
            if (!_minimum && !string.IsNullOrWhiteSpace(_inv.BusinessProcess))
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
            Text("TypeCode", _inv.TypeCode.ToCode());
            DateTimeElement("IssueDateTime", _inv.IssueDate);
            if (!_minimum)
            {
                foreach (var note in _inv.Notes.Where(n => !string.IsNullOrWhiteSpace(n.Text)))
                {
                    Start("IncludedNote");
                    Text("Content", note.Text);
                    Text("SubjectCode", note.SubjectCode);
                    End();
                }
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

            if (!_minimum && _inv.SellerTaxRepresentative is { } rep)
            {
                Start("SellerTaxRepresentativeTradeParty");
                Text("Name", rep.Name);
                Address(rep.Address);
                TaxRegistration("VA", rep.VatIdentifier);
                End();
            }

            if (_full) ReferencedId("SellerOrderReferencedDocument", _inv.SalesOrderReference);
            ReferencedId("BuyerOrderReferencedDocument", _inv.PurchaseOrderReference);
            if (!_minimum) ReferencedId("ContractReferencedDocument", _inv.ContractReference);

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
                    End();
                }
                if (!string.IsNullOrWhiteSpace(_inv.TenderOrLotReference))
                {
                    Start("AdditionalReferencedDocument");
                    Text("IssuerAssignedID", _inv.TenderOrLotReference);
                    Text("TypeCode", "50");
                    End();
                }
                if (_inv.InvoicedObjectIdentifier is { } obj && !string.IsNullOrWhiteSpace(obj.Value))
                {
                    Start("AdditionalReferencedDocument");
                    Text("IssuerAssignedID", obj.Value);
                    Text("TypeCode", "130");
                    Text("ReferenceTypeCode", obj.Scheme);
                    End();
                }
                if (!string.IsNullOrWhiteSpace(_inv.ProjectReference))
                {
                    Start("SpecifiedProcuringProject");
                    Text("ID", _inv.ProjectReference);
                    Text("Name", "Project reference");
                    End();
                }
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
            if (seller && _full) Text("Description", party.AdditionalLegalInformation);

            bool tradingName = !_minimum && !string.IsNullOrWhiteSpace(party.TradingName);
            if (party.LegalRegistrationIdentifier is { } legal && !string.IsNullOrWhiteSpace(legal.Value) || tradingName)
            {
                Start("SpecifiedLegalOrganization");
                if (party.LegalRegistrationIdentifier is { } lr && !string.IsNullOrWhiteSpace(lr.Value))
                    Text("ID", lr.Value, "schemeID", lr.Scheme);
                if (tradingName) Text("TradingBusinessName", party.TradingName);
                End();
            }

            if (_full && party.Contact is { } c && (Has(c.Name) || Has(c.Phone) || Has(c.Email)))
            {
                Start("DefinedTradeContact");
                Text("PersonName", c.Name);
                if (Has(c.Phone))
                {
                    Start("TelephoneUniversalCommunication");
                    Text("CompleteNumber", c.Phone);
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

            // MINIMUM carries the buyer without an address and the seller with its country only.
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

            TaxRegistration("VA", party.VatIdentifier);
            if (seller) TaxRegistration("FC", party.TaxRegistrationIdentifier);
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
            if (!_minimum && _inv.Delivery is { } d)
            {
                bool party = Has(d.PartyName) || d.LocationIdentifier is not null || d.Address is not null;
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
                if (d.ActualDeliveryDate is { } date)
                {
                    Start("ActualDeliverySupplyChainEvent");
                    DateTimeElement("OccurrenceDateTime", date);
                    End();
                }
            }
            if (!_minimum)
            {
                ReferencedId("DespatchAdviceReferencedDocument", _inv.DespatchAdviceReference);
                if (_full) ReferencedId("ReceivingAdviceReferencedDocument", _inv.ReceivingAdviceReference);
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

            if (!_minimum && _inv.Payee is { } payee)
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
                    if (_inv.TaxPointDate is { } tp)
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
            if (!_minimum && _inv.RoundingAmount is { } rounding) Text("RoundingAmount", Amount(rounding));
            Text("GrandTotalAmount", Amount(_totals.TaxInclusiveAmount));
            if (!_minimum && _inv.PrepaidAmount is { } prepaid) Text("TotalPrepaidAmount", Amount(prepaid));
            Text("DuePayableAmount", Amount(_totals.AmountDue));
            End();

            if (!_minimum)
            {
                foreach (var p in _inv.PrecedingInvoices.Where(p => Has(p.Number)))
                {
                    Start("InvoiceReferencedDocument");
                    Text("IssuerAssignedID", p.Number);
                    if (p.IssueDate is { } d)
                    {
                        Start("FormattedIssueDateTime");
                        _x.WriteStartElement("qdt", "DateTimeString", Qdt);
                        _x.WriteAttributeString("format", "102");
                        _x.WriteString(d.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
                        _x.WriteEndElement();
                        End();
                    }
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
                Text("Information", pay.MeansText);
            }

            void Card()
            {
                if (pay.Card is { } card && Has(card.PrimaryAccountNumber))
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
            if (ac.Percentage is { } pct) Text("CalculationPercent", Percent(pct));
            if (ac.BaseAmount is { } basis) Text("BasisAmount", Amount(basis));
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

        private void ReferencedId(string element, string? id)
        {
            if (!Has(id)) return;
            Start(element);
            Text("IssuerAssignedID", id);
            End();
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
