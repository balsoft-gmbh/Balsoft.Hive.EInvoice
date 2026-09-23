using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;

namespace Balsoft.Hive.EInvoice.Pdf;

/// <summary>
/// PDF/A (ISO 19005-3, 6.2.11.8) forbids text showing operators from referencing the .notdef
/// glyph. Some ERP print engines (the AX 2009 report writer among them) encode a trailing
/// line break they have no glyph for as glyph 0 at the end of a text string. A glyph at the
/// very end of a string only advances the text position, so removing it changes nothing that
/// is painted. This removes exactly that case: trailing code 0000 in hexadecimal strings shown
/// with a two-byte Identity-H or Identity-V font. Any other .notdef reference is left alone.
/// </summary>
internal static class NotdefCleaner
{
    private static readonly Regex Token = new(@"/([^\s/\[\]()<>{}%]+)\s+[-+]?(?:\d+\.?\d*|\.\d+)\s+Tf|(?<!<)<([0-9A-Fa-f\s]+)>(?!>)",
        RegexOptions.CultureInvariant);

    private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

    public static int RemoveTrailingNotdef(PdfDocument doc, Action<string>? log)
    {
        int removed = 0;
        foreach (var page in doc.Pages)
        {
            var twoByteFonts = TwoByteFonts(page.Elements.GetDictionary("/Resources")?.Elements.GetDictionary("/Font"));
            if (twoByteFonts.Count == 0) continue;
            foreach (var stream in ContentStreams(page))
            {
                if (stream.Stream is null) continue;
                string content = Latin1.GetString(stream.Stream.UnfilteredValue);
                int count = 0;
                string? currentFont = null;
                string cleaned = Token.Replace(content, m =>
                {
                    if (m.Groups[1].Success)
                    {
                        currentFont = "/" + m.Groups[1].Value;
                        return m.Value;
                    }
                    if (currentFont is null || !twoByteFonts.Contains(currentFont)) return m.Value;
                    string hex = Regex.Replace(m.Groups[2].Value, @"\s", "");
                    if (hex.Length % 4 != 0) return m.Value;
                    int end = hex.Length;
                    while (end >= 4 && hex.Substring(end - 4, 4) == "0000") end -= 4;
                    if (end == hex.Length || end == 0) return m.Value;
                    count += (hex.Length - end) / 4;
                    return "<" + hex.Substring(0, end) + ">";
                });
                if (count == 0) continue;
                stream.Stream.Value = PdfSharp.Pdf.Filters.Filtering.FlateDecode.Encode(Latin1.GetBytes(cleaned));
                stream.Elements["/Filter"] = new PdfName("/FlateDecode");
                stream.Elements.Remove("/DecodeParms");
                removed += count;
            }
        }
        if (removed > 0)
            log?.Invoke($"Removed {removed} trailing .notdef glyph reference(s) from text (PDF/A 6.2.11.8); nothing visible changes.");
        return removed;
    }

    private static HashSet<string> TwoByteFonts(PdfDictionary? fonts)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (fonts is null) return set;
        foreach (var key in fonts.Elements.Keys)
        {
            var font = fonts.Elements.GetDictionary(key);
            if (font?.Elements.GetName("/Subtype") != "/Type0") continue;
            string encoding = font.Elements.GetName("/Encoding");
            if (encoding is "/Identity-H" or "/Identity-V") set.Add(key);
        }
        return set;
    }

    private static IEnumerable<PdfDictionary> ContentStreams(PdfPage page)
    {
        var contents = page.Elements["/Contents"];
        if (contents is PdfReference r) contents = r.Value;
        if (contents is PdfDictionary single)
        {
            yield return single;
        }
        else if (contents is PdfArray array)
        {
            foreach (var item in array.Elements)
            {
                if ((item is PdfReference ir ? ir.Value : item) is PdfDictionary d) yield return d;
            }
        }
    }
}
