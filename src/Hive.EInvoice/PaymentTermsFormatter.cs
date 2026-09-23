using System.Globalization;
using System.Linq;
using System.Text;

namespace Hive.EInvoice;

/// <summary>
/// Builds BT-20 from the free payment terms text and the cash discounts, using the
/// XRechnung Skonto syntax (XRechnung specification, chapter "Skonto"; rule BR-DE-18):
/// one line per discount, <c>#SKONTO#TAGE=7#PROZENT=2.00#</c> with an optional
/// <c>BASISBETRAG=123.45#</c>, and a line break after the last line.
/// </summary>
public static class PaymentTermsFormatter
{
    /// <summary>The BT-20 text for <paramref name="invoice"/>, or null when there is nothing to say.</summary>
    public static string? Format(Invoice invoice)
    {
        string text = (invoice.PaymentTerms ?? "").Trim();
        var discounts = invoice.CashDiscounts.Where(d => d.Percent > 0m && d.Days >= 0).ToList();
        if (discounts.Count == 0) return text.Length == 0 ? null : text;

        var sb = new StringBuilder();
        if (text.Length > 0) sb.Append(text).Append('\n');
        foreach (var d in discounts)
        {
            sb.Append("#SKONTO#TAGE=").Append(d.Days.ToString(CultureInfo.InvariantCulture))
              .Append("#PROZENT=").Append(d.Percent.ToString("0.00", CultureInfo.InvariantCulture)).Append('#');
            if (d.BaseAmount is { } basis)
                sb.Append("BASISBETRAG=").Append(InvoiceCalculator.Round(basis).ToString("0.00", CultureInfo.InvariantCulture)).Append('#');
            sb.Append('\n');
        }
        return sb.ToString();
    }
}
