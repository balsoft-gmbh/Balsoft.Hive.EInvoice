using System.Text;
using Balsoft.Hive.EInvoice.Pdf;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace Balsoft.Hive.EInvoice.Tests;

/// <summary>A TrueType font the tests can read on Windows, macOS and the Linux CI runner.</summary>
internal static class TestFonts
{
    public static (string File, string PostScript)? Find()
    {
        string windows = Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows";
        string[] candidates =
        {
            Path.Combine(windows, "Fonts", "arial.ttf"),
            "/System/Library/Fonts/Supplemental/Arial.ttf",
            "/Library/Fonts/Arial.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "/usr/share/fonts/dejavu/DejaVuSans.ttf",
            "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
        };
        foreach (string file in candidates)
            if (File.Exists(file) && FontIndex.TryReadNames(file, out var ps, out _, out _, out _) && ps is not null)
                return (file, ps);
        return null;
    }

    public static (string File, string PostScript, TrueTypeFont Font) Load()
    {
        var found = Find();
        if (found is null) Assert.Skip("No TrueType test font on this machine (Arial, DejaVu Sans or Liberation Sans).");
        Assert.True(TrueTypeFont.TryParse(File.ReadAllBytes(found.Value.File), out var font));
        return (found.Value.File, found.Value.PostScript, font);
    }

    public static int Gid(TrueTypeFont font, char c)
        => font.Cmaps().First(m => m.Platform == 3 && m.Encoding == 1).Map[c];

    public static IEnumerable<int> Gids(TrueTypeFont font, string text) => text.Select(c => Gid(font, c));
}

/// <summary>Writes small PDFs object by object, so a test controls every key a font or stream has.</summary>
internal static class RawPdf
{
    public static byte[] Build(params (string Dict, byte[]? Stream)[] objects)
    {
        using var ms = new MemoryStream();
        void W(string s) { var b = Encoding.Latin1.GetBytes(s); ms.Write(b, 0, b.Length); }
        W("%PDF-1.7\n%\u00E2\u00E3\u00CF\u00D3\n");
        var offsets = new long[objects.Length];
        for (int i = 0; i < objects.Length; i++)
        {
            offsets[i] = ms.Position;
            var (dict, stream) = objects[i];
            W($"{i + 1} 0 obj\n");
            if (stream is null) W(dict + "\nendobj\n");
            else
            {
                int close = dict.LastIndexOf(">>", StringComparison.Ordinal);
                W(dict.Substring(0, close) + $" /Length {stream.Length} >>\nstream\n");
                ms.Write(stream, 0, stream.Length);
                W("\nendstream\nendobj\n");
            }
        }
        long xref = ms.Position;
        W($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (long o in offsets) W($"{o:D10} 00000 n \n");
        W($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }

    public static byte[] Latin1(string s) => Encoding.Latin1.GetBytes(s);
}

public class FontSubsetterTests
{
    private const string PageText = "Rechnung RE-2026-0001";
    private const string FormText = "Summe 99";
    private const string AnnotationText = "Kxyz";

    /// <summary>
    /// One page with a two-byte Identity-H CID font as the AX 2009 report writer writes it, text
    /// on the page, in a form XObject and in a FreeText annotation appearance.
    /// </summary>
    private static byte[] CidCarrier(TrueTypeFont font, string file, string ps, bool embedFull = false, bool formDefaults = false,
        string encoding = "/Identity-H", string fontName = "", string contentFilter = "")
    {
        string Hex(string text) => string.Concat(TestFonts.Gids(font, text).Select(g => g.ToString("X4")));
        string name = fontName.Length > 0 ? fontName : ps;
        byte[] program = File.ReadAllBytes(file);
        return RawPdf.Build(
            ($"<< /Type /Catalog /Pages 2 0 R{(formDefaults ? " /AcroForm 12 0 R" : "")} >>", null),
            ("<< /Type /Pages /Kids [3 0 R] /Count 1 >>", null),
            ("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 5 0 R >> /XObject << /X1 8 0 R >> >> /Contents 4 0 R /Annots [10 0 R] >>", null),
            ($"<< {contentFilter} >>", RawPdf.Latin1($"BT /F1 12 Tf 40 780 Td <{Hex(PageText)}> Tj ET q 1 0 0 1 0 0 cm /X1 Do Q")),
            ($"<< /Type /Font /Subtype /Type0 /BaseFont /{name} /Encoding {encoding} /DescendantFonts [6 0 R] >>", null),
            ($"<< /Type /Font /Subtype /CIDFontType2 /BaseFont /{name} /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> /FontDescriptor 7 0 R /DW 600 >>", null),
            ($"<< /Type /FontDescriptor /FontName /{name} /Flags 32 /FontBBox [-600 -300 2000 1000] /ItalicAngle 0 /Ascent 900 /Descent -200 /CapHeight 700 /StemV 80{(embedFull ? " /FontFile2 9 0 R" : "")} >>", null),
            ("<< /Type /XObject /Subtype /Form /BBox [0 0 595 842] /Resources << /Font << /F1 5 0 R >> >> >>", RawPdf.Latin1($"BT /F1 10 Tf 40 600 Td <{Hex(FormText)}> Tj ET")),
            embedFull ? ($"<< /Length1 {program.Length} >>", program) : ("null", null),
            ("<< /Type /Annot /Subtype /FreeText /Rect [40 400 300 430] /Contents (note) /DA (/F1 10 Tf 0 g) /AP << /N 11 0 R >> >>", null),
            ("<< /Type /XObject /Subtype /Form /BBox [0 0 260 30] /Resources << /Font << /F1 5 0 R >> >> >>", RawPdf.Latin1($"BT /F1 10 Tf 2 10 Td <{Hex(AnnotationText)}> Tj ET")),
            ("<< /Fields [] /DR << /Font << /F1 5 0 R >> >> /DA (/F1 0 Tf 0 g) >>", null));
    }

    private static TrueTypeFont EmbeddedProgram(PdfDocument doc)
    {
        var type0 = doc.Pages[0].Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/Font")!.Elements.GetDictionary("/F1")!;
        var cid = (PdfDictionary)((PdfReference)type0.Elements.GetArray("/DescendantFonts")!.Elements[0]).Value;
        var file = (PdfDictionary)cid.Elements.GetDictionary("/FontDescriptor")!.Elements.GetReference("/FontFile2")!.Value;
        Assert.True(TrueTypeFont.TryParse(file.Stream.UnfilteredValue, out var program), "The embedded program does not parse.");
        Assert.Equal(file.Stream.UnfilteredValue.Length, file.Elements.GetInteger("/Length1"));
        return program;
    }

    private static void AssertGlyphsKept(TrueTypeFont original, TrueTypeFont subset, string text)
    {
        foreach (int g in TestFonts.Gids(original, text).Distinct()) AssertSameGlyph(original.Glyph(g), subset.Glyph(g));
    }

    /// <summary>Same outline bytes; the subset may only add zero padding to the next 4-byte boundary.</summary>
    private static void AssertSameGlyph(byte[] expected, byte[] actual)
    {
        Assert.True(actual.Length >= expected.Length && actual.Length - expected.Length < 4, $"{expected.Length} vs {actual.Length} bytes");
        Assert.Equal(expected, actual.Take(expected.Length).ToArray());
        Assert.All(actual.Skip(expected.Length), b => Assert.Equal(0, b));
    }

    private static string FontNames(PdfDocument doc)
    {
        var type0 = doc.Pages[0].Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/Font")!.Elements.GetDictionary("/F1")!;
        var cid = (PdfDictionary)((PdfReference)type0.Elements.GetArray("/DescendantFonts")!.Elements[0]).Value;
        return string.Join(" ", type0.Elements.GetName("/BaseFont"), cid.Elements.GetName("/BaseFont"),
            cid.Elements.GetDictionary("/FontDescriptor")!.Elements.GetName("/FontName"));
    }

    [Fact]
    public void SubsetKeepsUsedGlyphsByteForByteAndEmptiesTheRest()
    {
        var (file, _, font) = TestFonts.Load();
        byte[] original = File.ReadAllBytes(file);
        var used = TestFonts.Gids(font, PageText).ToHashSet();

        byte[] subset = FontSubsetter.Subset(original, used)!;

        Assert.NotNull(subset);
        Assert.True(subset.Length < original.Length / 3, $"{subset.Length:N0} of {original.Length:N0} bytes");
        Assert.True(TrueTypeFont.TryParse(subset, out var reduced));
        Assert.Equal(font.GlyphCount, reduced.GlyphCount);
        AssertGlyphsKept(font, reduced, PageText);
        AssertSameGlyph(font.Glyph(0), reduced.Glyph(0));
        int unused = TestFonts.Gid(font, 'Z');
        Assert.DoesNotContain(unused, used);
        Assert.NotEmpty(font.Glyph(unused));
        Assert.Empty(reduced.Glyph(unused));
        Assert.Equal(0xB1B0AFBAu, WholeFileChecksum(subset));
    }

    [Fact]
    public void CompositeGlyphKeepsItsComponents()
    {
        var (file, _, font) = TestFonts.Load();
        int composite = "ÄÖÜäöüéèêÅåçñ".Select(c => TestFonts.Gid(font, c)).FirstOrDefault(g => font.Components(g).Any());
        if (composite == 0) Assert.Skip("The test font has no composite accented glyph.");
        var components = font.Components(composite).ToList();

        Assert.True(TrueTypeFont.TryParse(FontSubsetter.Subset(File.ReadAllBytes(file), new[] { composite })!, out var reduced));

        AssertSameGlyph(font.Glyph(composite), reduced.Glyph(composite));
        foreach (int c in components) AssertSameGlyph(font.Glyph(c), reduced.Glyph(c));
    }

    [Fact]
    public void NoTrueTypeProgramGivesNull()
    {
        Assert.Null(FontSubsetter.Subset(Encoding.ASCII.GetBytes("OTTO and some CFF data that is no TrueType font"), new[] { 1 }));
        Assert.Null(FontSubsetter.Subset(new byte[4], new[] { 1 }));
    }

    [Fact]
    public void HybridPdfEmbedsOnlyTheGlyphsTheCarrierShows()
    {
        var (file, ps, font) = TestFonts.Load();
        byte[] carrier = CidCarrier(font, file, ps);
        var log = new List<string>();
        HybridPdfOptions Options(bool subset)
        {
            var o = new HybridPdfOptions { SubsetFonts = subset, Log = log.Add, Timestamp = new DateTimeOffset(2026, 11, 23, 9, 0, 0, TimeSpan.FromHours(1)) };
            o.FontDirectories.Add(Path.GetDirectoryName(file)!);
            return o;
        }

        byte[] full = HybridPdf.Create(carrier, Samples.Standard(), InvoiceProfile.XRechnung, Options(subset: false));
        byte[] subset = HybridPdf.Create(carrier, Samples.Standard(), InvoiceProfile.XRechnung, Options(subset: true));

        Assert.True(subset.Length < full.Length / 3, $"subset {subset.Length:N0}, full {full.Length:N0} bytes");
        Assert.Contains(log, l => l.StartsWith($"Embedded font '{ps}' from", StringComparison.Ordinal) && l.Contains("(subset: ", StringComparison.Ordinal));
        Assert.Equal(HybridPdf.ExtractXml(full), HybridPdf.ExtractXml(subset));

        using var doc = PdfReader.Open(new MemoryStream(subset), PdfDocumentOpenMode.Import);
        var program = EmbeddedProgram(doc);
        AssertGlyphsKept(font, program, PageText + FormText + AnnotationText);
        Assert.Matches($"^/[A-Z]{{6}}\\+{ps} /[A-Z]{{6}}\\+{ps} /[A-Z]{{6}}\\+{ps}$", FontNames(doc));
    }

    [Fact]
    public void SubsetEmbeddedFontsReplacesAFullProgram()
    {
        var (file, ps, font) = TestFonts.Load();
        using var doc = PdfReader.Open(new MemoryStream(CidCarrier(font, file, ps, embedFull: true)), PdfDocumentOpenMode.Modify);

        var log = new List<string>();
        Assert.Equal(1, FontSubsetter.SubsetEmbeddedFonts(doc, log.Add));

        using var saved = new MemoryStream();
        doc.Save(saved, false);
        using var reread = PdfReader.Open(new MemoryStream(saved.ToArray()), PdfDocumentOpenMode.Import);
        var program = EmbeddedProgram(reread);
        AssertGlyphsKept(font, program, PageText + FormText + AnnotationText);
        Assert.Empty(program.Glyph(TestFonts.Gid(font, 'Q')));
        Assert.Matches($"^/[A-Z]{{6}}\\+{ps}", FontNames(reread));
        Assert.Single(log);
        Assert.True(saved.Length < File.ReadAllBytes(file).Length / 2, $"{saved.Length:N0} bytes");
    }

    [Fact]
    public void FontInFormFieldDefaultsStaysComplete()
    {
        var (file, ps, font) = TestFonts.Load();
        using var doc = PdfReader.Open(new MemoryStream(CidCarrier(font, file, ps, embedFull: true, formDefaults: true)), PdfDocumentOpenMode.Modify);
        Assert.Equal(0, FontSubsetter.SubsetEmbeddedFonts(doc));
    }

    [Fact]
    public void UnreadableContentStreamKeepsTheFont()
    {
        var (file, ps, font) = TestFonts.Load();
        using var doc = PdfReader.Open(new MemoryStream(CidCarrier(font, file, ps, embedFull: true, contentFilter: "/Filter /JBIG2Decode")), PdfDocumentOpenMode.Modify);
        Assert.Equal(0, FontSubsetter.SubsetEmbeddedFonts(doc));
    }

    [Fact]
    public void AlreadySubsetOrNonIdentityFontsAreLeftAlone()
    {
        var (file, ps, font) = TestFonts.Load();
        using (var tagged = PdfReader.Open(new MemoryStream(CidCarrier(font, file, ps, embedFull: true, fontName: "ABCDEF+" + ps)), PdfDocumentOpenMode.Modify))
            Assert.Equal(0, FontSubsetter.SubsetEmbeddedFonts(tagged));
        using (var cmap = PdfReader.Open(new MemoryStream(CidCarrier(font, file, ps, embedFull: true, encoding: "/UniGB-UCS2-H")), PdfDocumentOpenMode.Modify))
            Assert.Equal(0, FontSubsetter.SubsetEmbeddedFonts(cmap));
    }

    [Fact]
    public void SimpleWinAnsiFontKeepsEveryLetterShown()
    {
        var (file, ps, font) = TestFonts.Load();
        byte[] program = File.ReadAllBytes(file);
        // "Grüße 5 €" in WinAnsiEncoding: ü 0xFC, ß 0xDF, € 0x80, written with octal escapes.
        string widths = string.Join(" ", Enumerable.Repeat("600", 224));
        byte[] pdf = RawPdf.Build(
            ("<< /Type /Catalog /Pages 2 0 R >>", null),
            ("<< /Type /Pages /Kids [3 0 R] /Count 1 >>", null),
            ("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>", null),
            ("<< >>", RawPdf.Latin1("BT /F1 12 Tf 40 780 Td (Gr\\374\\337e 5 \\200) Tj ET")),
            ($"<< /Type /Font /Subtype /TrueType /BaseFont /{ps} /FirstChar 32 /LastChar 255 /Widths [{widths}] /Encoding /WinAnsiEncoding /FontDescriptor 6 0 R >>", null),
            ($"<< /Type /FontDescriptor /FontName /{ps} /Flags 32 /FontBBox [-600 -300 2000 1000] /ItalicAngle 0 /Ascent 900 /Descent -200 /CapHeight 700 /StemV 80 /FontFile2 7 0 R >>", null),
            ($"<< /Length1 {program.Length} >>", program));
        using var doc = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Modify);

        Assert.Equal(1, FontSubsetter.SubsetEmbeddedFonts(doc));

        var descriptor = doc.Pages[0].Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/Font")!.Elements.GetDictionary("/F1")!
            .Elements.GetDictionary("/FontDescriptor")!;
        var stream = (PdfDictionary)descriptor.Elements.GetReference("/FontFile2")!.Value;
        Assert.True(TrueTypeFont.TryParse(stream.Stream.UnfilteredValue, out var reduced));
        AssertGlyphsKept(font, reduced, "Grüße 5 €");
        Assert.Empty(reduced.Glyph(TestFonts.Gid(font, 'Q')));
    }

    [Fact]
    public void ContentLexerKeepsStringBytesAndSkipsInlineImages()
    {
        byte[] content = RawPdf.Latin1(
            "% comment (not a string\n/P <</MCID 0 /T (x)>> BDC (a\\(b\\)\\101\\n) Tj <4142 4> Tj [(x) -20 <0041>] TJ " +
            "BI /W 2 /H 1 /BPC 8 /CS /G ID \u0000EI\u0001 EI 1 0 0 1 5 5 cm (c\\\r\nd) Tj EMC");
        var seen = new List<(string, List<object?>)>();

        ContentLexer.Scan(content, (op, operands) => seen.Add((op, operands.ToList())));

        Assert.Equal(new[] { "BDC", "Tj", "Tj", "TJ", "cm", "Tj", "EMC" }, seen.Select(s => s.Item1));
        Assert.Equal(RawPdf.Latin1("a(b)A\n"), seen[1].Item2[0]);
        Assert.Equal(new byte[] { 0x41, 0x42, 0x40 }, seen[2].Item2[0]);
        var parts = (List<object>)seen[3].Item2[0]!;
        Assert.Equal(new object[] { RawPdf.Latin1("x"), -20.0, new byte[] { 0x00, 0x41 } }, parts);
        Assert.Equal(RawPdf.Latin1("cd"), seen[5].Item2[0]);
        Assert.Throws<FormatException>(() => ContentLexer.Scan(RawPdf.Latin1("(open"), (_, _) => { }));
        Assert.Throws<FormatException>(() => ContentLexer.Scan(RawPdf.Latin1("a ) b"), (_, _) => { }));
    }

    /// <summary>A TrueType program with one empty glyph and the given cmap subtable, written table by table.</summary>
    private static byte[] TinyFont(byte[] cmapSubtable)
    {
        byte[] Be(params int[] words) => words.SelectMany(w => new[] { (byte)(w >> 8), (byte)w }).ToArray();
        var head = new byte[54]; head[51] = 0;                       // short loca
        var hhea = new byte[36]; hhea[35] = 1;                        // one long metric
        var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["cmap"] = Be(0, 1, 3, 10, 0, 12).Concat(cmapSubtable).ToArray(),
            ["glyf"] = Array.Empty<byte>(),
            ["head"] = head,
            ["hhea"] = hhea,
            ["hmtx"] = new byte[4],
            ["loca"] = new byte[4],
            ["maxp"] = Be(0, 0x5000, 1),
        };
        using var ms = new MemoryStream();
        ms.Write(Be(1, 0, tables.Count, 0, 0, 0));
        int offset = 12 + 16 * tables.Count;
        foreach (var (tag, data) in tables)
        {
            ms.Write(Encoding.ASCII.GetBytes(tag));
            ms.Write(Be(0, 0, offset >> 16, offset & 0xFFFF, data.Length >> 16, data.Length & 0xFFFF));
            offset += (data.Length + 3) & ~3;
        }
        foreach (var data in tables.Values) { ms.Write(data); ms.Write(new byte[((data.Length + 3) & ~3) - data.Length]); }
        return ms.ToArray();
    }

    [Fact]
    public void ACmapWithHugeRangesIsNotWalked()
    {
        // Format 12 with three groups that each claim every code point: about 3.3 million steps, refused.
        var groups = Enumerable.Repeat(new[] { 0, 0, 0x10, 0xFFFF, 0, 1 }, 3).SelectMany(g => g).ToArray();
        byte[] Be(params int[] words) => words.SelectMany(w => new[] { (byte)(w >> 8), (byte)w }).ToArray();
        byte[] subtable = Be(12, 0, 0, 16 + 12 * 3, 0, 0, 0, 3).Concat(Be(groups)).ToArray();
        Assert.True(TrueTypeFont.TryParse(TinyFont(subtable), out var font));

        var watch = System.Diagnostics.Stopwatch.StartNew();
        Assert.Empty(font.Cmaps());
        Assert.True(watch.ElapsedMilliseconds < 2000, $"{watch.ElapsedMilliseconds} ms");

        // One honest group is read.
        byte[] honest = Be(12, 0, 0, 16 + 12, 0, 0, 0, 1).Concat(Be(0, 0x41, 0, 0x43, 0, 1)).ToArray();
        Assert.True(TrueTypeFont.TryParse(TinyFont(honest), out var small));
        Assert.Equal(new[] { 0x41, 0x42, 0x43 }, small.Cmaps().Single().Map.Keys.OrderBy(k => k));
    }

    [Fact]
    public void SubsetTagIsStableAndWellFormed()
    {
        string a = FontSubsetter.Tag(new[] { 3, 1, 2 }, "Arial"), b = FontSubsetter.Tag(new[] { 1, 2, 3 }, "Arial");
        Assert.Equal(a, b);
        Assert.Matches("^[A-Z]{6}$", a);
        Assert.NotEqual(a, FontSubsetter.Tag(new[] { 1, 2, 4 }, "Arial"));
        Assert.Equal("/" + a + "+Arial", FontSubsetter.WithTag("/QWERTY+Arial", a));
        Assert.Equal(258, MacGlyphNames.Count);
        Assert.Equal("asciitilde", MacGlyphNames.Get(97));
        Assert.Equal("dcroat", MacGlyphNames.Get(257));
    }

    private static uint WholeFileChecksum(byte[] font)
    {
        uint sum = 0;
        for (int i = 0; i < font.Length; i += 4)
        {
            uint word = 0;
            for (int b = 0; b < 4; b++) word = (word << 8) | (i + b < font.Length ? font[i + b] : 0u);
            sum = unchecked(sum + word);
        }
        return sum;
    }
}
