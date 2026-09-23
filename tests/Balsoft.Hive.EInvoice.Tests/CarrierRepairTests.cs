using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Balsoft.Hive.EInvoice.Pdf;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace Balsoft.Hive.EInvoice.Tests;

/// <summary>
/// Carriers shaped like the AX 2009 report writer's output (found on a live installation):
/// a bold CIDFontType2 named plain "Arial" without its font program and without CIDToGIDMap,
/// and a text string that ends in glyph 0 for a line break. veraPDF must accept the hybrid.
/// </summary>
public class CarrierRepairTests
{
    private static readonly string Out = Path.Combine(AppContext.BaseDirectory, "samples", "pdf");

    /// <summary>Takes a PDFsharp Arial Bold carrier and makes it look like AX 2009 wrote it.</summary>
    private static byte[] Ax2009Like(bool trailingNotdef)
    {
        using var doc = PdfReader.Open(new MemoryStream(Carriers.Text(unicode: true)), PdfDocumentOpenMode.Modify);
        foreach (var page in doc.Pages)
        {
            var fonts = page.Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/Font")!;
            foreach (var key in fonts.Elements.Keys)
            {
                var type0 = fonts.Elements.GetDictionary(key)!;
                var cid = (PdfDictionary)((PdfReference)type0.Elements.GetArray("/DescendantFonts")!.Elements[0]).Value;
                var descriptor = cid.Elements.GetDictionary("/FontDescriptor")!;
                bool bold = type0.Elements.GetName("/BaseFont").Contains("Bold", StringComparison.Ordinal);
                type0.Elements["/BaseFont"] = new PdfName("/Arial");
                cid.Elements["/BaseFont"] = new PdfName("/Arial");
                cid.Elements.Remove("/CIDToGIDMap");
                descriptor.Elements["/FontName"] = new PdfName("/Arial");
                descriptor.Elements["/Flags"] = new PdfInteger(bold ? 262176 : 32);
                descriptor.Elements.Remove("/FontFile2");
            }
            if (trailingNotdef)
            {
                var content = (PdfDictionary)((PdfReference)(page.Elements["/Contents"] is PdfArray a ? a.Elements[0] : page.Elements["/Contents"])!).Value;
                string text = Encoding.Latin1.GetString(content.Stream.UnfilteredValue);
                var hex = Regex.Match(text, @"<([0-9A-Fa-f]+)>\s*Tj");
                Assert.True(hex.Success, "PDFsharp no longer writes hexadecimal Tj strings; adapt the test.");
                text = text.Substring(0, hex.Groups[1].Index + hex.Groups[1].Length) + "0000" + text.Substring(hex.Groups[1].Index + hex.Groups[1].Length);
                content.Stream.Value = PdfSharp.Pdf.Filters.Filtering.FlateDecode.Encode(Encoding.Latin1.GetBytes(text));
                content.Elements["/Filter"] = new PdfName("/FlateDecode");
            }
        }
        using var ms = new MemoryStream();
        doc.Save(ms, false);
        return ms.ToArray();
    }

    [Fact]
    public void BoldFontWithAPlainNameGetsTheBoldProgram()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) { Assert.Skip("Uses Windows fonts."); return; }
        var log = new List<string>();
        byte[] pdf = HybridPdf.Create(Ax2009Like(trailingNotdef: false), Samples.Standard(), InvoiceProfile.XRechnung, new HybridPdfOptions { Log = log.Add });
        Assert.Contains(log, l => l.StartsWith("Embedded font 'Arial' bold", StringComparison.Ordinal));
        Assert.Contains(log, l => l.StartsWith("Embedded font 'Arial' from", StringComparison.Ordinal));
        AssertPdfA(pdf, "ax2009-bold");
    }

    [Fact]
    public void TrailingNotdefIsRemoved()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) { Assert.Skip("Uses Windows fonts."); return; }
        var log = new List<string>();
        byte[] pdf = HybridPdf.Create(Ax2009Like(trailingNotdef: true), Samples.Standard(), InvoiceProfile.XRechnung, new HybridPdfOptions { Log = log.Add });
        Assert.Contains(log, l => l.StartsWith("Removed 1 trailing .notdef", StringComparison.Ordinal));
        AssertPdfA(pdf, "ax2009-notdef");

        Assert.False(HasTrailingNotdef(pdf));

        // Switched off, the page content keeps the trailing glyph 0.
        byte[] untouched = HybridPdf.Create(Ax2009Like(trailingNotdef: true), Samples.Standard(), InvoiceProfile.XRechnung,
            new HybridPdfOptions { RemoveTrailingNotdef = false });
        Assert.True(HasTrailingNotdef(untouched));
    }

    private static bool HasTrailingNotdef(byte[] pdf)
    {
        using var doc = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        var contents = doc.Pages[0].Elements["/Contents"];
        var first = (PdfDictionary)((PdfReference)(contents is PdfArray a ? a.Elements[0] : contents)!).Value;
        string text = Encoding.Latin1.GetString(first.Stream.UnfilteredValue);
        return Regex.IsMatch(text, @"<(?:[0-9A-Fa-f]{4})*0000>\s*Tj");
    }

    private static string Save(string name, byte[] pdf)
    {
        Directory.CreateDirectory(Out);
        string file = Path.Combine(Out, name + ".pdf");
        File.WriteAllBytes(file, pdf);
        return file;
    }

    private static void AssertPdfA(byte[] pdf, string name)
    {
        string file = Save(name, pdf);
        if (ExternalValidators.Directory is not { } tools) { Assert.Skip("HIVE_VALIDATORS is not set."); return; }
        var outcome = ExternalValidators.Mustang(tools, file);
        Assert.True(outcome.Accepted, outcome.ToString());
    }
}
