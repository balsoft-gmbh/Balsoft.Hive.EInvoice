using System;
using System.Collections.Generic;

namespace Hive.EInvoice;

/// <summary>
/// An EN 16931 invoice. Every property is named after the business term it carries, with
/// the term number (BT-n, BG-n) in its documentation. Totals (BG-22) and the VAT breakdown
/// (BG-23) are not set by hand: <see cref="InvoiceCalculator"/> derives them from the lines,
/// allowances and charges exactly as EN 16931 prescribes, and every writer uses it.
/// </summary>
public sealed class Invoice
{
    /// <summary>BT-1 Invoice number. Mandatory.</summary>
    public string Number { get; set; } = "";

    /// <summary>BT-2 Invoice issue date (the time of day is ignored). Mandatory.</summary>
    public DateTime IssueDate { get; set; } = DateTime.Today;

    /// <summary>BT-3 Invoice type code. Defaults to 380 (commercial invoice).</summary>
    public InvoiceTypeCode TypeCode { get; set; } = InvoiceTypeCode.CommercialInvoice;

    /// <summary>BT-5 Invoice currency code (ISO 4217). Defaults to EUR.</summary>
    public string Currency { get; set; } = "EUR";

    /// <summary>BT-6 VAT accounting currency code, when VAT is accounted in another currency.</summary>
    public string? TaxCurrency { get; set; }

    /// <summary>BT-111 Invoice total VAT amount in accounting currency. Required when <see cref="TaxCurrency"/> is set.</summary>
    public decimal? VatTotalInTaxCurrency { get; set; }

    /// <summary>BT-7 Value added tax point date.</summary>
    public DateTime? TaxPointDate { get; set; }

    /// <summary>BT-8 Value added tax point date code (UNTDID 2005 in UBL, 2475 in CII: 5, 29 or 72).</summary>
    public string? TaxPointDateCode { get; set; }

    /// <summary>BT-9 Payment due date.</summary>
    public DateTime? DueDate { get; set; }

    /// <summary>BT-10 Buyer reference, for example the German Leitweg-ID. Mandatory in XRechnung.</summary>
    public string? BuyerReference { get; set; }

    /// <summary>BT-11 Project reference.</summary>
    public string? ProjectReference { get; set; }

    /// <summary>BT-12 Contract reference.</summary>
    public string? ContractReference { get; set; }

    /// <summary>BT-13 Purchase order reference.</summary>
    public string? PurchaseOrderReference { get; set; }

    /// <summary>BT-14 Sales order reference.</summary>
    public string? SalesOrderReference { get; set; }

    /// <summary>BT-15 Receiving advice reference.</summary>
    public string? ReceivingAdviceReference { get; set; }

    /// <summary>BT-16 Despatch advice reference.</summary>
    public string? DespatchAdviceReference { get; set; }

    /// <summary>BT-17 Tender or lot reference.</summary>
    public string? TenderOrLotReference { get; set; }

    /// <summary>BT-18 Invoiced object identifier, with its scheme (BT-18-1, UNTDID 1153).</summary>
    public SchemedIdentifier? InvoicedObjectIdentifier { get; set; }

    /// <summary>BT-19 Buyer accounting reference.</summary>
    public string? BuyerAccountingReference { get; set; }

    /// <summary>
    /// BT-20 Payment terms, free text. <see cref="CashDiscounts"/> are appended in the
    /// XRechnung Skonto syntax when the document is written.
    /// </summary>
    public string? PaymentTerms { get; set; }

    /// <summary>
    /// Cash discounts (Skonto). Written into BT-20 as <c>#SKONTO#TAGE=n#PROZENT=p.pp#</c>
    /// lines (XRechnung BR-DE-18), which German receivers apply automatically.
    /// </summary>
    public List<CashDiscount> CashDiscounts { get; } = new();

    /// <summary>BG-1 Invoice notes.</summary>
    public List<InvoiceNote> Notes { get; } = new();

    /// <summary>The Peppol billing process identifier, the BT-23 value XRechnung 3.0 and Peppol require.</summary>
    public const string PeppolBillingProcess = "urn:fdc:peppol.eu:2017:poacc:billing:01:1.0";

    /// <summary>
    /// BT-23 Business process type. Defaults to <see cref="PeppolBillingProcess"/>, which
    /// XRechnung 3.0 (PEPPOL-EN16931-R001) and Peppol require. Set to null to omit it.
    /// </summary>
    public string? BusinessProcess { get; set; } = PeppolBillingProcess;

    /// <summary>BG-3 Preceding invoice references (for credit notes and corrections).</summary>
    public List<PrecedingInvoiceReference> PrecedingInvoices { get; } = new();

    /// <summary>BG-4 Seller. Mandatory.</summary>
    public Party Seller { get; set; } = new();

    /// <summary>BG-7 Buyer. Mandatory.</summary>
    public Party Buyer { get; set; } = new();

    /// <summary>BG-10 Payee, when different from the seller.</summary>
    public Payee? Payee { get; set; }

    /// <summary>BG-11 Seller tax representative party.</summary>
    public TaxRepresentative? SellerTaxRepresentative { get; set; }

    /// <summary>BG-13 Delivery information.</summary>
    public DeliveryInformation? Delivery { get; set; }

    /// <summary>BG-14 Invoicing period.</summary>
    public Period? InvoicingPeriod { get; set; }

    /// <summary>BG-16 Payment instructions. Mandatory in XRechnung.</summary>
    public PaymentInstructions? PaymentInstructions { get; set; }

    /// <summary>BG-20 document level allowances and BG-21 document level charges.</summary>
    public List<AllowanceCharge> AllowancesAndCharges { get; } = new();

    /// <summary>
    /// Exemption reasons for the VAT breakdown (BT-120, BT-121), one per VAT category that
    /// needs one (E, AE, K, G, O). The calculator attaches them to the matching BG-23 group.
    /// </summary>
    public List<VatExemption> VatExemptions { get; } = new();

    /// <summary>BT-113 Paid amount (prepayments), deducted from the amount due.</summary>
    public decimal? PrepaidAmount { get; set; }

    /// <summary>BT-114 Rounding amount, added to the amount due.</summary>
    public decimal? RoundingAmount { get; set; }

    /// <summary>BG-24 Additional supporting documents.</summary>
    public List<SupportingDocument> SupportingDocuments { get; } = new();

    /// <summary>BG-25 Invoice lines. At least one for every EN 16931 compliant profile.</summary>
    public List<InvoiceLine> Lines { get; } = new();

    /// <summary>Adds a line and returns it, numbering it 1..n when no identifier is given.</summary>
    public InvoiceLine AddLine(InvoiceLine line)
    {
        if (string.IsNullOrWhiteSpace(line.Id))
            line.Id = (Lines.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        Lines.Add(line);
        return line;
    }

    /// <summary>Totals and VAT breakdown of this invoice, calculated per EN 16931.</summary>
    public InvoiceTotals CalculateTotals() => InvoiceCalculator.Calculate(this);
}

/// <summary>BG-1 Invoice note.</summary>
public sealed class InvoiceNote
{
    /// <summary>Creates a note.</summary>
    public InvoiceNote() { }

    /// <summary>Creates a note with text and an optional subject code.</summary>
    public InvoiceNote(string text, string? subjectCode = null)
    {
        Text = text;
        SubjectCode = subjectCode;
    }

    /// <summary>BT-22 Invoice note text.</summary>
    public string Text { get; set; } = "";

    /// <summary>BT-21 Invoice note subject code (UNTDID 4451), for example "REG" or "AAI".</summary>
    public string? SubjectCode { get; set; }
}

/// <summary>BG-3 Preceding invoice reference.</summary>
public sealed class PrecedingInvoiceReference
{
    /// <summary>BT-25 Preceding invoice number.</summary>
    public string Number { get; set; } = "";

    /// <summary>BT-26 Preceding invoice issue date.</summary>
    public DateTime? IssueDate { get; set; }
}

/// <summary>A cash discount (Skonto) offered for early payment.</summary>
public sealed class CashDiscount
{
    /// <summary>Days from the issue date within which the discount applies.</summary>
    public int Days { get; set; }

    /// <summary>Discount percentage, for example 2 for 2 %.</summary>
    public decimal Percent { get; set; }

    /// <summary>Base amount the percentage applies to, when it is not the full amount due.</summary>
    public decimal? BaseAmount { get; set; }
}

/// <summary>An exemption reason for one VAT category in the breakdown (BT-120, BT-121).</summary>
public sealed class VatExemption
{
    /// <summary>The VAT category the reason belongs to.</summary>
    public VatCategory Category { get; set; }

    /// <summary>BT-120 VAT exemption reason text.</summary>
    public string? Reason { get; set; }

    /// <summary>BT-121 VAT exemption reason code (CEF VATEX list, e.g. "VATEX-EU-AE").</summary>
    public string? ReasonCode { get; set; }
}

/// <summary>An identifier with an optional scheme identifier.</summary>
public sealed class SchemedIdentifier
{
    /// <summary>Creates an empty identifier.</summary>
    public SchemedIdentifier() { }

    /// <summary>Creates an identifier with an optional scheme.</summary>
    public SchemedIdentifier(string value, string? scheme = null)
    {
        Value = value;
        Scheme = scheme;
    }

    /// <summary>The identifier.</summary>
    public string Value { get; set; } = "";

    /// <summary>The scheme identifier (ISO 6523 ICD, EAS or UNTDID 1153 depending on the term).</summary>
    public string? Scheme { get; set; }

    /// <inheritdoc />
    public override string ToString() => Scheme is null ? Value : $"{Scheme}:{Value}";
}

/// <summary>A date range (BG-14, BG-26).</summary>
public sealed class Period
{
    /// <summary>Creates an empty period.</summary>
    public Period() { }

    /// <summary>Creates a period.</summary>
    public Period(DateTime? start, DateTime? end)
    {
        Start = start;
        End = end;
    }

    /// <summary>Start date (BT-73, BT-134).</summary>
    public DateTime? Start { get; set; }

    /// <summary>End date (BT-74, BT-135).</summary>
    public DateTime? End { get; set; }
}

/// <summary>BG-24 Additional supporting document.</summary>
public sealed class SupportingDocument
{
    /// <summary>BT-122 Supporting document reference. Mandatory.</summary>
    public string Reference { get; set; } = "";

    /// <summary>BT-123 Supporting document description.</summary>
    public string? Description { get; set; }

    /// <summary>BT-124 External document location (URL).</summary>
    public string? ExternalLocation { get; set; }

    /// <summary>BT-125 Attached document content.</summary>
    public byte[]? Content { get; set; }

    /// <summary>BT-125-1 Attached document MIME code (application/pdf, image/png, image/jpeg, text/csv, xlsx, ods).</summary>
    public string? MimeCode { get; set; }

    /// <summary>BT-125-2 Attached document file name.</summary>
    public string? FileName { get; set; }
}
