namespace Balsoft.Hive.EInvoice.Tests;

/// <summary>
/// One broken XRechnung each: a valid sample with a single defect, the rule the pre-flight
/// validator must report, and the rule KoSIT reports for the same file. Keeping both in one
/// table proves the pre-flight rule identifiers match the official artefact.
/// </summary>
public static class NegativeCases
{
    /// <summary>How KoSIT treats the defect.</summary>
    public enum Kosit
    {
        /// <summary>Rejects the file with <see cref="Case.KositRule"/>.</summary>
        Rejects,
        /// <summary>Accepts the file but reports <see cref="Case.KositRule"/> as a warning.</summary>
        Warns,
        /// <summary>
        /// Does not check it: the rule is in EN 16931 but not asserted by the CII artefact
        /// (BR-CO-25) or shadowed by another rule (BR-CL-23). The pre-flight is stricter here.
        /// </summary>
        DoesNotCheck,
    }

    public sealed record Case(string Name, string ValidatorRule, string KositRule, Func<Invoice> Build, Kosit Behaviour = Kosit.Rejects, bool ValidatorWarns = false);

    private static Invoice With(Action<Invoice> mutate, Func<Invoice>? start = null)
    {
        var inv = (start ?? Samples.Standard)();
        mutate(inv);
        return inv;
    }

    public static IReadOnlyList<Case> All { get; } = new List<Case>
    {
        new("no-buyer-reference", "BR-DE-15", "BR-DE-15", () => With(i => i.BuyerReference = null)),
        new("no-seller-phone", "BR-DE-6", "BR-DE-6", () => With(i => i.Seller.Contact!.Phone = null)),
        new("no-seller-email", "BR-DE-7", "BR-DE-7", () => With(i => i.Seller.Contact!.Email = null)),
        new("no-seller-contact-name", "BR-DE-5", "BR-DE-5", () => With(i => i.Seller.Contact!.Name = null)),
        new("no-seller-contact", "BR-DE-2", "BR-DE-2", () => With(i => i.Seller.Contact = null)),
        new("no-payment-instructions", "BR-DE-1", "BR-DE-1", () => With(i => i.PaymentInstructions = null)),
        new("no-seller-city", "BR-DE-3", "BR-DE-3", () => With(i => i.Seller.Address.City = null)),
        new("no-seller-postcode", "BR-DE-4", "BR-DE-4", () => With(i => i.Seller.Address.PostCode = null)),
        new("no-buyer-city", "BR-DE-8", "BR-DE-8", () => With(i => i.Buyer.Address.City = null)),
        new("no-buyer-postcode", "BR-DE-9", "BR-DE-9", () => With(i => i.Buyer.Address.PostCode = null)),
        new("no-seller-electronic-address", "PEPPOL-EN16931-R020", "PEPPOL-EN16931-R020", () => With(i => i.Seller.ElectronicAddress = null)),
        new("no-buyer-electronic-address", "PEPPOL-EN16931-R010", "PEPPOL-EN16931-R010", () => With(i => i.Buyer.ElectronicAddress = null)),
        new("type-code-debit-note", "BR-DE-17", "BR-DE-17", () => With(i => i.TypeCode = InvoiceTypeCode.DebitNote), Kosit.Warns, ValidatorWarns: true),
        new("no-due-date-no-terms", "BR-CO-25", "BR-CO-25", () => With(i => { i.DueDate = null; i.PaymentTerms = null; }), Kosit.DoesNotCheck, ValidatorWarns: true),
        new("seller-without-tax-ids", "BR-DE-16", "BR-DE-16", () => With(i =>
        {
            i.Seller.VatIdentifier = null;
            i.Seller.TaxRegistrationIdentifier = null;
            i.Seller.LegalRegistrationIdentifier = new SchemedIdentifier("HRB 12345");
        })),
        new("negative-net-price", "BR-27", "BR-27", () => With(i => i.Lines[0].NetPrice = -120m)),
        new("standard-rate-zero", "BR-S-05", "BR-S-05", () => With(i => i.Lines[0].VatRate = 0m)),
        new("exempt-without-reason", "BR-E-10", "BR-E-10", () => With(i => i.VatExemptions.Clear(), Samples.Exempt)),
        new("reverse-charge-without-buyer-vat", "BR-AE-02", "BR-AE-02", () => With(i => i.Buyer.VatIdentifier = null, Samples.ReverseCharge)),
        new("intra-community-without-delivery-country", "BR-IC-12", "BR-IC-12", () => With(i => i.Delivery!.Address = null, Samples.IntraCommunity)),
        new("not-subject-with-seller-vat", "BR-O-02", "BR-O-02", () => With(i => i.Seller.VatIdentifier = "DE452983132", Samples.NotSubject)),
        new("unknown-unit-code", "BR-CL-23", "BR-CL-23", () => With(i => i.Lines[0].UnitCode = "STUNDE"), Kosit.DoesNotCheck),
        new("unknown-country", "BR-CL-14", "BR-CL-14", () => With(i => i.Buyer.Address.CountryCode = "XX")),
        new("unknown-address-scheme", "BR-CL-25", "BR-CL-25", () => With(i => i.Buyer.ElectronicAddress = new SchemedIdentifier("ap@muster-kunde.de", "MAIL"))),
        new("credit-transfer-without-account", "BR-DE-23-a", "BR-DE-23-a", () => With(i => i.PaymentInstructions!.CreditTransfers.Clear())),
        new("vat-id-without-prefix", "BR-CO-09", "BR-CO-09", () => With(i => i.Buyer.VatIdentifier = "123456789")),
        new("allowance-without-reason", "BR-33", "BR-33", () => With(i => { i.AllowancesAndCharges[0].Reason = null; i.AllowancesAndCharges[0].ReasonCode = null; }, Samples.AllowancesCharges)),
        new("period-end-before-start", "BR-29", "BR-29", () => With(i => i.InvoicingPeriod = new Period(new DateTime(2026, 8, 31), new DateTime(2026, 8, 1)))),
        new("direct-debit-without-creditor-id", "BR-DE-30", "BR-DE-30", () => With(i => i.PaymentInstructions!.DirectDebit!.CreditorIdentifier = null, Samples.DirectDebit)),
        new("line-without-item-name", "BR-25", "BR-25", () => With(i => i.Lines[0].Item.Name = "")),
        new("no-business-process", "PEPPOL-EN16931-R001", "PEPPOL-EN16931-R001", () => With(i => i.BusinessProcess = null)),
    };
}
