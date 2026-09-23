using System.Runtime.InteropServices;
using Hive.EInvoice.Cii;
using Hive.EInvoice.Pdf;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Hive.EInvoice.Tests;

/// <summary>Carrier PDFs as an ERP would print them, generated for the tests.</summary>
public static class Carriers
{
    /// <summary>Vector graphics only: no fonts involved, runs everywhere.</summary>
    public static byte[] Vector()
    {
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        using (var g = XGraphics.FromPdfPage(page))
        {
            g.DrawRectangle(new XSolidBrush(XColor.FromArgb(252, 182, 8)), 40, 40, 120, 30);
            g.DrawLine(XPens.Black, 40, 100, 550, 100);
            for (int i = 0; i < 5; i++) g.DrawRectangle(XPens.Gray, 40, 120 + i * 24, 510, 20);
        }
        using var ms = new MemoryStream();
        doc.Save(ms, false);
        return ms.ToArray();
    }

    /// <summary>Text in Arial as PDFsharp writes it (font embedded). Windows only.</summary>
    public static byte[] Text(bool stripFontProgram = false)
    {
        GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        using (var g = XGraphics.FromPdfPage(page))
        {
            var font = new XFont("Arial", 11);
            g.DrawString("Balsoft GmbH · Rechnung RE-2026-0001", new XFont("Arial", 16, XFontStyleEx.Bold), XBrushes.Black, 40, 60);
            g.DrawString("Beratung D365FO   10 h × 120,00 €   1.200,00 €", font, XBrushes.Black, 40, 100);
            g.DrawString("Summe brutto 2.616,81 €", font, XBrushes.Black, 40, 130);
        }
        using var ms = new MemoryStream();
        doc.Save(ms, false);
        byte[] bytes = ms.ToArray();
        return stripFontProgram ? StripFontPrograms(bytes) : bytes;
    }

    /// <summary>Removes /FontFile2 from every font descriptor: a carrier that references Arial without embedding it.</summary>
    private static byte[] StripFontPrograms(byte[] pdf)
    {
        using var doc = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Modify);
        foreach (var page in doc.Pages)
        {
            var fonts = page.Elements.GetDictionary("/Resources")?.Elements.GetDictionary("/Font");
            if (fonts is null) continue;
            foreach (var key in fonts.Elements.Keys)
            {
                var font = fonts.Elements.GetDictionary(key)!;
                var target = font.Elements.GetArray("/DescendantFonts") is { } kids ? (PdfDictionary)((PdfSharp.Pdf.Advanced.PdfReference)kids.Elements[0]).Value : font;
                target.Elements.GetDictionary("/FontDescriptor")?.Elements.Remove("/FontFile2");
            }
        }
        using var ms = new MemoryStream();
        doc.Save(ms, false);
        return ms.ToArray();
    }
}

public class HybridPdfTests
{
    private static readonly string Out = Path.Combine(AppContext.BaseDirectory, "samples", "pdf");

    public static TheoryData<InvoiceProfile> Profiles() => new()
    {
        InvoiceProfile.XRechnung, InvoiceProfile.EN16931, InvoiceProfile.FacturXExtended,
        InvoiceProfile.FacturXBasic, InvoiceProfile.FacturXBasicWL, InvoiceProfile.FacturXMinimum,
    };

    private static string Save(string name, byte[] pdf)
    {
        Directory.CreateDirectory(Out);
        string file = Path.Combine(Out, name + ".pdf");
        File.WriteAllBytes(file, pdf);
        return file;
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void HybridIsValidPdfA3AndCarriesTheXml(InvoiceProfile profile)
    {
        var invoice = Samples.SkontoAktivbank();
        byte[] xml = CiiWriter.Write(invoice, profile);
        byte[] pdf = HybridPdf.Create(Carriers.Vector(), xml, profile);

        Assert.Equal(xml, HybridPdf.ExtractXml(pdf));

        // The incremental update must be what readers see: the Factur-X XMP, not PDFsharp's.
        using (var doc = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import))
        {
            var metadata = (PdfDictionary)doc.Internals.Catalog.Elements.GetReference("/Metadata")!.Value;
            string xmp = System.Text.Encoding.UTF8.GetString(metadata.Stream.UnfilteredValue);
            Assert.Contains($"<fx:ConformanceLevel>{profile.FacturXConformanceLevel()}</fx:ConformanceLevel>", xmp);
            Assert.Contains($"<fx:DocumentFileName>{profile.EmbeddedFileName()}</fx:DocumentFileName>", xmp);
            Assert.Contains("<pdfaid:part>3</pdfaid:part>", xmp);
        }

        string file = Save($"vector-{profile}", pdf);
        if (ExternalValidators.Directory is not { } tools) { Assert.Skip("HIVE_VALIDATORS is not set."); return; }
        var outcome = ExternalValidators.Mustang(tools, file);
        Assert.True(outcome.Accepted, outcome.ToString());
    }

    [Fact]
    public void TextCarrierWithEmbeddedFontsIsValid()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) { Assert.Skip("Uses Windows fonts."); return; }
        byte[] pdf = HybridPdf.Create(Carriers.Text(), Samples.Standard(), InvoiceProfile.XRechnung);
        string file = Save("text-embedded", pdf);
        if (ExternalValidators.Directory is not { } tools) { Assert.Skip("HIVE_VALIDATORS is not set."); return; }
        var outcome = ExternalValidators.Mustang(tools, file);
        Assert.True(outcome.Accepted, outcome.ToString());
    }

    [Fact]
    public void MissingTrueTypeFontIsEmbeddedFromTheSystem()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) { Assert.Skip("Uses Windows fonts."); return; }
        var log = new List<string>();
        byte[] pdf = HybridPdf.Create(Carriers.Text(stripFontProgram: true), Samples.Standard(), InvoiceProfile.XRechnung,
            new HybridPdfOptions { Log = log.Add });
        Assert.Contains(log, l => l.StartsWith("Embedded font", StringComparison.Ordinal));
        string file = Save("text-font-missing", pdf);
        if (ExternalValidators.Directory is not { } tools) { Assert.Skip("HIVE_VALIDATORS is not set."); return; }
        var outcome = ExternalValidators.Mustang(tools, file);
        Assert.True(outcome.Accepted, outcome.ToString());
    }

    [Fact]
    public void ExtractXmlReturnsNullForAPlainPdf() => Assert.Null(HybridPdf.ExtractXml(Carriers.Vector()));

    [Fact]
    public void AttachmentNameFollowsTheProfile()
    {
        Assert.Equal("xrechnung.xml", InvoiceProfile.XRechnung.EmbeddedFileName());
        Assert.Equal("factur-x.xml", InvoiceProfile.EN16931.EmbeddedFileName());
    }
}
