using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Hive.EInvoice.Pdf;

/// <summary>
/// Appends a PDF incremental update (ISO 32000-1, 7.5.6) that redefines one stream object.
/// The original bytes stay untouched; a new body section, a cross-reference section with a
/// /Prev link to the previous one and a new trailer follow them. PDF/A permits incremental
/// updates.
/// </summary>
internal static class IncrementalUpdate
{
    private static readonly Encoding Ascii = Encoding.ASCII;

    public static byte[] ReplaceStream(byte[] pdf, int objectNumber, int generation, string dictionaryEntries, byte[] content)
    {
        string tail = Ascii.GetString(pdf, Math.Max(0, pdf.Length - 4096), Math.Min(4096, pdf.Length));
        var startXref = Regex.Match(tail, @"startxref\s+(\d+)\s+%%EOF\s*$");
        if (!startXref.Success) throw new InvalidOperationException("The PDF has no final startxref.");
        long previousXref = long.Parse(startXref.Groups[1].Value, CultureInfo.InvariantCulture);

        // The last trailer dictionary: Size, Root, Info and ID carry over into the new trailer.
        int trailerAt = tail.LastIndexOf("trailer", StringComparison.Ordinal);
        if (trailerAt < 0) throw new InvalidOperationException("Only PDFs with a classic cross-reference table are supported.");
        string trailer = tail.Substring(trailerAt);
        string Entry(string key, string pattern)
        {
            var m = Regex.Match(trailer, "/" + key + @"\s*" + pattern);
            return m.Success ? m.Value : "";
        }
        string size = Entry("Size", @"\d+");
        string root = Entry("Root", @"\d+\s+\d+\s+R");
        string info = Entry("Info", @"\d+\s+\d+\s+R");
        string id = Entry("ID", @"\[[^\]]*\]");
        if (size.Length == 0 || root.Length == 0) throw new InvalidOperationException("The trailer lacks /Size or /Root.");

        using var ms = new MemoryStream(pdf.Length + content.Length + 512);
        ms.Write(pdf, 0, pdf.Length);
        if (pdf[pdf.Length - 1] != (byte)'\n') ms.WriteByte((byte)'\n');

        long objectOffset = ms.Position;
        Write(ms, $"{objectNumber} {generation} obj\n<<{dictionaryEntries}/Length {content.Length}>>\nstream\n");
        ms.Write(content, 0, content.Length);
        Write(ms, "\nendstream\nendobj\n");

        long xrefOffset = ms.Position;
        Write(ms, "xref\n");
        Write(ms, "0 1\n0000000000 65535 f\r\n");
        Write(ms, $"{objectNumber} 1\n{objectOffset:D10} {generation:D5} n\r\n");
        Write(ms, $"trailer\n<<{size}{root}{info}{id}/Prev {previousXref}>>\nstartxref\n{xrefOffset}\n%%EOF\n");
        return ms.ToArray();
    }

    private static void Write(Stream s, FormattableString text)
    {
        var bytes = Ascii.GetBytes(text.ToString(CultureInfo.InvariantCulture));
        s.Write(bytes, 0, bytes.Length);
    }

    private static void Write(Stream s, string text)
    {
        var bytes = Ascii.GetBytes(text);
        s.Write(bytes, 0, bytes.Length);
    }
}
