using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Hive.EInvoice.Cii;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace Hive.EInvoice.Pdf;

/// <summary>Options for <see cref="HybridPdf"/>.</summary>
public sealed class HybridPdfOptions
{
    /// <summary>Document title (Info /Title and XMP dc:title). Defaults to "Invoice {number}" or "Invoice".</summary>
    public string? Title { get; set; }

    /// <summary>Document author (Info /Author and XMP dc:creator). Defaults to the seller name.</summary>
    public string? Author { get; set; }

    /// <summary>Document subject (Info /Subject and XMP dc:description).</summary>
    public string? Subject { get; set; }

    /// <summary>Creating application (Info /Creator and XMP xmp:CreatorTool).</summary>
    public string Creator { get; set; } = "Hive.EInvoice";

    /// <summary>
    /// Embed TrueType fonts the carrier references but does not embed, taken from the system
    /// font folders (PDF/A requires every font to be embedded). Default true.
    /// </summary>
    public bool EmbedMissingFonts { get; set; } = true;

    /// <summary>Additional folders to search for fonts, before the system font folders.</summary>
    public IList<string> FontDirectories { get; } = new List<string>();

    /// <summary>Receives notes about the conversion (fonts embedded or not found).</summary>
    public Action<string>? Log { get; set; }

    /// <summary>Creation and modification date written to the document. Defaults to now.</summary>
    public DateTimeOffset? Timestamp { get; set; }
}

/// <summary>
/// Creates hybrid e-invoices: a PDF/A-3 whose pages come from the carrier PDF (the visual
/// invoice your ERP prints) with the XRechnung or ZUGFeRD / Factur-X XML embedded as an
/// associated file, plus the Factur-X XMP metadata, the sRGB output intent and the file
/// relationship receivers look for. Reads the XML back out of such a PDF.
/// </summary>
public static class HybridPdf
{
    /// <summary>Writes the invoice as CII and embeds it into <paramref name="carrierPdf"/>.</summary>
    public static byte[] Create(byte[] carrierPdf, Invoice invoice, InvoiceProfile profile, HybridPdfOptions? options = null)
    {
        options ??= new HybridPdfOptions();
        options.Title ??= string.IsNullOrWhiteSpace(invoice.Number) ? "Invoice" : $"Invoice {invoice.Number}";
        options.Author ??= string.IsNullOrWhiteSpace(invoice.Seller.Name) ? null : invoice.Seller.Name;
        return Create(carrierPdf, CiiWriter.Write(invoice, profile), profile, options);
    }

    /// <summary>File variant of <see cref="Create(byte[], Invoice, InvoiceProfile, HybridPdfOptions)"/>.</summary>
    public static void Create(string carrierPdfPath, Invoice invoice, InvoiceProfile profile, string outputPdfPath, HybridPdfOptions? options = null)
    {
        var bytes = Create(File.ReadAllBytes(carrierPdfPath), invoice, profile, options);
        string? dir = Path.GetDirectoryName(Path.GetFullPath(outputPdfPath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllBytes(outputPdfPath, bytes);
    }

    /// <summary>
    /// Embeds already written CII XML into <paramref name="carrierPdf"/>. The profile decides
    /// the attachment name (xrechnung.xml or factur-x.xml), the relationship and the XMP
    /// conformance level; it must match the XML.
    /// </summary>
    public static byte[] Create(byte[] carrierPdf, byte[] invoiceXml, InvoiceProfile profile, HybridPdfOptions? options = null)
    {
        if (profile == InvoiceProfile.PeppolBis3)
            throw new NotSupportedException("Peppol BIS documents are exchanged as XML, not as hybrid PDF.");
        options ??= new HybridPdfOptions();
        var timestamp = options.Timestamp ?? DateTimeOffset.Now;
        string fileName = profile.EmbeddedFileName();

        using var input = PdfReader.Open(new MemoryStream(carrierPdf), PdfDocumentOpenMode.Import);
        using var output = new PdfDocument();
        output.Version = 17;
        foreach (var page in input.Pages) output.AddPage(page);

        output.Info.Title = options.Title ?? "Invoice";
        if (options.Author is { Length: > 0 } author) output.Info.Author = author;
        if (options.Subject is { Length: > 0 } subject) output.Info.Subject = subject;
        output.Info.Creator = options.Creator;
        output.Info.CreationDate = timestamp.LocalDateTime;
        output.Info.ModificationDate = timestamp.LocalDateTime;

        AttachInvoice(output, invoiceXml, fileName, profile, timestamp);
        AddOutputIntent(output);
        if (options.EmbedMissingFonts)
            FontEmbedder.EmbedMissingFonts(output, options.FontDirectories, options.Log);

        using var saved = new MemoryStream();
        output.Save(saved, closeStream: false);

        // PDFsharp regenerates /Metadata during save from the document information and has
        // no Factur-X extension schema, so the metadata stream is replaced afterwards by an
        // incremental update (allowed by PDF/A): same object number, new XMP.
        var metadataRef = output.Internals.Catalog.Elements.GetReference("/Metadata")
            ?? throw new InvalidOperationException("PDFsharp wrote no metadata stream.");
        var xmp = Xmp.Build(output.Info, profile, fileName);
        return IncrementalUpdate.ReplaceStream(saved.ToArray(), metadataRef.ObjectNumber, metadataRef.GenerationNumber,
            "/Type/Metadata/Subtype/XML", xmp);
    }

    /// <summary>
    /// The invoice XML embedded in a hybrid PDF (factur-x.xml, xrechnung.xml or the ZUGFeRD 1
    /// and 2.0 names), or null when the PDF carries none.
    /// </summary>
    public static byte[]? ExtractXml(byte[] pdf)
    {
        using var doc = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        var names = doc.Internals.Catalog.Elements.GetDictionary("/Names")?.Elements.GetDictionary("/EmbeddedFiles");
        var candidates = new List<(string Name, PdfDictionary Spec)>();
        void Walk(PdfDictionary? node)
        {
            if (node is null) return;
            if (node.Elements.GetArray("/Names") is { } pairs)
            {
                for (int i = 0; i + 1 < pairs.Elements.Count; i += 2)
                {
                    string name = (pairs.Elements[i] as PdfString)?.Value ?? "";
                    var spec = pairs.Elements[i + 1] is PdfReference r ? r.Value as PdfDictionary : pairs.Elements[i + 1] as PdfDictionary;
                    if (spec is not null) candidates.Add((name, spec));
                }
            }
            if (node.Elements.GetArray("/Kids") is { } kids)
                foreach (var kid in kids.Elements)
                    Walk(kid is PdfReference kr ? kr.Value as PdfDictionary : kid as PdfDictionary);
        }
        Walk(names);

        string[] known = { "factur-x.xml", "xrechnung.xml", "zugferd-invoice.xml", "ZUGFeRD-invoice.xml" };
        var match = candidates.FirstOrDefault(c => known.Contains(c.Name, StringComparer.OrdinalIgnoreCase));
        if (match.Spec is null) match = candidates.FirstOrDefault(c => c.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        if (match.Spec?.Elements.GetDictionary("/EF") is not { } ef) return null;
        var fileRef = ef.Elements.GetReference("/F") ?? ef.Elements.GetReference("/UF");
        if (fileRef?.Value is not PdfDictionary stream || stream.Stream is null) return null;
        return stream.Stream.UnfilteredValue;
    }

    /// <summary>File variant of <see cref="ExtractXml(byte[])"/>.</summary>
    public static byte[]? ExtractXml(string pdfPath) => ExtractXml(File.ReadAllBytes(pdfPath));

    /// <summary>Reads the invoice embedded in a hybrid PDF, or null when the PDF carries none.</summary>
    public static Reading.ReadResult? ReadInvoice(byte[] pdf)
        => ExtractXml(pdf) is { } xml ? Reading.InvoiceReader.Read(xml) : null;

    // ── document parts ────────────────────────────────────────────────────────

    private static void AttachInvoice(PdfDocument doc, byte[] xml, string fileName, InvoiceProfile profile, DateTimeOffset timestamp)
    {
        var parameters = new PdfDictionary(doc);
        parameters.Elements["/ModDate"] = new PdfString(PdfDate(timestamp));
        parameters.Elements["/Size"] = new PdfInteger(xml.Length);

        var embedded = new PdfDictionary(doc);
        embedded.Elements["/Type"] = new PdfName("/EmbeddedFile");
        embedded.Elements["/Subtype"] = new PdfName("/text/xml");
        embedded.Elements["/Params"] = parameters;
        embedded.CreateStream(PdfSharp.Pdf.Filters.Filtering.FlateDecode.Encode(xml));
        embedded.Elements["/Filter"] = new PdfName("/FlateDecode");
        doc.Internals.AddObject(embedded);

        var ef = new PdfDictionary(doc);
        ef.Elements["/F"] = embedded.Reference;
        ef.Elements["/UF"] = embedded.Reference;

        // MINIMUM and BASIC WL are not a full invoice on their own: the PDF is the invoice and
        // the XML is data about it ("Data"); for every other profile the XML is an equivalent
        // representation ("Alternative"). Factur-X 1.09 / ZUGFeRD 2.5, section 6.
        string relationship = profile is InvoiceProfile.FacturXMinimum or InvoiceProfile.FacturXBasicWL ? "/Data" : "/Alternative";
        var spec = new PdfDictionary(doc);
        spec.Elements["/Type"] = new PdfName("/Filespec");
        spec.Elements["/F"] = new PdfString(fileName);
        spec.Elements["/UF"] = new PdfString(fileName, PdfStringEncoding.Unicode);
        spec.Elements["/Desc"] = new PdfString(profile == InvoiceProfile.XRechnung ? "XRechnung XML" : "Factur-X XML invoice");
        spec.Elements["/AFRelationship"] = new PdfName(relationship);
        spec.Elements["/EF"] = ef;
        doc.Internals.AddObject(spec);

        var af = new PdfArray(doc);
        af.Elements.Add(spec.Reference!);
        doc.Internals.Catalog.Elements["/AF"] = af;

        var namesArray = new PdfArray(doc);
        namesArray.Elements.Add(new PdfString(fileName));
        namesArray.Elements.Add(spec.Reference!);
        var embeddedFiles = new PdfDictionary(doc);
        embeddedFiles.Elements["/Names"] = namesArray;
        var names = doc.Internals.Catalog.Elements.GetDictionary("/Names") ?? new PdfDictionary(doc);
        names.Elements["/EmbeddedFiles"] = embeddedFiles;
        doc.Internals.Catalog.Elements["/Names"] = names;
    }

    private static void AddOutputIntent(PdfDocument doc)
    {
        var icc = new PdfDictionary(doc);
        icc.Elements["/N"] = new PdfInteger(3);
        icc.CreateStream(PdfSharp.Pdf.Filters.Filtering.FlateDecode.Encode(Resource("Hive.EInvoice.Pdf.sRGB.icc")));
        icc.Elements["/Filter"] = new PdfName("/FlateDecode");
        doc.Internals.AddObject(icc);

        var intent = new PdfDictionary(doc);
        intent.Elements["/Type"] = new PdfName("/OutputIntent");
        intent.Elements["/S"] = new PdfName("/GTS_PDFA1");
        intent.Elements["/OutputConditionIdentifier"] = new PdfString("sRGB IEC61966-2.1");
        intent.Elements["/Info"] = new PdfString("sRGB IEC61966-2.1");
        intent.Elements["/DestOutputProfile"] = icc.Reference;
        doc.Internals.AddObject(intent);

        var intents = new PdfArray(doc);
        intents.Elements.Add(intent.Reference!);
        doc.Internals.Catalog.Elements["/OutputIntents"] = intents;
    }

    internal static string PdfDate(DateTimeOffset t)
    {
        var o = t.Offset;
        string sign = o < TimeSpan.Zero ? "-" : "+";
        return $"D:{t:yyyyMMddHHmmss}{sign}{Math.Abs(o.Hours):00}'{Math.Abs(o.Minutes):00}'";
    }

    private static byte[] Resource(string name)
    {
        using var s = typeof(HybridPdf).GetTypeInfo().Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing embedded resource {name}.");
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    internal static string Invariant(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}
