using System.Text;

namespace Hive.EInvoice;

/// <summary>IBAN helpers (ISO 13616): compact form and the mod-97 check.</summary>
public static class Iban
{
    /// <summary>The IBAN without spaces, upper case.</summary>
    public static string Compact(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (char c in value)
            if (!char.IsWhiteSpace(c)) sb.Append(char.ToUpperInvariant(c));
        return sb.ToString();
    }

    /// <summary>True when <paramref name="value"/> is a structurally valid IBAN with a correct check sum.</summary>
    public static bool IsValid(string? value)
    {
        if (value is null) return false;
        string iban = Compact(value);
        if (iban.Length < 15 || iban.Length > 34) return false;
        if (!char.IsLetter(iban[0]) || !char.IsLetter(iban[1]) || !char.IsDigit(iban[2]) || !char.IsDigit(iban[3]))
            return false;

        int remainder = 0;
        string rearranged = iban.Substring(4) + iban.Substring(0, 4);
        foreach (char c in rearranged)
        {
            int digit;
            if (c >= '0' && c <= '9') digit = c - '0';
            else if (c >= 'A' && c <= 'Z') digit = c - 'A' + 10;
            else return false;

            remainder = digit >= 10 ? (remainder * 100 + digit) % 97 : (remainder * 10 + digit) % 97;
        }
        return remainder == 1;
    }
}
