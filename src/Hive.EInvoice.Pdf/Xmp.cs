using System;
using System.Globalization;
using System.Security;
using System.Text;
using PdfSharp.Pdf;

namespace Hive.EInvoice.Pdf;

/// <summary>
/// XMP metadata for a Factur-X / ZUGFeRD / XRechnung hybrid PDF/A-3: the PDF/A identification
/// (part 3, conformance B), the document information mirrored exactly (PDF/A requires Info and
/// XMP to agree), and the Factur-X extension schema with DocumentType, DocumentFileName,
/// Version and ConformanceLevel.
/// </summary>
internal static class Xmp
{
    public static byte[] Build(PdfDocumentInformation info, InvoiceProfile profile, string fileName)
    {
        string E(string? s) => SecurityElement.Escape(s ?? "") ?? "";
        string Date(DateTime d) => d.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

        // fx:Version is the version of the Factur-X XMP schema convention, "1.0" for Factur-X
        // 1.x / ZUGFeRD 2.x; XRechnung documents use the XRechnung version.
        string version = profile == InvoiceProfile.XRechnung ? "3.0" : "1.0";

        var sb = new StringBuilder();
        sb.Append("<?xpacket begin=\"﻿\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>\n");
        sb.Append("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">\n");
        sb.Append(" <rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">\n");

        sb.Append("  <rdf:Description rdf:about=\"\" xmlns:pdfaid=\"http://www.aiim.org/pdfa/ns/id/\">\n");
        sb.Append("   <pdfaid:part>3</pdfaid:part>\n");
        sb.Append("   <pdfaid:conformance>B</pdfaid:conformance>\n");
        sb.Append("  </rdf:Description>\n");

        sb.Append("  <rdf:Description rdf:about=\"\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\">\n");
        sb.Append("   <dc:format>application/pdf</dc:format>\n");
        if (info.Title.Length > 0)
            sb.Append("   <dc:title><rdf:Alt><rdf:li xml:lang=\"x-default\">").Append(E(info.Title)).Append("</rdf:li></rdf:Alt></dc:title>\n");
        if (info.Author.Length > 0)
            sb.Append("   <dc:creator><rdf:Seq><rdf:li>").Append(E(info.Author)).Append("</rdf:li></rdf:Seq></dc:creator>\n");
        if (info.Subject.Length > 0)
            sb.Append("   <dc:description><rdf:Alt><rdf:li xml:lang=\"x-default\">").Append(E(info.Subject)).Append("</rdf:li></rdf:Alt></dc:description>\n");
        sb.Append("  </rdf:Description>\n");

        sb.Append("  <rdf:Description rdf:about=\"\" xmlns:pdf=\"http://ns.adobe.com/pdf/1.3/\">\n");
        sb.Append("   <pdf:Producer>").Append(E(info.Producer)).Append("</pdf:Producer>\n");
        if (info.Keywords.Length > 0) sb.Append("   <pdf:Keywords>").Append(E(info.Keywords)).Append("</pdf:Keywords>\n");
        sb.Append("  </rdf:Description>\n");

        sb.Append("  <rdf:Description rdf:about=\"\" xmlns:xmp=\"http://ns.adobe.com/xap/1.0/\">\n");
        if (info.Creator.Length > 0) sb.Append("   <xmp:CreatorTool>").Append(E(info.Creator)).Append("</xmp:CreatorTool>\n");
        sb.Append("   <xmp:CreateDate>").Append(Date(info.CreationDate)).Append("</xmp:CreateDate>\n");
        sb.Append("   <xmp:ModifyDate>").Append(Date(info.ModificationDate)).Append("</xmp:ModifyDate>\n");
        sb.Append("   <xmp:MetadataDate>").Append(Date(info.ModificationDate)).Append("</xmp:MetadataDate>\n");
        sb.Append("  </rdf:Description>\n");

        sb.Append("  <rdf:Description rdf:about=\"\" xmlns:pdfaExtension=\"http://www.aiim.org/pdfa/ns/extension/\" xmlns:pdfaSchema=\"http://www.aiim.org/pdfa/ns/schema#\" xmlns:pdfaProperty=\"http://www.aiim.org/pdfa/ns/property#\">\n");
        sb.Append("   <pdfaExtension:schemas>\n    <rdf:Bag>\n     <rdf:li rdf:parseType=\"Resource\">\n");
        sb.Append("      <pdfaSchema:schema>Factur-X PDFA Extension Schema</pdfaSchema:schema>\n");
        sb.Append("      <pdfaSchema:namespaceURI>urn:factur-x:pdfa:CrossIndustryDocument:invoice:1p0#</pdfaSchema:namespaceURI>\n");
        sb.Append("      <pdfaSchema:prefix>fx</pdfaSchema:prefix>\n");
        sb.Append("      <pdfaSchema:property>\n       <rdf:Seq>\n");
        Property(sb, "DocumentFileName", "The name of the embedded XML document");
        Property(sb, "DocumentType", "The type of the hybrid document in capital letters, e.g. INVOICE or ORDER");
        Property(sb, "Version", "The actual version of the standard applying to the embedded XML document");
        Property(sb, "ConformanceLevel", "The conformance level of the embedded XML document");
        sb.Append("       </rdf:Seq>\n      </pdfaSchema:property>\n     </rdf:li>\n    </rdf:Bag>\n   </pdfaExtension:schemas>\n");
        sb.Append("  </rdf:Description>\n");

        sb.Append("  <rdf:Description rdf:about=\"\" xmlns:fx=\"urn:factur-x:pdfa:CrossIndustryDocument:invoice:1p0#\">\n");
        sb.Append("   <fx:DocumentType>INVOICE</fx:DocumentType>\n");
        sb.Append("   <fx:DocumentFileName>").Append(E(fileName)).Append("</fx:DocumentFileName>\n");
        sb.Append("   <fx:Version>").Append(version).Append("</fx:Version>\n");
        sb.Append("   <fx:ConformanceLevel>").Append(profile.FacturXConformanceLevel()).Append("</fx:ConformanceLevel>\n");
        sb.Append("  </rdf:Description>\n");

        sb.Append(" </rdf:RDF>\n</x:xmpmeta>\n");
        // Padding lets tools update the packet in place, as the XMP specification recommends.
        sb.Append(new string(' ', 2000)).Append('\n');
        sb.Append("<?xpacket end=\"w\"?>");
        return new UTF8Encoding(false).GetBytes(sb.ToString());
    }

    private static void Property(StringBuilder sb, string name, string description)
    {
        sb.Append("        <rdf:li rdf:parseType=\"Resource\">\n");
        sb.Append("         <pdfaProperty:name>").Append(name).Append("</pdfaProperty:name>\n");
        sb.Append("         <pdfaProperty:valueType>Text</pdfaProperty:valueType>\n");
        sb.Append("         <pdfaProperty:category>external</pdfaProperty:category>\n");
        sb.Append("         <pdfaProperty:description>").Append(description).Append("</pdfaProperty:description>\n");
        sb.Append("        </rdf:li>\n");
    }
}
