using System;

namespace Hive.EInvoice;

/// <summary>
/// The specification a document is written against. It decides the specification
/// identifier (BT-24), which business terms are written, and which business rules the
/// validator applies.
/// </summary>
public enum InvoiceProfile
{
    /// <summary>
    /// XRechnung 3.0 (German CIUS of EN 16931), valid for public sector and B2B invoicing in
    /// Germany. Adds the BR-DE rules: buyer reference, seller contact, payment instructions,
    /// electronic addresses.
    /// </summary>
    XRechnung,

    /// <summary>EN 16931 core without any CIUS. Equals ZUGFeRD / Factur-X profile "EN 16931" (formerly COMFORT).</summary>
    EN16931,

    /// <summary>Factur-X / ZUGFeRD profile BASIC: EN 16931 compliant subset with invoice lines.</summary>
    FacturXBasic,

    /// <summary>Factur-X / ZUGFeRD profile BASIC WL: document level only, no invoice lines. Not a valid EN 16931 invoice on its own.</summary>
    FacturXBasicWL,

    /// <summary>Factur-X / ZUGFeRD profile MINIMUM: header and totals only. Not a valid EN 16931 invoice on its own.</summary>
    FacturXMinimum,

    /// <summary>
    /// Factur-X / ZUGFeRD profile EXTENDED. This library writes the EN 16931 content under
    /// the EXTENDED identifier, which is valid because EXTENDED is a superset.
    /// </summary>
    FacturXExtended,

    /// <summary>Peppol BIS Billing 3.0 (usually written as UBL).</summary>
    PeppolBis3,
}

/// <summary>Identifiers and properties of an <see cref="InvoiceProfile"/>.</summary>
public static class InvoiceProfileExtensions
{
    private static readonly InvoiceProfile[] All =
    {
        InvoiceProfile.XRechnung, InvoiceProfile.EN16931, InvoiceProfile.FacturXBasic, InvoiceProfile.FacturXBasicWL,
        InvoiceProfile.FacturXMinimum, InvoiceProfile.FacturXExtended, InvoiceProfile.PeppolBis3,
    };

    /// <summary>Specification identifier BT-24 (CII GuidelineSpecifiedDocumentContextParameter, UBL CustomizationID).</summary>
    public static string SpecificationIdentifier(this InvoiceProfile profile) => profile switch
    {
        InvoiceProfile.XRechnung => "urn:cen.eu:en16931:2017#compliant#urn:xeinkauf.de:kosit:xrechnung_3.0",
        InvoiceProfile.EN16931 => "urn:cen.eu:en16931:2017",
        InvoiceProfile.FacturXBasic => "urn:cen.eu:en16931:2017#compliant#urn:factur-x.eu:1p0:basic",
        InvoiceProfile.FacturXBasicWL => "urn:factur-x.eu:1p0:basicwl",
        InvoiceProfile.FacturXMinimum => "urn:factur-x.eu:1p0:minimum",
        InvoiceProfile.FacturXExtended => "urn:cen.eu:en16931:2017#conformant#urn:factur-x.eu:1p0:extended",
        InvoiceProfile.PeppolBis3 => "urn:cen.eu:en16931:2017#compliant#urn:fdc:peppol.eu:2017:poacc:billing:3.0",
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, null),
    };

    /// <summary>The profile whose specification identifier is <paramref name="identifier"/>, if any.</summary>
    public static bool TryFromSpecificationIdentifier(string? identifier, out InvoiceProfile profile)
    {
        foreach (var p in All)
        {
            if (string.Equals(p.SpecificationIdentifier(), (identifier ?? "").Trim(), StringComparison.Ordinal))
            {
                profile = p;
                return true;
            }
        }
        profile = InvoiceProfile.EN16931;
        return false;
    }

    /// <summary>
    /// The Factur-X conformance level written into the PDF/A-3 XMP metadata
    /// (fx:ConformanceLevel): MINIMUM, BASIC WL, BASIC, EN 16931, EXTENDED or XRECHNUNG.
    /// </summary>
    public static string FacturXConformanceLevel(this InvoiceProfile profile) => profile switch
    {
        InvoiceProfile.XRechnung => "XRECHNUNG",
        InvoiceProfile.EN16931 => "EN 16931",
        InvoiceProfile.FacturXBasic => "BASIC",
        InvoiceProfile.FacturXBasicWL => "BASIC WL",
        InvoiceProfile.FacturXMinimum => "MINIMUM",
        InvoiceProfile.FacturXExtended => "EXTENDED",
        _ => throw new NotSupportedException($"{profile} is not a Factur-X / ZUGFeRD profile."),
    };

    /// <summary>
    /// Name of the XML attachment inside a hybrid PDF: "xrechnung.xml" for XRechnung,
    /// "factur-x.xml" for every other Factur-X / ZUGFeRD 2.x profile.
    /// </summary>
    public static string EmbeddedFileName(this InvoiceProfile profile)
        => profile == InvoiceProfile.XRechnung ? "xrechnung.xml" : "factur-x.xml";

    /// <summary>True when documents of this profile carry invoice lines (BG-25).</summary>
    public static bool HasLines(this InvoiceProfile profile)
        => profile is not (InvoiceProfile.FacturXMinimum or InvoiceProfile.FacturXBasicWL);

    /// <summary>True when the profile is EN 16931 compliant, so the full EN 16931 rule set applies.</summary>
    public static bool IsEN16931Compliant(this InvoiceProfile profile) => profile.HasLines();
}
