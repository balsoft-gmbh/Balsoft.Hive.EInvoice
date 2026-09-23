using System.Collections.Generic;

namespace Hive.EInvoice.Reading;

/// <summary>The syntax of an e-invoice document.</summary>
public enum InvoiceSyntax
{
    /// <summary>UN/CEFACT Cross Industry Invoice (CII, D16B).</summary>
    Cii,
    /// <summary>OASIS UBL 2.1 Invoice or CreditNote.</summary>
    Ubl,
}

/// <summary>Totals as stated in a read document (BG-22), for comparison with the recalculated ones.</summary>
public sealed class StatedTotals
{
    /// <summary>BT-106 Sum of invoice line net amounts.</summary>
    public decimal? LineNetTotal { get; internal set; }

    /// <summary>BT-107 Sum of allowances on document level.</summary>
    public decimal? AllowanceTotal { get; internal set; }

    /// <summary>BT-108 Sum of charges on document level.</summary>
    public decimal? ChargeTotal { get; internal set; }

    /// <summary>BT-109 Invoice total amount without VAT.</summary>
    public decimal? TaxExclusiveAmount { get; internal set; }

    /// <summary>BT-110 Invoice total VAT amount.</summary>
    public decimal? VatTotal { get; internal set; }

    /// <summary>BT-112 Invoice total amount with VAT.</summary>
    public decimal? TaxInclusiveAmount { get; internal set; }

    /// <summary>BT-115 Amount due for payment.</summary>
    public decimal? AmountDue { get; internal set; }
}

/// <summary>The outcome of reading an e-invoice document.</summary>
public sealed class ReadResult
{
    internal ReadResult(Invoice invoice, InvoiceSyntax syntax, string specificationIdentifier, InvoiceProfile? profile, StatedTotals stated)
    {
        Invoice = invoice;
        Syntax = syntax;
        SpecificationIdentifier = specificationIdentifier;
        Profile = profile;
        StatedTotals = stated;
    }

    /// <summary>The invoice. Totals are recalculated by <see cref="InvoiceCalculator"/>.</summary>
    public Invoice Invoice { get; }

    /// <summary>CII or UBL.</summary>
    public InvoiceSyntax Syntax { get; }

    /// <summary>BT-24 as found in the document.</summary>
    public string SpecificationIdentifier { get; }

    /// <summary>The profile BT-24 identifies, or null for a specification this library does not know.</summary>
    public InvoiceProfile? Profile { get; }

    /// <summary>The totals the document states.</summary>
    public StatedTotals StatedTotals { get; }

    /// <summary>
    /// Differences between the stated and the recalculated totals, e.g. "BT-115 stated 100.00,
    /// calculated 100.01". Empty when the document's arithmetic agrees with EN 16931.
    /// </summary>
    public IReadOnlyList<string> TotalsDiscrepancies()
    {
        var calc = Invoice.CalculateTotals();
        var list = new List<string>();
        void Check(string bt, decimal? stated, decimal calculated)
        {
            if (stated is { } s && s != calculated)
                list.Add($"{bt} stated {s:0.00}, calculated {calculated:0.00}");
        }
        Check("BT-106", StatedTotals.LineNetTotal, calc.LineNetTotal);
        Check("BT-107", StatedTotals.AllowanceTotal, calc.AllowanceTotal);
        Check("BT-108", StatedTotals.ChargeTotal, calc.ChargeTotal);
        Check("BT-109", StatedTotals.TaxExclusiveAmount, calc.TaxExclusiveAmount);
        Check("BT-110", StatedTotals.VatTotal, calc.VatTotal);
        Check("BT-112", StatedTotals.TaxInclusiveAmount, calc.TaxInclusiveAmount);
        Check("BT-115", StatedTotals.AmountDue, calc.AmountDue);
        return list;
    }
}
