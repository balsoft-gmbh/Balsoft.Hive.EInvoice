using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Hive.EInvoice.CodeLists;

namespace Hive.EInvoice.Validation;

/// <summary>
/// Pre-flight check of an <see cref="Invoice"/> against the business rules of EN 16931 and,
/// for XRechnung, the German CIUS (BR-DE). It catches what a receiver's validator would
/// reject before the document leaves your system, with the official rule identifier in each
/// message. It does not replace the official validation artefacts (KoSIT validator, Schematron),
/// which remain authoritative; the test suite of this library runs every sample through them.
/// </summary>
public static class InvoiceValidator
{
    /// <summary>Document types XRechnung permits (BR-DE-17).</summary>
    private static readonly int[] XRechnungTypeCodes = { 326, 380, 384, 389, 381, 875, 876, 877 };

    private static readonly Regex EmailPattern = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant);

    /// <summary>Checks <paramref name="invoice"/> against the rules of <paramref name="profile"/>.</summary>
    public static ValidationResult Validate(Invoice invoice, InvoiceProfile profile)
    {
        var c = new Checker(invoice, profile);
        c.Run();
        return new ValidationResult(profile, c.Issues.OrderBy(i => i.Severity).ToList());
    }

    private sealed class Checker
    {
        private readonly Invoice _inv;
        private readonly InvoiceProfile _profile;
        public readonly List<ValidationIssue> Issues = new();

        public Checker(Invoice invoice, InvoiceProfile profile)
        {
            _inv = invoice;
            _profile = profile;
        }

        private void Error(string rule, string message, string? bt = null) => Issues.Add(new ValidationIssue(rule, Severity.Error, message, bt));

        private void Warn(string rule, string message, string? bt = null) => Issues.Add(new ValidationIssue(rule, Severity.Warning, message, bt));

        private static bool Has(string? s) => !string.IsNullOrWhiteSpace(s);

        public void Run()
        {
            Header();
            if (_profile.IsEN16931Compliant())
            {
                Parties();
                Lines();
                AllowancesAndCharges();
                Payment();
                Vat();
                Miscellaneous();
            }
            if (_profile == InvoiceProfile.XRechnung) XRechnung();
            if (_profile == InvoiceProfile.PeppolBis3) Peppol();
        }

        // ── header (all profiles) ─────────────────────────────────────────────

        private void Header()
        {
            if (!Has(_inv.Number)) Error("BR-02", "An invoice shall have an invoice number.", "BT-1");
            if (_inv.IssueDate == default) Error("BR-03", "An invoice shall have an issue date.", "BT-2");
            if (!CodeList.IsInvoiceTypeCode(_inv.TypeCode.ToCode()))
                Error("BR-CL-01", $"Invoice type code {_inv.TypeCode.ToCode()} is not in the UNTDID 1001 subset of EN 16931.", "BT-3");
            if (!CodeList.IsCurrencyCode(_inv.Currency))
                Error("BR-CL-04", $"Invoice currency '{_inv.Currency}' is not an ISO 4217 code.", "BT-5");
            if (Has(_inv.TaxCurrency))
            {
                if (!CodeList.IsCurrencyCode(_inv.TaxCurrency))
                    Error("BR-CL-05", $"VAT accounting currency '{_inv.TaxCurrency}' is not an ISO 4217 code.", "BT-6");
                if (_inv.VatTotalInTaxCurrency is null)
                    Error("BR-53", "When a VAT accounting currency is given, the total VAT in that currency shall be given.", "BT-111");
            }
            if (!Has(_inv.Seller.Name)) Error("BR-06", "An invoice shall contain the seller name.", "BT-27");
            if (!Has(_inv.Buyer.Name)) Error("BR-07", "An invoice shall contain the buyer name.", "BT-44");
            Country("BR-09", _inv.Seller.Address.CountryCode, "BT-40", "seller");
            if (_inv.Buyer.VatIdentifier is { } buyerVat && Has(buyerVat)) VatPrefix(buyerVat, "BT-48");
            if (_inv.Seller.VatIdentifier is { } sellerVat && Has(sellerVat)) VatPrefix(sellerVat, "BT-31");
        }

        private void Country(string rule, string? code, string bt, string who)
        {
            if (!Has(code)) Error(rule, $"The {who} postal address shall contain a country code.", bt);
            else if (!CodeList.IsCountryCode(code)) Error("BR-CL-14", $"'{code}' is not an ISO 3166-1 alpha-2 country code.", bt);
        }

        private void VatPrefix(string vat, string bt)
        {
            string prefix = vat.Trim().Length >= 2 ? vat.Trim().Substring(0, 2) : vat;
            if (prefix != "EL" && !CodeList.IsCountryCode(prefix))
                Error("BR-CO-09", $"VAT identifier '{vat}' shall start with an ISO 3166-1 alpha-2 country prefix (EL for Greece).", bt);
        }

        // ── parties ───────────────────────────────────────────────────────────

        private void Parties()
        {
            var s = _inv.Seller;
            if (!Has(s.VatIdentifier) && s.LegalRegistrationIdentifier is null && !s.Identifiers.Any(i => Has(i.Value)))
                Error("BR-CO-26", "The seller shall be identified by an identifier, a legal registration identifier or a VAT identifier.", "BT-29, BT-30, BT-31");
            Country("BR-11", _inv.Buyer.Address.CountryCode, "BT-55", "buyer");
            if (_inv.Buyer.Identifiers.Count > 1)
                Warn("HIVE-02", "The buyer has one identifier (BT-46); only the first one is written.", "BT-46");

            Schemes(s, "BT-29", "BT-30", "BT-34", "BR-62");
            Schemes(_inv.Buyer, "BT-46", "BT-47", "BT-49", "BR-63");

            if (_inv.Payee is { } payee && !Has(payee.Name))
                Error("BR-17", "A payee shall have a name.", "BT-59");
            if (_inv.SellerTaxRepresentative is { } rep)
            {
                if (!Has(rep.Name)) Error("BR-18", "The seller tax representative shall have a name.", "BT-62");
                Country("BR-20", rep.Address.CountryCode, "BT-69", "seller tax representative");
                if (!Has(rep.VatIdentifier)) Error("BR-56", "The seller tax representative shall have a VAT identifier.", "BT-63");
            }
            if (_inv.Delivery?.Address is { } da) Country("BR-57", da.CountryCode, "BT-80", "deliver to");
        }

        private void Schemes(Party p, string idTerm, string legalTerm, string addressTerm, string addressRule)
        {
            foreach (var id in p.Identifiers.Where(i => Has(i.Scheme) && !CodeList.IsIdentifierScheme(i.Scheme)))
                Error("BR-CL-10", $"Identifier scheme '{id.Scheme}' is not an ISO 6523 ICD code.", idTerm);
            if (p.LegalRegistrationIdentifier is { } lr && Has(lr.Scheme) && !CodeList.IsRegistrationIdentifierScheme(lr.Scheme))
                Error("BR-CL-11", $"Registration identifier scheme '{lr.Scheme}' is not an ISO 6523 ICD code.", legalTerm);
            if (p.ElectronicAddress is { } ea && Has(ea.Value))
            {
                if (!Has(ea.Scheme)) Error(addressRule, "An electronic address shall have a scheme identifier.", addressTerm);
                else if (!CodeList.IsElectronicAddressScheme(ea.Scheme))
                    Error("BR-CL-25", $"Electronic address scheme '{ea.Scheme}' is not in the CEF EAS list.", addressTerm);
            }
        }

        // ── lines ─────────────────────────────────────────────────────────────

        private void Lines()
        {
            if (_inv.Lines.Count == 0)
            {
                Error("BR-16", "An invoice shall have at least one invoice line.", "BG-25");
                return;
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < _inv.Lines.Count; i++)
            {
                var l = _inv.Lines[i];
                string where = $"line {(Has(l.Id) ? l.Id : (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture))}";
                if (Has(l.Id) && !ids.Add(l.Id)) Error("BR-21", $"Invoice line identifier '{l.Id}' is used twice.", "BT-126");
                if (!Has(l.UnitCode)) Error("BR-23", $"{where}: the invoiced quantity shall have a unit of measure.", "BT-130");
                else if (!CodeList.IsUnitCode(l.UnitCode)) Error("BR-CL-23", $"{where}: '{l.UnitCode}' is not a UN/ECE Rec 20 unit code.", "BT-130");
                if (Has(l.PriceBaseQuantityUnitCode) && !CodeList.IsUnitCode(l.PriceBaseQuantityUnitCode))
                    Error("BR-CL-23", $"{where}: '{l.PriceBaseQuantityUnitCode}' is not a UN/ECE Rec 20 unit code.", "BT-150");
                if (!Has(l.Item.Name)) Error("BR-25", $"{where}: each invoice line shall contain the item name.", "BT-153");
                if (l.NetPrice < 0m) Error("BR-27", $"{where}: the item net price shall not be negative. Put the sign on the quantity instead.", "BT-146");
                if (l.GrossPrice is < 0m) Error("BR-28", $"{where}: the item gross price shall not be negative.", "BT-148");
                if (l.PriceBaseQuantity is <= 0m) Error("HIVE-01", $"{where}: the price base quantity shall be positive.", "BT-149");
                PeriodOrder("BR-30", l.Period, $"{where}: the line period end date", "BT-135");
                if (l.Item.StandardIdentifier is { } sid && Has(sid.Value) && !Has(sid.Scheme))
                    Error("BR-64", $"{where}: the item standard identifier shall have a scheme identifier.", "BT-157-1");
                foreach (var cl in l.Item.Classifications)
                {
                    if (!Has(cl.Scheme)) Error("BR-65", $"{where}: an item classification shall have a scheme identifier.", "BT-158-1");
                    else if (!CodeList.IsItemClassificationScheme(cl.Scheme))
                        Error("BR-CL-13", $"{where}: '{cl.Scheme}' is not a UNTDID 7143 classification scheme.", "BT-158-1");
                }
                if (Has(l.Item.OriginCountryCode) && !CodeList.IsCountryCode(l.Item.OriginCountryCode))
                    Error("BR-CL-15", $"{where}: '{l.Item.OriginCountryCode}' is not an ISO 3166-1 alpha-2 country code.", "BT-159");
                foreach (var a in l.Item.Attributes)
                    if (!Has(a.Name) || !Has(a.Value)) Error("BR-54", $"{where}: each item attribute shall have a name and a value.", "BG-32");
                foreach (var ac in l.AllowancesAndCharges)
                {
                    string kind = ac.IsCharge ? "charge" : "allowance";
                    if (ac.Amount is null && (ac.BaseAmount is null || ac.Percentage is null))
                        Error(ac.IsCharge ? "BR-43" : "BR-41", $"{where}: each line {kind} shall have an amount.", ac.IsCharge ? "BT-141" : "BT-136");
                    if (!Has(ac.Reason) && !Has(ac.ReasonCode))
                        Error(ac.IsCharge ? "BR-44" : "BR-42", $"{where}: each line {kind} shall have a reason or a reason code.", ac.IsCharge ? "BT-144" : "BT-139");
                    ReasonCode(ac, ac.IsCharge ? "BT-145" : "BT-140");
                }
                LineVat(l, where);
            }
            PeriodOrder("BR-29", _inv.InvoicingPeriod, "the invoicing period end date", "BT-74");
        }

        private void PeriodOrder(string rule, Period? period, string what, string bt)
        {
            if (period is { Start: { } start, End: { } end } && end.Date < start.Date)
                Error(rule, $"{what} shall be on or after its start date.", bt);
        }

        private void LineVat(InvoiceLine l, string where)
        {
            switch (l.VatCategory)
            {
                case VatCategory.StandardRate when l.VatRate is not > 0m:
                    Error("BR-S-05", $"{where}: a line with category S shall have a VAT rate greater than zero.", "BT-152");
                    break;
                case VatCategory.ZeroRated when l.VatRate is not null and not 0m:
                    Error("BR-Z-05", $"{where}: a line with category Z shall have a VAT rate of 0.", "BT-152");
                    break;
                case VatCategory.Exempt when l.VatRate is not null and not 0m:
                    Error("BR-E-05", $"{where}: a line with category E shall have a VAT rate of 0.", "BT-152");
                    break;
                case VatCategory.ReverseCharge when l.VatRate is not null and not 0m:
                    Error("BR-AE-05", $"{where}: a line with category AE shall have a VAT rate of 0.", "BT-152");
                    break;
                case VatCategory.IntraCommunitySupply when l.VatRate is not null and not 0m:
                    Error("BR-IC-05", $"{where}: a line with category K shall have a VAT rate of 0.", "BT-152");
                    break;
                case VatCategory.ExportOutsideEu when l.VatRate is not null and not 0m:
                    Error("BR-G-05", $"{where}: a line with category G shall have a VAT rate of 0.", "BT-152");
                    break;
                case VatCategory.NotSubjectToVat when l.VatRate is not null:
                    Error("BR-O-05", $"{where}: a line with category O shall not contain a VAT rate.", "BT-152");
                    break;
                case VatCategory.CanaryIslands or VatCategory.CeutaMelilla when l.VatRate is null or < 0m:
                    Error(l.VatCategory == VatCategory.CanaryIslands ? "BR-AF-05" : "BR-AG-05",
                        $"{where}: a line with category {l.VatCategory.ToCode()} shall have a VAT rate of 0 or more.", "BT-152");
                    break;
            }
        }

        // ── document level allowances and charges ─────────────────────────────

        private void AllowancesAndCharges()
        {
            foreach (var ac in _inv.AllowancesAndCharges)
            {
                bool charge = ac.IsCharge;
                string kind = charge ? "document level charge" : "document level allowance";
                if (ac.Amount is null && (ac.BaseAmount is null || ac.Percentage is null))
                    Error(charge ? "BR-36" : "BR-31", $"Each {kind} shall have an amount.", charge ? "BT-99" : "BT-92");
                if (!Has(ac.Reason) && !Has(ac.ReasonCode))
                    Error(charge ? "BR-39" : "BR-33", $"Each {kind} shall have a reason or a reason code.", charge ? "BT-104" : "BT-97");
                ReasonCode(ac, charge ? "BT-105" : "BT-98");
                if (ac.VatCategory == VatCategory.StandardRate && ac.VatRate is not > 0m)
                    Error(charge ? "BR-S-07" : "BR-S-06", $"A {kind} with category S shall have a VAT rate greater than zero.", charge ? "BT-103" : "BT-96");
                if (ac.VatCategory == VatCategory.NotSubjectToVat && ac.VatRate is not null)
                    Error(charge ? "BR-O-07" : "BR-O-06", $"A {kind} with category O shall not contain a VAT rate.", charge ? "BT-103" : "BT-96");
            }
        }

        private void ReasonCode(AllowanceCharge ac, string bt)
        {
            if (!Has(ac.ReasonCode)) return;
            if (ac.IsCharge && !CodeList.IsChargeReasonCode(ac.ReasonCode))
                Error("BR-CL-20", $"'{ac.ReasonCode}' is not a UNTDID 7161 charge reason code.", bt);
            if (!ac.IsCharge && !CodeList.IsAllowanceReasonCode(ac.ReasonCode))
                Error("BR-CL-19", $"'{ac.ReasonCode}' is not a UNTDID 5189 allowance reason code.", bt);
        }

        // ── payment ───────────────────────────────────────────────────────────

        private void Payment()
        {
            var totals = InvoiceCalculator.Calculate(_inv);
            if (totals.AmountDue > 0m && _inv.DueDate is null && PaymentTermsFormatter.Format(_inv) is null)
                // EN 16931 states BR-CO-25, but the official CII and UBL artefacts do not assert it.
                Warn("BR-CO-25", "A positive amount due should come with a payment due date or payment terms.", "BT-9, BT-20");

            var p = _inv.PaymentInstructions;
            if (p is null) return;
            if (!Has(p.MeansCode)) Error("BR-49", "Payment instructions shall have a payment means type code.", "BT-81");
            else if (!CodeList.IsPaymentMeansCode(p.MeansCode))
                Error("BR-CL-16", $"'{p.MeansCode}' is not a UNTDID 4461 payment means code.", "BT-81");

            if (p.MeansCode is PaymentMeansCode.CreditTransfer or PaymentMeansCode.SepaCreditTransfer
                && !p.CreditTransfers.Any(a => Has(a.AccountIdentifier)))
                Error("BR-61", "A credit transfer shall have a payment account identifier.", "BT-84");
            if (p.MeansCode == PaymentMeansCode.SepaCreditTransfer)
            {
                foreach (var a in p.CreditTransfers.Where(a => Has(a.AccountIdentifier) && !Iban.IsValid(a.AccountIdentifier)))
                    Warn("BR-DE-19", $"'{a.AccountIdentifier}' is not a valid IBAN, which SEPA credit transfer expects.", "BT-84");
            }
            if (p.Card is { } card)
            {
                string digits = new string(card.PrimaryAccountNumber.Where(char.IsDigit).ToArray());
                if (digits.Length < 4 || digits.Length > 6)
                    Error("BR-51", "Only the last 4 to 6 digits of the payment card number shall be given.", "BT-87");
            }
        }

        // ── VAT ───────────────────────────────────────────────────────────────

        private void Vat()
        {
            var categories = new HashSet<VatCategory>(_inv.Lines.Select(l => l.VatCategory)
                .Concat(_inv.AllowancesAndCharges.Select(a => a.VatCategory)));
            var s = _inv.Seller;
            bool sellerVat = Has(s.VatIdentifier);
            bool sellerTaxId = Has(s.TaxRegistrationIdentifier);
            bool rep = Has(_inv.SellerTaxRepresentative?.VatIdentifier);
            bool buyerVat = Has(_inv.Buyer.VatIdentifier);
            bool buyerLegal = Has(_inv.Buyer.LegalRegistrationIdentifier?.Value);

            void SellerIdentified(string rule, string category, bool taxIdCounts)
            {
                if (!sellerVat && !rep && !(taxIdCounts && sellerTaxId))
                    Error(rule, $"An invoice with VAT category {category} shall contain the seller VAT identifier{(taxIdCounts ? ", the seller tax registration identifier" : "")} or the seller tax representative VAT identifier.", "BT-31, BT-32, BT-63");
            }

            void Exemption(string rule, VatCategory category)
            {
                var e = _inv.VatExemptions.FirstOrDefault(x => x.Category == category);
                if (e is null || (!Has(e.Reason) && !Has(e.ReasonCode)))
                    Error(rule, $"The VAT breakdown for category {category.ToCode()} shall have an exemption reason or reason code.", "BT-120, BT-121");
                else if (Has(e.ReasonCode) && !CodeList.IsVatExemptionReasonCode(e.ReasonCode))
                    Error("BR-CL-22", $"'{e.ReasonCode}' is not a VATEX code.", "BT-121");
            }

            if (categories.Contains(VatCategory.StandardRate)) SellerIdentified("BR-S-02", "S", true);
            if (categories.Contains(VatCategory.ZeroRated)) SellerIdentified("BR-Z-02", "Z", true);
            if (categories.Contains(VatCategory.Exempt))
            {
                SellerIdentified("BR-E-02", "E", true);
                Exemption("BR-E-10", VatCategory.Exempt);
            }
            if (categories.Contains(VatCategory.ReverseCharge))
            {
                SellerIdentified("BR-AE-02", "AE", false);
                if (!buyerVat && !buyerLegal)
                    Error("BR-AE-02", "An invoice with VAT category AE shall contain the buyer VAT identifier or legal registration identifier.", "BT-47, BT-48");
                Exemption("BR-AE-10", VatCategory.ReverseCharge);
            }
            if (categories.Contains(VatCategory.IntraCommunitySupply))
            {
                SellerIdentified("BR-IC-02", "K", false);
                if (!buyerVat) Error("BR-IC-02", "An invoice with VAT category K shall contain the buyer VAT identifier.", "BT-48");
                Exemption("BR-IC-10", VatCategory.IntraCommunitySupply);
                if (_inv.Delivery?.ActualDeliveryDate is null && _inv.InvoicingPeriod is null)
                    Error("BR-IC-11", "An invoice with VAT category K shall contain the actual delivery date or the invoicing period.", "BT-72, BG-14");
                if (!Has(_inv.Delivery?.Address?.CountryCode))
                    Error("BR-IC-12", "An invoice with VAT category K shall contain the deliver to country code.", "BT-80");
            }
            if (categories.Contains(VatCategory.ExportOutsideEu))
            {
                SellerIdentified("BR-G-02", "G", false);
                Exemption("BR-G-10", VatCategory.ExportOutsideEu);
            }
            if (categories.Contains(VatCategory.NotSubjectToVat))
            {
                Exemption("BR-O-10", VatCategory.NotSubjectToVat);
                if (sellerVat || rep || buyerVat)
                    Error("BR-O-02", "An invoice with VAT category O shall not contain the seller, seller tax representative or buyer VAT identifier.", "BT-31, BT-48, BT-63");
                if (categories.Count > 1)
                    Error("BR-O-11", "An invoice with VAT category O shall not contain any other VAT category.", "BG-23");
            }

            foreach (var e in _inv.VatExemptions.Where(e => !e.Category.RequiresExemptionReason()))
                if (Has(e.Reason) || Has(e.ReasonCode))
                    Error(e.Category == VatCategory.StandardRate ? "BR-S-10" : "BR-Z-10",
                        $"The VAT breakdown for category {e.Category.ToCode()} shall not have an exemption reason.", "BT-120, BT-121");
        }

        // ── remaining document level terms ────────────────────────────────────

        private void Miscellaneous()
        {
            foreach (var d in _inv.SupportingDocuments)
            {
                if (!Has(d.Reference)) Error("BR-52", "Each additional supporting document shall have a reference.", "BT-122");
                if (d.Content is not null && Has(d.MimeCode) && !CodeList.IsMimeCode(d.MimeCode))
                    Error("BR-CL-24", $"'{d.MimeCode}' is not a permitted attachment MIME code.", "BT-125-1");
            }
            foreach (var p in _inv.PrecedingInvoices)
                if (!Has(p.Number)) Error("BR-55", "Each preceding invoice reference shall contain the preceding invoice number.", "BT-25");
            foreach (var n in _inv.Notes.Where(n => Has(n.SubjectCode) && !CodeList.IsTextSubjectCode(n.SubjectCode)))
                Error("BR-CL-08", $"'{n.SubjectCode}' is not a UNTDID 4451 subject code.", "BT-21");
            if (Has(_inv.TaxPointDateCode) && !CodeList.IsVatDateCode(_inv.TaxPointDateCode))
                Error("BR-CL-06", $"'{_inv.TaxPointDateCode}' is not a VAT point date code.", "BT-8");
            if (_inv.TaxPointDate is not null && Has(_inv.TaxPointDateCode))
                Error("BR-CO-03", "The VAT point date and the VAT point date code are mutually exclusive.", "BT-7, BT-8");
        }

        // ── XRechnung (German CIUS) ───────────────────────────────────────────

        private void XRechnung()
        {
            var s = _inv.Seller;
            var b = _inv.Buyer;
            if (!Has(_inv.BuyerReference)) Error("BR-DE-15", "XRechnung requires the buyer reference, e.g. the Leitweg-ID.", "BT-10");
            if (!Has(_inv.BusinessProcess)) Error("PEPPOL-EN16931-R001", $"XRechnung 3.0 requires the business process, normally {Invoice.PeppolBillingProcess}.", "BT-23");
            if (!XRechnungTypeCodes.Contains((int)_inv.TypeCode))
                Warn("BR-DE-17", $"XRechnung expects the document types 326, 380, 381, 384, 389, 875, 876 or 877, not {_inv.TypeCode.ToCode()}.", "BT-3");
            if (_inv.PaymentInstructions is null) Error("BR-DE-1", "XRechnung requires payment instructions.", "BG-16");
            if (!Has(s.Address.City)) Error("BR-DE-3", "XRechnung requires the seller city.", "BT-37");
            if (!Has(s.Address.PostCode)) Error("BR-DE-4", "XRechnung requires the seller post code.", "BT-38");
            if (!Has(b.Address.City)) Error("BR-DE-8", "XRechnung requires the buyer city.", "BT-52");
            if (!Has(b.Address.PostCode)) Error("BR-DE-9", "XRechnung requires the buyer post code.", "BT-53");
            if (_inv.Delivery?.Address is { } da)
            {
                if (!Has(da.City)) Error("BR-DE-10", "XRechnung requires the deliver to city when a deliver to address is given.", "BT-77");
                if (!Has(da.PostCode)) Error("BR-DE-11", "XRechnung requires the deliver to post code when a deliver to address is given.", "BT-78");
            }

            if (s.Contact is not { } c) Error("BR-DE-2", "XRechnung requires a seller contact with name, phone and email.", "BG-6");
            else
            {
                if (!Has(c.Name)) Error("BR-DE-5", "XRechnung requires the seller contact point (name or department).", "BT-41");
                if (!Has(c.Phone)) Error("BR-DE-6", "XRechnung requires the seller contact telephone number.", "BT-42");
                else if (c.Phone!.Count(char.IsDigit) < 3) Warn("BR-DE-27", "The seller contact telephone number should contain at least three digits.", "BT-42");
                if (!Has(c.Email)) Error("BR-DE-7", "XRechnung requires the seller contact email address.", "BT-43");
                else if (!EmailPattern.IsMatch(c.Email!.Trim())) Warn("BR-DE-28", "The seller contact email address should be a valid address.", "BT-43");
            }

            if (s.ElectronicAddress is null || !Has(s.ElectronicAddress.Value))
                Error("PEPPOL-EN16931-R020", "XRechnung 3.0 requires the seller electronic address (e.g. email with scheme EM).", "BT-34");
            if (b.ElectronicAddress is null || !Has(b.ElectronicAddress.Value))
                Error("PEPPOL-EN16931-R010", "XRechnung 3.0 requires the buyer electronic address (e.g. the Leitweg-ID with scheme 0204 or email with EM).", "BT-49");

            var categories = _inv.Lines.Select(l => l.VatCategory).Concat(_inv.AllowancesAndCharges.Select(a => a.VatCategory));
            if (categories.Any(cat => cat != VatCategory.NotSubjectToVat)
                && !Has(s.VatIdentifier) && !Has(s.TaxRegistrationIdentifier) && !Has(_inv.SellerTaxRepresentative?.VatIdentifier))
                Error("BR-DE-16", "XRechnung requires the seller VAT identifier, tax number or tax representative when VAT is charged or exempted.", "BT-31, BT-32, BT-63");
            foreach (var l in _inv.Lines.Where(l => l.VatCategory != VatCategory.NotSubjectToVat && InvoiceCalculator.EffectiveRate(l.VatCategory, l.VatRate) is null))
                Error("BR-DE-14", $"XRechnung requires a VAT rate on line {l.Id}.", "BT-152");

            if (_inv.PaymentInstructions is { } p)
            {
                bool transfer = p.CreditTransfers.Any(a => Has(a.AccountIdentifier));
                bool card = p.Card is not null;
                bool debit = p.DirectDebit is not null;
                switch (p.MeansCode)
                {
                    case PaymentMeansCode.CreditTransfer or PaymentMeansCode.SepaCreditTransfer:
                        if (!transfer || card || debit)
                            Error("BR-DE-23-a", "With payment means 30 or 58, give a credit transfer account (BG-17) and neither card (BG-18) nor direct debit (BG-19).", "BG-17");
                        break;
                    case "48" or "54" or "55":
                        if (!card || transfer || debit)
                            Error("BR-DE-24-a", "With a card payment means, give the card (BG-18) and neither credit transfer (BG-17) nor direct debit (BG-19).", "BG-18");
                        break;
                    case PaymentMeansCode.SepaDirectDebit:
                        if (!debit || transfer || card)
                            Error("BR-DE-25-a", "With payment means 59, give the direct debit (BG-19) and neither credit transfer (BG-17) nor card (BG-18).", "BG-19");
                        break;
                }
                if (debit)
                {
                    if (!Has(p.DirectDebit!.CreditorIdentifier)) Error("BR-DE-30", "A direct debit requires the bank assigned creditor identifier.", "BT-90");
                    if (!Has(p.DirectDebit.DebitedAccountIdentifier)) Error("BR-DE-31", "A direct debit requires the debited account identifier.", "BT-91");
                    else if (!Iban.IsValid(p.DirectDebit.DebitedAccountIdentifier)) Warn("BR-DE-20", "The debited account should be a valid IBAN.", "BT-91");
                }
            }

            if (_inv.TypeCode == InvoiceTypeCode.CorrectedInvoice && _inv.PrecedingInvoices.Count == 0)
                Warn("BR-DE-26", "A corrected invoice should reference the preceding invoice.", "BG-3");

            var fileNames = _inv.SupportingDocuments.Where(d => d.Content is not null && Has(d.FileName)).Select(d => d.FileName!).ToList();
            if (fileNames.Count != fileNames.Distinct(StringComparer.OrdinalIgnoreCase).Count())
                Error("BR-DE-22", "Attached documents shall have unique file names.", "BT-125-2");
        }

        // ── Peppol BIS 3.0 ────────────────────────────────────────────────────

        private void Peppol()
        {
            if (!Has(_inv.BuyerReference) && !Has(_inv.PurchaseOrderReference))
                Error("PEPPOL-EN16931-R003", "Peppol requires a buyer reference or a purchase order reference.", "BT-10, BT-13");
            if (_inv.Seller.ElectronicAddress is null) Error("PEPPOL-EN16931-R020", "Peppol requires the seller electronic address.", "BT-34");
            if (_inv.Buyer.ElectronicAddress is null) Error("PEPPOL-EN16931-R010", "Peppol requires the buyer electronic address.", "BT-49");
            if (_inv.BusinessProcess != Invoice.PeppolBillingProcess)
                Error("PEPPOL-EN16931-R001", $"Peppol requires business process {Invoice.PeppolBillingProcess}.", "BT-23");
        }
    }
}
