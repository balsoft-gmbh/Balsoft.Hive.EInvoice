using System;
using System.Collections.Generic;

namespace Balsoft.Hive.EInvoice;

/// <summary>A seller (BG-4) or buyer (BG-7).</summary>
public sealed class Party
{
    /// <summary>BT-27 Seller name / BT-44 Buyer name. Mandatory.</summary>
    public string Name { get; set; } = "";

    /// <summary>BT-28 Seller trading name / BT-45 Buyer trading name.</summary>
    public string? TradingName { get; set; }

    /// <summary>
    /// BT-29 Seller identifier (repeatable) / BT-46 Buyer identifier (one). An identifier
    /// with a scheme (ISO 6523 ICD, e.g. 0088 for a GLN) is written as a global identifier.
    /// </summary>
    public List<SchemedIdentifier> Identifiers { get; } = new();

    /// <summary>BT-30 Seller / BT-47 Buyer legal registration identifier (with optional ICD scheme).</summary>
    public SchemedIdentifier? LegalRegistrationIdentifier { get; set; }

    /// <summary>BT-31 Seller / BT-48 Buyer VAT identifier, with the ISO 3166 country prefix (e.g. DE123456789).</summary>
    public string? VatIdentifier { get; set; }

    /// <summary>BT-32 Seller tax registration identifier (local tax number, "Steuernummer"). Seller only.</summary>
    public string? TaxRegistrationIdentifier { get; set; }

    /// <summary>BT-33 Seller additional legal information. Seller only.</summary>
    public string? AdditionalLegalInformation { get; set; }

    /// <summary>BT-34 Seller / BT-49 Buyer electronic address, with its EAS scheme. Mandatory in XRechnung.</summary>
    public SchemedIdentifier? ElectronicAddress { get; set; }

    /// <summary>BG-5 Seller / BG-8 Buyer postal address. Mandatory.</summary>
    public PostalAddress Address { get; set; } = new();

    /// <summary>BG-6 Seller / BG-9 Buyer contact. The seller contact is mandatory in XRechnung.</summary>
    public Contact? Contact { get; set; }
}

/// <summary>A postal address (BG-5, BG-8, BG-12, BG-15).</summary>
public sealed class PostalAddress
{
    /// <summary>Address line 1 (BT-35, BT-50, BT-64, BT-75).</summary>
    public string? Line1 { get; set; }

    /// <summary>Address line 2 (BT-36, BT-51, BT-65, BT-76).</summary>
    public string? Line2 { get; set; }

    /// <summary>Address line 3 (BT-162, BT-163, BT-164, BT-165).</summary>
    public string? Line3 { get; set; }

    /// <summary>City (BT-37, BT-52, BT-66, BT-77).</summary>
    public string? City { get; set; }

    /// <summary>Post code (BT-38, BT-53, BT-67, BT-78).</summary>
    public string? PostCode { get; set; }

    /// <summary>Country subdivision (BT-39, BT-54, BT-68, BT-79).</summary>
    public string? CountrySubdivision { get; set; }

    /// <summary>Country code, ISO 3166-1 alpha-2 (BT-40, BT-55, BT-69, BT-80). Mandatory.</summary>
    public string CountryCode { get; set; } = "";
}

/// <summary>A contact (BG-6 seller, BG-9 buyer).</summary>
public sealed class Contact
{
    /// <summary>BT-41 / BT-56 Contact point: a person or department.</summary>
    public string? Name { get; set; }

    /// <summary>BT-42 / BT-57 Contact telephone number. Mandatory for the seller in XRechnung (at least 3 digits).</summary>
    public string? Phone { get; set; }

    /// <summary>BT-43 / BT-58 Contact email address. Mandatory for the seller in XRechnung.</summary>
    public string? Email { get; set; }
}

/// <summary>BG-10 Payee, when the payment goes to a party other than the seller.</summary>
public sealed class Payee
{
    /// <summary>BT-59 Payee name.</summary>
    public string Name { get; set; } = "";

    /// <summary>BT-60 Payee identifier (with optional ICD scheme).</summary>
    public SchemedIdentifier? Identifier { get; set; }

    /// <summary>BT-61 Payee legal registration identifier (with optional ICD scheme).</summary>
    public SchemedIdentifier? LegalRegistrationIdentifier { get; set; }
}

/// <summary>BG-11 Seller tax representative party.</summary>
public sealed class TaxRepresentative
{
    /// <summary>BT-62 Seller tax representative name.</summary>
    public string Name { get; set; } = "";

    /// <summary>BT-63 Seller tax representative VAT identifier.</summary>
    public string VatIdentifier { get; set; } = "";

    /// <summary>BG-12 Seller tax representative postal address.</summary>
    public PostalAddress Address { get; set; } = new();
}

/// <summary>BG-13 Delivery information.</summary>
public sealed class DeliveryInformation
{
    /// <summary>BT-70 Deliver to party name.</summary>
    public string? PartyName { get; set; }

    /// <summary>BT-71 Deliver to location identifier (with optional ICD scheme).</summary>
    public SchemedIdentifier? LocationIdentifier { get; set; }

    /// <summary>BT-72 Actual delivery date.</summary>
    public DateTime? ActualDeliveryDate { get; set; }

    /// <summary>BG-15 Deliver to address.</summary>
    public PostalAddress? Address { get; set; }
}
