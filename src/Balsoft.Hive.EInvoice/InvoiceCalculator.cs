using System;
using System.Collections.Generic;
using System.Linq;

namespace Balsoft.Hive.EInvoice;

/// <summary>BG-22 Document totals and BG-23 VAT breakdown, calculated per EN 16931.</summary>
public sealed class InvoiceTotals
{
    internal InvoiceTotals(IReadOnlyList<decimal> lineNetAmounts, decimal lineNetTotal, decimal allowanceTotal,
        decimal chargeTotal, decimal vatTotal, decimal? prepaid, decimal? rounding, IReadOnlyList<VatBreakdown> breakdown)
    {
        LineNetAmounts = lineNetAmounts;
        LineNetTotal = lineNetTotal;
        AllowanceTotal = allowanceTotal;
        ChargeTotal = chargeTotal;
        VatTotal = vatTotal;
        PrepaidAmount = prepaid;
        RoundingAmount = rounding;
        VatBreakdown = breakdown;
    }

    /// <summary>BT-131 Invoice line net amount of each line, in line order.</summary>
    public IReadOnlyList<decimal> LineNetAmounts { get; }

    /// <summary>BT-106 Sum of invoice line net amounts.</summary>
    public decimal LineNetTotal { get; }

    /// <summary>BT-107 Sum of allowances on document level.</summary>
    public decimal AllowanceTotal { get; }

    /// <summary>BT-108 Sum of charges on document level.</summary>
    public decimal ChargeTotal { get; }

    /// <summary>BT-109 Invoice total amount without VAT.</summary>
    public decimal TaxExclusiveAmount => LineNetTotal - AllowanceTotal + ChargeTotal;

    /// <summary>BT-110 Invoice total VAT amount.</summary>
    public decimal VatTotal { get; }

    /// <summary>BT-112 Invoice total amount with VAT.</summary>
    public decimal TaxInclusiveAmount => TaxExclusiveAmount + VatTotal;

    /// <summary>BT-113 Paid amount.</summary>
    public decimal? PrepaidAmount { get; }

    /// <summary>BT-114 Rounding amount.</summary>
    public decimal? RoundingAmount { get; }

    /// <summary>BT-115 Amount due for payment.</summary>
    public decimal AmountDue => TaxInclusiveAmount - (PrepaidAmount ?? 0m) + (RoundingAmount ?? 0m);

    /// <summary>BG-23 VAT breakdown, one entry per VAT category and rate.</summary>
    public IReadOnlyList<VatBreakdown> VatBreakdown { get; }
}

/// <summary>BG-23 VAT breakdown entry.</summary>
public sealed class VatBreakdown
{
    internal VatBreakdown(VatCategory category, decimal? rate, decimal taxableAmount, decimal taxAmount,
        string? exemptionReason, string? exemptionReasonCode)
    {
        Category = category;
        Rate = rate;
        TaxableAmount = taxableAmount;
        TaxAmount = taxAmount;
        ExemptionReason = exemptionReason;
        ExemptionReasonCode = exemptionReasonCode;
    }

    /// <summary>BT-118 VAT category code.</summary>
    public VatCategory Category { get; }

    /// <summary>BT-119 VAT category rate. Zero for category O (not subject to VAT); null only when a line lacks its rate.</summary>
    public decimal? Rate { get; }

    /// <summary>BT-116 VAT category taxable amount.</summary>
    public decimal TaxableAmount { get; }

    /// <summary>BT-117 VAT category tax amount.</summary>
    public decimal TaxAmount { get; }

    /// <summary>BT-120 VAT exemption reason text.</summary>
    public string? ExemptionReason { get; }

    /// <summary>BT-121 VAT exemption reason code.</summary>
    public string? ExemptionReasonCode { get; }
}

/// <summary>
/// EN 16931 calculation model. Amounts are rounded to two decimals, half away from zero, at
/// the points the standard defines: each line net amount (BT-131), each allowance or charge
/// calculated from a percentage, and each VAT category tax amount (BT-117, BR-CO-17).
/// </summary>
public static class InvoiceCalculator
{
    /// <summary>Rounds to two decimals, half away from zero (commercial rounding).</summary>
    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>The effective amount of an allowance or charge: as given, else base × percentage / 100.</summary>
    public static decimal AmountOf(AllowanceCharge allowanceCharge)
    {
        if (allowanceCharge.Amount is { } amount) return Round(amount);
        if (allowanceCharge.BaseAmount is { } baseAmount && allowanceCharge.Percentage is { } percentage)
            return Round(baseAmount * percentage / 100m);
        return 0m;
    }

    /// <summary>BT-131 net amount of one line: quantity × net price / base quantity, plus charges, minus allowances.</summary>
    public static decimal LineNetAmount(InvoiceLine line)
    {
        decimal baseQuantity = line.PriceBaseQuantity is { } b && b != 0m ? b : 1m;
        decimal amount = line.Quantity * line.NetPrice / baseQuantity;
        foreach (var ac in line.AllowancesAndCharges)
            amount += ac.IsCharge ? AmountOf(ac) : -AmountOf(ac);
        return Round(amount);
    }

    /// <summary>
    /// The VAT rate used for a category: as given, zero for the categories fixed at zero
    /// (Z, E, AE, K, G) when not given, and none for O.
    /// </summary>
    public static decimal? EffectiveRate(VatCategory category, decimal? rate) => category switch
    {
        VatCategory.NotSubjectToVat => null,
        VatCategory.ZeroRated or VatCategory.Exempt or VatCategory.ReverseCharge or VatCategory.IntraCommunitySupply
            or VatCategory.ExportOutsideEu => rate ?? 0m,
        _ => rate,
    };

    /// <summary>Calculates totals and VAT breakdown of <paramref name="invoice"/>.</summary>
    public static InvoiceTotals Calculate(Invoice invoice)
    {
        var lineNets = invoice.Lines.Select(LineNetAmount).ToList();
        decimal lineTotal = lineNets.Sum();

        decimal allowances = 0m, charges = 0m;
        foreach (var ac in invoice.AllowancesAndCharges)
        {
            if (ac.IsCharge) charges += AmountOf(ac);
            else allowances += AmountOf(ac);
        }

        // Group by (category, rate) in order of first appearance: lines first, then
        // document level allowances and charges, which carry their own category.
        var groups = new List<(VatCategory Category, decimal? Rate, decimal Basis)>();
        void Add(VatCategory category, decimal? rate, decimal amount)
        {
            var effective = EffectiveRate(category, rate);
            int i = groups.FindIndex(g => g.Category == category && g.Rate == effective);
            if (i < 0) groups.Add((category, effective, amount));
            else groups[i] = (category, effective, groups[i].Basis + amount);
        }

        for (int i = 0; i < invoice.Lines.Count; i++)
            Add(invoice.Lines[i].VatCategory, invoice.Lines[i].VatRate, lineNets[i]);
        foreach (var ac in invoice.AllowancesAndCharges)
            Add(ac.VatCategory, ac.VatRate, ac.IsCharge ? AmountOf(ac) : -AmountOf(ac));

        var breakdown = new List<VatBreakdown>();
        decimal vatTotal = 0m;
        foreach (var (category, rate, basis) in groups)
        {
            decimal tax = rate is { } r ? Round(basis * r / 100m) : 0m;
            vatTotal += tax;
            var exemption = invoice.VatExemptions.FirstOrDefault(e => e.Category == category);
            // A category O line carries no rate (BR-O-05), but its breakdown states 0: EN 16931
            // permits it and XRechnung requires BT-119 on every breakdown (BR-DE-14).
            decimal? breakdownRate = category == VatCategory.NotSubjectToVat ? 0m : rate;
            breakdown.Add(new VatBreakdown(category, breakdownRate, Round(basis), tax, exemption?.Reason, exemption?.ReasonCode));
        }

        return new InvoiceTotals(lineNets, lineTotal, allowances, charges, vatTotal,
            invoice.PrepaidAmount, invoice.RoundingAmount, breakdown);
    }
}
