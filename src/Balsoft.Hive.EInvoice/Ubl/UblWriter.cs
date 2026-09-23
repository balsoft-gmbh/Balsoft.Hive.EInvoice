using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Balsoft.Hive.EInvoice.Cii;
using Balsoft.Hive.EInvoice.Validation;

namespace Balsoft.Hive.EInvoice.Ubl;

/// <summary>
/// Writes an <see cref="Invoice"/> as OASIS UBL 2.1, the syntax of Peppol BIS Billing 3.0 and
/// the second syntax of XRechnung. Credit note type codes (381, 261, ...) produce a CreditNote
/// document, everything else an Invoice document; the two differ in element order and names,
/// which the writer follows.
/// </summary>
public static class UblWriter
{
    internal const string InvoiceNs = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2";
    internal const string CreditNoteNs = "urn:oasis:names:specification:ubl:schema:xsd:CreditNote-2";
    internal const string Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    internal const string Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";

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
        if (!profile.IsEN16931Compliant())
            throw new NotSupportedException($"{profile} is a Factur-X profile without lines; UBL documents are EN 16931 compliant.");
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

    /// <summary>UBL uses UNTDID 2005 for BT-8; the model keeps the CII code (UNTDID 2475).</summary>
    internal static string? VatPointCodeToUbl(string? ciiCode) => ciiCode switch
    {
        "5" => "3",
        "29" => "35",
        "72" => "432",
        _ => ciiCode,
    };

    private sealed class Emitter
    {
        private readonly XmlWriter _x;
        private readonly Invoice _inv;
        private readonly InvoiceProfile _profile;
        private readonly InvoiceTotals _totals;
        private readonly bool _credit;
        private readonly string _cur;

        public Emitter(XmlWriter x, Invoice invoice, InvoiceProfile profile)
        {
            _x = x;
            _inv = invoice;
            _profile = profile;
            _totals = InvoiceCalculator.Calculate(invoice);
            _credit = invoice.TypeCode.IsCreditNote();
            _cur = invoice.Currency;
        }

        public void Document()
        {
            _x.WriteStartDocument();
            _x.WriteStartElement(_credit ? "CreditNote" : "Invoice", _credit ? CreditNoteNs : InvoiceNs);
            _x.WriteAttributeString("xmlns", "cac", null, Cac);
            _x.WriteAttributeString("xmlns", "cbc", null, Cbc);

            B("CustomizationID", _profile.SpecificationIdentifier());
            B("ProfileID", _inv.BusinessProcess);
            B("ID", _inv.Number);
            B("IssueDate", Date(_inv.IssueDate));
            if (_credit)
            {
                if (_inv.TaxPointDate is { } tp) B("TaxPointDate", Date(tp));
                B("CreditNoteTypeCode", _inv.TypeCode.ToCode());
            }
            else
            {
                if (_inv.DueDate is { } due) B("DueDate", Date(due));
                B("InvoiceTypeCode", _inv.TypeCode.ToCode());
            }
            foreach (var n in _inv.Notes.Where(n => Has(n.Text)))
                B("Note", Has(n.SubjectCode) ? $"#{n.SubjectCode}#{n.Text}" : n.Text);
            if (!_credit && _inv.TaxPointDate is { } tp2) B("TaxPointDate", Date(tp2));
            B("DocumentCurrencyCode", _cur);
            B("TaxCurrencyCode", _inv.TaxCurrency);
            B("AccountingCost", _inv.BuyerAccountingReference);
            B("BuyerReference", _inv.BuyerReference);
            InvoicePeriod();

            if (Has(_inv.PurchaseOrderReference) || Has(_inv.SalesOrderReference))
            {
                A("OrderReference");
                // cbc:ID is mandatory in OrderReference; "NA" is the agreed filler when only BT-14 is known.
                B("ID", Has(_inv.PurchaseOrderReference) ? _inv.PurchaseOrderReference : "NA");
                B("SalesOrderID", _inv.SalesOrderReference);
                End();
            }
            foreach (var p in _inv.PrecedingInvoices.Where(p => Has(p.Number)))
            {
                A("BillingReference");
                A("InvoiceDocumentReference");
                B("ID", p.Number);
                if (p.IssueDate is { } d) B("IssueDate", Date(d));
                End();
                End();
            }
            IdReference("DespatchDocumentReference", _inv.DespatchAdviceReference);
            IdReference("ReceiptDocumentReference", _inv.ReceivingAdviceReference);
            if (_credit)
            {
                IdReference("ContractDocumentReference", _inv.ContractReference);
                AdditionalDocuments();
                IdReference("OriginatorDocumentReference", _inv.TenderOrLotReference);
            }
            else
            {
                IdReference("OriginatorDocumentReference", _inv.TenderOrLotReference);
                IdReference("ContractDocumentReference", _inv.ContractReference);
                AdditionalDocuments();
                IdReference("ProjectReference", _inv.ProjectReference);
            }

            SupplierParty();
            CustomerParty();
            Payee();
            TaxRepresentative();
            Delivery();
            PaymentMeans();
            PaymentTerms();
            foreach (var ac in _inv.AllowancesAndCharges) AllowanceCharge(ac, withTax: true);
            TaxTotals();
            MonetaryTotal();
            for (int i = 0; i < _inv.Lines.Count; i++) Line(_inv.Lines[i], _totals.LineNetAmounts[i], i);

            _x.WriteEndElement();
            _x.WriteEndDocument();
        }

        private void InvoicePeriod()
        {
            string? code = VatPointCodeToUbl(_inv.TaxPointDateCode);
            var period = _inv.InvoicingPeriod;
            if (period is null && !Has(code)) return;
            A("InvoicePeriod");
            if (period?.Start is { } s) B("StartDate", Date(s));
            if (period?.End is { } e) B("EndDate", Date(e));
            B("DescriptionCode", code);
            End();
        }

        private void AdditionalDocuments()
        {
            if (_inv.InvoicedObjectIdentifier is { } obj && Has(obj.Value))
            {
                A("AdditionalDocumentReference");
                B("ID", obj.Value, "schemeID", obj.Scheme);
                B("DocumentTypeCode", "130");
                End();
            }
            foreach (var d in _inv.SupportingDocuments.Where(d => Has(d.Reference)))
            {
                A("AdditionalDocumentReference");
                B("ID", d.Reference);
                B("DocumentDescription", d.Description);
                if (d.Content is { Length: > 0 } || Has(d.ExternalLocation))
                {
                    A("Attachment");
                    if (d.Content is { Length: > 0 } content)
                    {
                        _x.WriteStartElement("cbc", "EmbeddedDocumentBinaryObject", Cbc);
                        _x.WriteAttributeString("mimeCode", d.MimeCode ?? "application/pdf");
                        _x.WriteAttributeString("filename", d.FileName ?? d.Reference);
                        _x.WriteString(Convert.ToBase64String(content));
                        _x.WriteEndElement();
                    }
                    if (Has(d.ExternalLocation))
                    {
                        A("ExternalReference");
                        B("URI", d.ExternalLocation);
                        End();
                    }
                    End();
                }
                End();
            }
            if (_credit && Has(_inv.ProjectReference))
            {
                // CreditNote has no ProjectReference; EN 16931 binds BT-11 to a document reference of type 50.
                A("AdditionalDocumentReference");
                B("ID", _inv.ProjectReference);
                B("DocumentTypeCode", "50");
                End();
            }
        }

        // ── parties ───────────────────────────────────────────────────────────

        private void SupplierParty()
        {
            A("AccountingSupplierParty");
            Party(_inv.Seller, seller: true);
            End();
        }

        private void CustomerParty()
        {
            A("AccountingCustomerParty");
            Party(_inv.Buyer, seller: false);
            End();
        }

        private void Party(Party p, bool seller)
        {
            A("Party");
            if (p.ElectronicAddress is { } ea && Has(ea.Value)) B("EndpointID", ea.Value, "schemeID", ea.Scheme);
            var ids = seller ? p.Identifiers : p.Identifiers.Take(1).ToList();
            foreach (var id in ids.Where(i => Has(i.Value)))
            {
                A("PartyIdentification");
                B("ID", id.Value, "schemeID", id.Scheme);
                End();
            }
            // BT-90 creditor identifier belongs to the seller unless a payee exists.
            if (seller && _inv.Payee is null && _inv.PaymentInstructions?.DirectDebit?.CreditorIdentifier is { Length: > 0 } creditor)
            {
                A("PartyIdentification");
                B("ID", creditor, "schemeID", "SEPA");
                End();
            }
            if (Has(p.TradingName))
            {
                A("PartyName");
                B("Name", p.TradingName);
                End();
            }
            Address("PostalAddress", p.Address);
            if (Has(p.VatIdentifier))
            {
                A("PartyTaxScheme");
                B("CompanyID", p.VatIdentifier);
                A("TaxScheme");
                B("ID", "VAT");
                End();
                End();
            }
            if (seller && Has(p.TaxRegistrationIdentifier))
            {
                A("PartyTaxScheme");
                B("CompanyID", p.TaxRegistrationIdentifier);
                A("TaxScheme");
                B("ID", "FC");
                End();
                End();
            }
            A("PartyLegalEntity");
            B("RegistrationName", p.Name);
            if (p.LegalRegistrationIdentifier is { } lr && Has(lr.Value)) B("CompanyID", lr.Value, "schemeID", lr.Scheme);
            if (seller) B("CompanyLegalForm", p.AdditionalLegalInformation);
            End();
            if (p.Contact is { } c && (Has(c.Name) || Has(c.Phone) || Has(c.Email)))
            {
                A("Contact");
                B("Name", c.Name);
                B("Telephone", c.Phone);
                B("ElectronicMail", c.Email);
                End();
            }
            End();
        }

        private void Address(string element, PostalAddress a)
        {
            A(element);
            B("StreetName", a.Line1);
            B("AdditionalStreetName", a.Line2);
            B("CityName", a.City);
            B("PostalZone", a.PostCode);
            B("CountrySubentity", a.CountrySubdivision);
            if (Has(a.Line3))
            {
                A("AddressLine");
                B("Line", a.Line3);
                End();
            }
            A("Country");
            B("IdentificationCode", a.CountryCode);
            End();
            End();
        }

        private void Payee()
        {
            if (_inv.Payee is not { } payee) return;
            A("PayeeParty");
            if (payee.Identifier is { } id && Has(id.Value))
            {
                A("PartyIdentification");
                B("ID", id.Value, "schemeID", id.Scheme);
                End();
            }
            if (_inv.PaymentInstructions?.DirectDebit?.CreditorIdentifier is { Length: > 0 } creditor)
            {
                A("PartyIdentification");
                B("ID", creditor, "schemeID", "SEPA");
                End();
            }
            A("PartyName");
            B("Name", payee.Name);
            End();
            if (payee.LegalRegistrationIdentifier is { } lr && Has(lr.Value))
            {
                A("PartyLegalEntity");
                B("CompanyID", lr.Value, "schemeID", lr.Scheme);
                End();
            }
            End();
        }

        private void TaxRepresentative()
        {
            if (_inv.SellerTaxRepresentative is not { } rep) return;
            A("TaxRepresentativeParty");
            A("PartyName");
            B("Name", rep.Name);
            End();
            Address("PostalAddress", rep.Address);
            A("PartyTaxScheme");
            B("CompanyID", rep.VatIdentifier);
            A("TaxScheme");
            B("ID", "VAT");
            End();
            End();
            End();
        }

        private void Delivery()
        {
            if (_inv.Delivery is not { } d) return;
            A("Delivery");
            if (d.ActualDeliveryDate is { } date) B("ActualDeliveryDate", Date(date));
            if (d.LocationIdentifier is { } loc && Has(loc.Value) || d.Address is not null)
            {
                A("DeliveryLocation");
                if (d.LocationIdentifier is { } l && Has(l.Value)) B("ID", l.Value, "schemeID", l.Scheme);
                if (d.Address is not null) Address("Address", d.Address);
                End();
            }
            if (Has(d.PartyName))
            {
                A("DeliveryParty");
                A("PartyName");
                B("Name", d.PartyName);
                End();
                End();
            }
            End();
        }

        // ── payment ───────────────────────────────────────────────────────────

        private void PaymentMeans()
        {
            var pay = _inv.PaymentInstructions;
            if (pay is null)
            {
                // A credit note states its due date inside a payment means (BT-9); that needs a code.
                if (_credit && _inv.DueDate is { } dueOnly)
                {
                    A("PaymentMeans");
                    B("PaymentMeansCode", PaymentMeansCode.NotDefined);
                    B("PaymentDueDate", Date(dueOnly));
                    End();
                }
                return;
            }

            var accounts = pay.CreditTransfers.Where(a => Has(a.AccountIdentifier)).ToList();
            int count = Math.Max(1, accounts.Count);
            for (int i = 0; i < count; i++)
            {
                A("PaymentMeans");
                B("PaymentMeansCode", pay.MeansCode, "name", pay.MeansText);
                if (_credit && _inv.DueDate is { } due) B("PaymentDueDate", Date(due));
                B("PaymentID", pay.RemittanceInformation);
                if (pay.Card is { } card && Has(card.PrimaryAccountNumber))
                {
                    A("CardAccount");
                    B("PrimaryAccountNumberID", card.PrimaryAccountNumber);
                    B("NetworkID", "NA");
                    B("HolderName", card.HolderName);
                    End();
                }
                if (i < accounts.Count)
                {
                    var account = accounts[i];
                    A("PayeeFinancialAccount");
                    string compact = Iban.Compact(account.AccountIdentifier);
                    B("ID", Iban.IsValid(compact) ? compact : account.AccountIdentifier.Trim());
                    B("Name", account.AccountName);
                    if (Has(account.ServiceProviderIdentifier))
                    {
                        A("FinancialInstitutionBranch");
                        B("ID", account.ServiceProviderIdentifier!.Trim());
                        End();
                    }
                    End();
                }
                if (pay.DirectDebit is { } dd && (Has(dd.MandateReference) || Has(dd.DebitedAccountIdentifier)))
                {
                    A("PaymentMandate");
                    B("ID", dd.MandateReference);
                    if (Has(dd.DebitedAccountIdentifier))
                    {
                        A("PayerFinancialAccount");
                        B("ID", Iban.Compact(dd.DebitedAccountIdentifier!));
                        End();
                    }
                    End();
                }
                End();
            }
        }

        private void PaymentTerms()
        {
            string? terms = PaymentTermsFormatter.Format(_inv);
            if (terms is null) return;
            A("PaymentTerms");
            _x.WriteStartElement("cbc", "Note", Cbc);
            _x.WriteString(terms);
            _x.WriteEndElement();
            End();
        }

        private void AllowanceCharge(AllowanceCharge ac, bool withTax)
        {
            A("AllowanceCharge");
            B("ChargeIndicator", ac.IsCharge ? "true" : "false");
            B("AllowanceChargeReasonCode", ac.ReasonCode);
            B("AllowanceChargeReason", ac.Reason);
            if (ac.Percentage is { } pct) B("MultiplierFactorNumeric", CiiWriter.Percent(pct));
            Amount("Amount", InvoiceCalculator.AmountOf(ac));
            if (ac.BaseAmount is { } basis) Amount("BaseAmount", basis);
            if (withTax)
            {
                A("TaxCategory");
                B("ID", ac.VatCategory.ToCode());
                if (InvoiceCalculator.EffectiveRate(ac.VatCategory, ac.VatRate) is { } rate) B("Percent", CiiWriter.Percent(rate));
                TaxScheme();
                End();
            }
            End();
        }

        private void TaxTotals()
        {
            A("TaxTotal");
            Amount("TaxAmount", _totals.VatTotal);
            foreach (var b in _totals.VatBreakdown)
            {
                A("TaxSubtotal");
                Amount("TaxableAmount", b.TaxableAmount);
                Amount("TaxAmount", b.TaxAmount);
                A("TaxCategory");
                B("ID", b.Category.ToCode());
                if (b.Rate is { } rate) B("Percent", CiiWriter.Percent(rate));
                B("TaxExemptionReasonCode", b.ExemptionReasonCode);
                B("TaxExemptionReason", b.ExemptionReason);
                TaxScheme();
                End();
                End();
            }
            End();
            if (Has(_inv.TaxCurrency) && _inv.VatTotalInTaxCurrency is { } vatInTaxCurrency)
            {
                A("TaxTotal");
                _x.WriteStartElement("cbc", "TaxAmount", Cbc);
                _x.WriteAttributeString("currencyID", _inv.TaxCurrency);
                _x.WriteString(CiiWriter.Amount(vatInTaxCurrency));
                _x.WriteEndElement();
                End();
            }
        }

        private void MonetaryTotal()
        {
            A("LegalMonetaryTotal");
            Amount("LineExtensionAmount", _totals.LineNetTotal);
            Amount("TaxExclusiveAmount", _totals.TaxExclusiveAmount);
            Amount("TaxInclusiveAmount", _totals.TaxInclusiveAmount);
            if (_inv.AllowancesAndCharges.Any(a => !a.IsCharge)) Amount("AllowanceTotalAmount", _totals.AllowanceTotal);
            if (_inv.AllowancesAndCharges.Any(a => a.IsCharge)) Amount("ChargeTotalAmount", _totals.ChargeTotal);
            if (_inv.PrepaidAmount is { } prepaid) Amount("PrepaidAmount", prepaid);
            if (_inv.RoundingAmount is { } rounding) Amount("PayableRoundingAmount", rounding);
            Amount("PayableAmount", _totals.AmountDue);
            End();
        }

        // ── lines ─────────────────────────────────────────────────────────────

        private void Line(InvoiceLine line, decimal netAmount, int index)
        {
            A(_credit ? "CreditNoteLine" : "InvoiceLine");
            B("ID", Has(line.Id) ? line.Id : (index + 1).ToString(CultureInfo.InvariantCulture));
            B("Note", line.Note);
            B(_credit ? "CreditedQuantity" : "InvoicedQuantity", CiiWriter.Quantity(line.Quantity), "unitCode", line.UnitCode);
            Amount("LineExtensionAmount", netAmount);
            B("AccountingCost", line.BuyerAccountingReference);
            if (line.Period is { } p && (p.Start is not null || p.End is not null))
            {
                A("InvoicePeriod");
                if (p.Start is { } s) B("StartDate", Date(s));
                if (p.End is { } e) B("EndDate", Date(e));
                End();
            }
            if (Has(line.OrderLineReference))
            {
                A("OrderLineReference");
                B("LineID", line.OrderLineReference);
                End();
            }
            if (line.ObjectIdentifier is { } obj && Has(obj.Value))
            {
                A("DocumentReference");
                B("ID", obj.Value, "schemeID", obj.Scheme);
                B("DocumentTypeCode", "130");
                End();
            }
            foreach (var ac in line.AllowancesAndCharges) AllowanceCharge(ac, withTax: false);

            var item = line.Item;
            A("Item");
            B("Description", item.Description);
            B("Name", item.Name);
            IdElement("BuyersItemIdentification", item.BuyersIdentifier, null);
            IdElement("SellersItemIdentification", item.SellersIdentifier, null);
            if (item.StandardIdentifier is { } sid && Has(sid.Value)) IdElement("StandardItemIdentification", sid.Value, sid.Scheme);
            if (Has(item.OriginCountryCode))
            {
                A("OriginCountry");
                B("IdentificationCode", item.OriginCountryCode);
                End();
            }
            foreach (var c in item.Classifications)
            {
                A("CommodityClassification");
                _x.WriteStartElement("cbc", "ItemClassificationCode", Cbc);
                _x.WriteAttributeString("listID", c.Scheme);
                if (Has(c.SchemeVersion)) _x.WriteAttributeString("listVersionID", c.SchemeVersion);
                _x.WriteString(c.Code);
                _x.WriteEndElement();
                End();
            }
            A("ClassifiedTaxCategory");
            B("ID", line.VatCategory.ToCode());
            if (InvoiceCalculator.EffectiveRate(line.VatCategory, line.VatRate) is { } rate) B("Percent", CiiWriter.Percent(rate));
            TaxScheme();
            End();
            foreach (var a in item.Attributes)
            {
                A("AdditionalItemProperty");
                B("Name", a.Name);
                B("Value", a.Value);
                End();
            }
            End();

            A("Price");
            B("PriceAmount", CiiWriter.Price(line.NetPrice), "currencyID", _cur);
            if (line.PriceBaseQuantity is { } q) B("BaseQuantity", CiiWriter.Quantity(q), "unitCode", line.PriceBaseQuantityUnitCode ?? line.UnitCode);
            if (line.GrossPrice is { } gross)
            {
                A("AllowanceCharge");
                B("ChargeIndicator", "false");
                B("Amount", CiiWriter.Price(line.PriceDiscount ?? gross - line.NetPrice), "currencyID", _cur);
                B("BaseAmount", CiiWriter.Price(gross), "currencyID", _cur);
                End();
            }
            End();

            End();
        }

        private void IdElement(string element, string? value, string? scheme)
        {
            if (!Has(value)) return;
            A(element);
            B("ID", value, "schemeID", scheme);
            End();
        }

        private void IdReference(string element, string? id)
        {
            if (!Has(id)) return;
            A(element);
            B("ID", id);
            End();
        }

        private void TaxScheme()
        {
            A("TaxScheme");
            B("ID", "VAT");
            End();
        }

        // ── primitives ────────────────────────────────────────────────────────

        private void A(string name) => _x.WriteStartElement("cac", name, Cac);

        private void End() => _x.WriteEndElement();

        private void B(string name, string? value, string? attribute = null, string? attributeValue = null)
        {
            if (!Has(value)) return;
            _x.WriteStartElement("cbc", name, Cbc);
            if (attribute is not null && Has(attributeValue)) _x.WriteAttributeString(attribute, attributeValue!.Trim());
            _x.WriteString(value!.Trim());
            _x.WriteEndElement();
        }

        private void Amount(string name, decimal value) => B(name, CiiWriter.Amount(value), "currencyID", _cur);

        private static string Date(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        private static bool Has(string? s) => !string.IsNullOrWhiteSpace(s);
    }
}
