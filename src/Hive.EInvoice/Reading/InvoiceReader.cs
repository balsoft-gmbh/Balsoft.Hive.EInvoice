using System;
using System.IO;
using System.Xml.Linq;
using Hive.EInvoice.Cii;
using Hive.EInvoice.Ubl;

namespace Hive.EInvoice.Reading;

/// <summary>Reads CII or UBL e-invoice XML, detecting the syntax from the root element.</summary>
public static class InvoiceReader
{
    /// <summary>Reads an e-invoice XML document.</summary>
    public static ReadResult Read(byte[] xml) => Read(new MemoryStream(xml));

    /// <summary>Reads an e-invoice XML document.</summary>
    public static ReadResult Read(Stream xml)
    {
        var doc = XDocument.Load(xml, LoadOptions.PreserveWhitespace);
        var ns = doc.Root?.Name.NamespaceName;
        if (ns == CiiWriter.Rsm) return CiiReader.Read(doc);
        if (ns == UblWriter.InvoiceNs || ns == UblWriter.CreditNoteNs) return UblReader.Read(doc);
        throw new FormatException($"Unknown e-invoice syntax (root element {doc.Root?.Name}).");
    }

    /// <summary>Reads an e-invoice XML file.</summary>
    public static ReadResult Read(string path)
    {
        using var s = File.OpenRead(path);
        return Read(s);
    }
}
