using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Hive.EInvoice.Reading;

internal static class ReaderHelpers
{
    private static readonly Regex Skonto = new(@"^#SKONTO#TAGE=(\d+)#PROZENT=(\d+(?:\.\d+)?)#(?:BASISBETRAG=(-?\d+(?:\.\d+)?)#)?\s*$",
        RegexOptions.CultureInvariant);

    public static string? Value(this XElement? e) => e is null || string.IsNullOrWhiteSpace(e.Value) ? null : e.Value.Trim();

    public static string? Attr(this XElement? e, string name) => e?.Attribute(name)?.Value is { Length: > 0 } v ? v.Trim() : null;

    public static decimal? Decimal(this XElement? e)
        => e.Value() is { } v && decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;

    public static DateTime? Date(string? value)
    {
        if (value is null) return null;
        string[] formats = { "yyyyMMdd", "yyyy-MM-dd" };
        return DateTime.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    public static bool Truthy(string? value) => string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Splits BT-20 into the free text and the XRechnung Skonto lines.</summary>
    public static void ApplyPaymentTerms(Invoice invoice, string? terms)
    {
        if (terms is null) return;
        var free = new System.Collections.Generic.List<string>();
        foreach (string raw in terms.Replace("\r\n", "\n").Split('\n'))
        {
            string line = raw.Trim();
            var m = Skonto.Match(line);
            if (m.Success)
            {
                invoice.CashDiscounts.Add(new CashDiscount
                {
                    Days = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                    Percent = decimal.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                    BaseAmount = m.Groups[3].Success ? decimal.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : null,
                });
            }
            else if (line.Length > 0)
            {
                free.Add(line);
            }
        }
        invoice.PaymentTerms = free.Count == 0 ? null : string.Join("\n", free);
    }

    public static VatCategory Category(string? code)
        => CodeExtensions.TryParseVatCategory(code, out var c) ? c : VatCategory.StandardRate;

    public static InvoiceTypeCode TypeCode(string? code)
        => int.TryParse(code, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? (InvoiceTypeCode)n : InvoiceTypeCode.CommercialInvoice;

    public static PaymentInstructions EnsurePayment(Invoice invoice)
        => invoice.PaymentInstructions ??= new PaymentInstructions();

    public static DirectDebit EnsureDirectDebit(Invoice invoice)
        => EnsurePayment(invoice).DirectDebit ??= new DirectDebit();

    public static SchemedIdentifier? Id(XElement? e)
        => e.Value() is { } v ? new SchemedIdentifier(v, e.Attr("schemeID")) : null;

    public static bool IsEmpty(this PostalAddress a)
        => new[] { a.Line1, a.Line2, a.Line3, a.City, a.PostCode, a.CountrySubdivision, a.CountryCode }.All(string.IsNullOrWhiteSpace);
}
