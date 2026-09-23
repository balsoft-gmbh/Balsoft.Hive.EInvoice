using System.Collections.Generic;

namespace Balsoft.Hive.EInvoice;

/// <summary>BG-25 Invoice line.</summary>
public sealed class InvoiceLine
{
    /// <summary>BT-126 Invoice line identifier. Filled with 1..n by <see cref="Invoice.AddLine"/> when empty.</summary>
    public string Id { get; set; } = "";

    /// <summary>BT-127 Invoice line note.</summary>
    public string? Note { get; set; }

    /// <summary>BT-128 Invoice line object identifier, with its scheme (BT-128-1, UNTDID 1153).</summary>
    public SchemedIdentifier? ObjectIdentifier { get; set; }

    /// <summary>BT-129 Invoiced quantity. Mandatory.</summary>
    public decimal Quantity { get; set; }

    /// <summary>BT-130 Invoiced quantity unit of measure code (UN/ECE Rec 20). Defaults to C62.</summary>
    public string UnitCode { get; set; } = EInvoice.UnitCode.One;

    /// <summary>BT-132 Referenced purchase order line reference.</summary>
    public string? OrderLineReference { get; set; }

    /// <summary>BT-133 Invoice line buyer accounting reference.</summary>
    public string? BuyerAccountingReference { get; set; }

    /// <summary>BG-26 Invoice line period.</summary>
    public Period? Period { get; set; }

    /// <summary>
    /// BG-27 line allowances and BG-28 line charges. Their VAT category is the line's; the
    /// VAT fields of <see cref="AllowanceCharge"/> are ignored here.
    /// </summary>
    public List<AllowanceCharge> AllowancesAndCharges { get; } = new();

    /// <summary>BT-146 Item net price, after any price discount, per <see cref="PriceBaseQuantity"/> units. Must not be negative.</summary>
    public decimal NetPrice { get; set; }

    /// <summary>BT-148 Item gross price, before the price discount.</summary>
    public decimal? GrossPrice { get; set; }

    /// <summary>BT-147 Item price discount. Derived as gross minus net price when not set and a gross price is given.</summary>
    public decimal? PriceDiscount { get; set; }

    /// <summary>BT-149 Item price base quantity. Defaults to 1.</summary>
    public decimal? PriceBaseQuantity { get; set; }

    /// <summary>BT-150 Item price base quantity unit of measure code.</summary>
    public string? PriceBaseQuantityUnitCode { get; set; }

    /// <summary>BT-151 Invoiced item VAT category code. Defaults to S (standard rate).</summary>
    public VatCategory VatCategory { get; set; } = VatCategory.StandardRate;

    /// <summary>BT-152 Invoiced item VAT rate in percent, e.g. 19. Zero for Z, E, AE, K, G; not written for O.</summary>
    public decimal? VatRate { get; set; }

    /// <summary>BG-31 Item information. Name is mandatory.</summary>
    public Item Item { get; set; } = new();

    /// <summary>Creates an empty line.</summary>
    public InvoiceLine() { }

    /// <summary>Creates a line from the fields most invoices need.</summary>
    public InvoiceLine(string itemName, decimal quantity, string unitCode, decimal netPrice, decimal vatRate,
        VatCategory vatCategory = VatCategory.StandardRate)
    {
        Item.Name = itemName;
        Quantity = quantity;
        UnitCode = unitCode;
        NetPrice = netPrice;
        VatRate = vatRate;
        VatCategory = vatCategory;
    }
}

/// <summary>BG-31 Item information.</summary>
public sealed class Item
{
    /// <summary>BT-153 Item name. Mandatory.</summary>
    public string Name { get; set; } = "";

    /// <summary>BT-154 Item description.</summary>
    public string? Description { get; set; }

    /// <summary>BT-155 Item seller's identifier.</summary>
    public string? SellersIdentifier { get; set; }

    /// <summary>BT-156 Item buyer's identifier.</summary>
    public string? BuyersIdentifier { get; set; }

    /// <summary>BT-157 Item standard identifier with its ICD scheme (e.g. 0160 for a GTIN).</summary>
    public SchemedIdentifier? StandardIdentifier { get; set; }

    /// <summary>BT-158 Item classification identifiers.</summary>
    public List<ItemClassification> Classifications { get; } = new();

    /// <summary>BT-159 Item country of origin (ISO 3166-1 alpha-2).</summary>
    public string? OriginCountryCode { get; set; }

    /// <summary>BG-32 Item attributes.</summary>
    public List<ItemAttribute> Attributes { get; } = new();
}

/// <summary>BT-158 Item classification identifier.</summary>
public sealed class ItemClassification
{
    /// <summary>BT-158 Item classification identifier.</summary>
    public string Code { get; set; } = "";

    /// <summary>BT-158-1 Scheme identifier (UNTDID 7143, e.g. "STI", "TST", "CV").</summary>
    public string Scheme { get; set; } = "";

    /// <summary>BT-158-2 Scheme version identifier.</summary>
    public string? SchemeVersion { get; set; }
}

/// <summary>BG-32 Item attribute.</summary>
public sealed class ItemAttribute
{
    /// <summary>BT-160 Item attribute name.</summary>
    public string Name { get; set; } = "";

    /// <summary>BT-161 Item attribute value.</summary>
    public string Value { get; set; } = "";
}

/// <summary>
/// An allowance (BG-20 document, BG-27 line) or a charge (BG-21 document, BG-28 line).
/// The amount is taken as given, or calculated from base amount and percentage.
/// </summary>
public sealed class AllowanceCharge
{
    /// <summary>False for an allowance (reduces the amount), true for a charge.</summary>
    public bool IsCharge { get; set; }

    /// <summary>BT-92/99/136/141 Amount, without VAT. Calculated as base × percentage / 100 when null.</summary>
    public decimal? Amount { get; set; }

    /// <summary>BT-93/100/137/142 Base amount.</summary>
    public decimal? BaseAmount { get; set; }

    /// <summary>BT-94/101/138/143 Percentage.</summary>
    public decimal? Percentage { get; set; }

    /// <summary>BT-97/104/139/144 Reason text.</summary>
    public string? Reason { get; set; }

    /// <summary>BT-98/105/140/145 Reason code (UNTDID 5189 for allowances, 7161 for charges).</summary>
    public string? ReasonCode { get; set; }

    /// <summary>BT-95/102 VAT category code. Mandatory on document level, ignored on line level.</summary>
    public VatCategory VatCategory { get; set; } = VatCategory.StandardRate;

    /// <summary>BT-96/103 VAT rate. Document level only.</summary>
    public decimal? VatRate { get; set; }

    /// <summary>Creates an allowance.</summary>
    public static AllowanceCharge Allowance(decimal amount, string? reason = null, string? reasonCode = null,
        VatCategory category = VatCategory.StandardRate, decimal? vatRate = null)
        => new() { IsCharge = false, Amount = amount, Reason = reason, ReasonCode = reasonCode, VatCategory = category, VatRate = vatRate };

    /// <summary>Creates a charge.</summary>
    public static AllowanceCharge Charge(decimal amount, string? reason = null, string? reasonCode = null,
        VatCategory category = VatCategory.StandardRate, decimal? vatRate = null)
        => new() { IsCharge = true, Amount = amount, Reason = reason, ReasonCode = reasonCode, VatCategory = category, VatRate = vatRate };
}
