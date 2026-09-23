using System;

namespace Hive.EInvoice;

/// <summary>
/// UNTDID 1001 document type code (BT-3). The numeric value of each member is the code
/// itself, so <c>(int)InvoiceTypeCode.CreditNote</c> is 381. Codes that have no member
/// here can still be used by casting, e.g. <c>(InvoiceTypeCode)393</c>; they are checked
/// against the EN 16931 code list by the validator.
/// </summary>
public enum InvoiceTypeCode
{
    /// <summary>83: credit note related to financial adjustments (for example a bonus credit).</summary>
    CreditNoteFinancialAdjustments = 83,
    /// <summary>84: debit note related to financial adjustments.</summary>
    DebitNoteFinancialAdjustments = 84,
    /// <summary>261: self billed credit note.</summary>
    SelfBilledCreditNote = 261,
    /// <summary>326: partial invoice.</summary>
    PartialInvoice = 326,
    /// <summary>380: commercial invoice, the default.</summary>
    CommercialInvoice = 380,
    /// <summary>381: credit note.</summary>
    CreditNote = 381,
    /// <summary>383: debit note.</summary>
    DebitNote = 383,
    /// <summary>384: corrected invoice.</summary>
    CorrectedInvoice = 384,
    /// <summary>386: prepayment invoice.</summary>
    PrepaymentInvoice = 386,
    /// <summary>389: self billed invoice.</summary>
    SelfBilledInvoice = 389,
    /// <summary>875: partial construction invoice (XRechnung).</summary>
    PartialConstructionInvoice = 875,
    /// <summary>876: partial final construction invoice (XRechnung).</summary>
    PartialFinalConstructionInvoice = 876,
    /// <summary>877: final construction invoice (XRechnung).</summary>
    FinalConstructionInvoice = 877,
}

/// <summary>UNTDID 5305 VAT category (BT-95, BT-102, BT-118, BT-151).</summary>
public enum VatCategory
{
    /// <summary>S: standard rate.</summary>
    StandardRate,
    /// <summary>Z: zero rated goods.</summary>
    ZeroRated,
    /// <summary>E: exempt from VAT.</summary>
    Exempt,
    /// <summary>AE: VAT reverse charge.</summary>
    ReverseCharge,
    /// <summary>K: VAT exempt for EEA intra-community supply of goods and services.</summary>
    IntraCommunitySupply,
    /// <summary>G: free export item, VAT not charged.</summary>
    ExportOutsideEu,
    /// <summary>O: services outside scope of tax.</summary>
    NotSubjectToVat,
    /// <summary>L: Canary Islands general indirect tax (IGIC).</summary>
    CanaryIslands,
    /// <summary>M: tax for production, services and importation in Ceuta and Melilla (IPSI).</summary>
    CeutaMelilla,
    /// <summary>B: transferred VAT (Italy).</summary>
    TransferredVat,
}

/// <summary>Code conversions for <see cref="VatCategory"/> and <see cref="InvoiceTypeCode"/>.</summary>
public static class CodeExtensions
{
    /// <summary>The UNTDID 5305 letter code of the category.</summary>
    public static string ToCode(this VatCategory category) => category switch
    {
        VatCategory.StandardRate => "S",
        VatCategory.ZeroRated => "Z",
        VatCategory.Exempt => "E",
        VatCategory.ReverseCharge => "AE",
        VatCategory.IntraCommunitySupply => "K",
        VatCategory.ExportOutsideEu => "G",
        VatCategory.NotSubjectToVat => "O",
        VatCategory.CanaryIslands => "L",
        VatCategory.CeutaMelilla => "M",
        VatCategory.TransferredVat => "B",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
    };

    /// <summary>Parses a UNTDID 5305 letter code (case-insensitive).</summary>
    public static bool TryParseVatCategory(string? code, out VatCategory category)
    {
        switch ((code ?? "").Trim().ToUpperInvariant())
        {
            case "S": category = VatCategory.StandardRate; return true;
            case "Z": category = VatCategory.ZeroRated; return true;
            case "E": category = VatCategory.Exempt; return true;
            case "AE": category = VatCategory.ReverseCharge; return true;
            case "K": category = VatCategory.IntraCommunitySupply; return true;
            case "G": category = VatCategory.ExportOutsideEu; return true;
            case "O": category = VatCategory.NotSubjectToVat; return true;
            case "L": category = VatCategory.CanaryIslands; return true;
            case "M": category = VatCategory.CeutaMelilla; return true;
            case "B": category = VatCategory.TransferredVat; return true;
            default: category = VatCategory.StandardRate; return false;
        }
    }

    /// <summary>
    /// True for the categories whose rate is fixed at zero and which need an exemption
    /// reason in the VAT breakdown (E, AE, K, G, O).
    /// </summary>
    public static bool RequiresExemptionReason(this VatCategory category)
        => category is VatCategory.Exempt or VatCategory.ReverseCharge or VatCategory.IntraCommunitySupply
            or VatCategory.ExportOutsideEu or VatCategory.NotSubjectToVat;

    /// <summary>The UNTDID 1001 code as text, e.g. "380".</summary>
    public static string ToCode(this InvoiceTypeCode type) => ((int)type).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// True for the document types that a UBL writer serialises as CreditNote rather than
    /// Invoice (EN 16931 / Peppol code list for the CreditNote document).
    /// </summary>
    public static bool IsCreditNote(this InvoiceTypeCode type)
        => (int)type is 81 or 83 or 261 or 262 or 296 or 308 or 381 or 396 or 420 or 458 or 532;
}

/// <summary>Frequently used UNTDID 4461 payment means codes (BT-81).</summary>
public static class PaymentMeansCode
{
    /// <summary>1: instrument not defined.</summary>
    public const string NotDefined = "1";
    /// <summary>10: in cash.</summary>
    public const string Cash = "10";
    /// <summary>20: cheque.</summary>
    public const string Cheque = "20";
    /// <summary>30: credit transfer (non SEPA).</summary>
    public const string CreditTransfer = "30";
    /// <summary>31: debit transfer.</summary>
    public const string DebitTransfer = "31";
    /// <summary>42: payment to bank account.</summary>
    public const string PaymentToBankAccount = "42";
    /// <summary>48: bank card.</summary>
    public const string BankCard = "48";
    /// <summary>49: direct debit (non SEPA).</summary>
    public const string DirectDebit = "49";
    /// <summary>57: standing agreement.</summary>
    public const string StandingAgreement = "57";
    /// <summary>58: SEPA credit transfer.</summary>
    public const string SepaCreditTransfer = "58";
    /// <summary>59: SEPA direct debit.</summary>
    public const string SepaDirectDebit = "59";
    /// <summary>97: clearing between partners.</summary>
    public const string ClearingBetweenPartners = "97";
}

/// <summary>Frequently used UN/ECE Recommendation 20 unit codes (BT-130, BT-150).</summary>
public static class UnitCode
{
    /// <summary>C62: one (unit without a specific measure). The EN 16931 default.</summary>
    public const string One = "C62";
    /// <summary>H87: piece.</summary>
    public const string Piece = "H87";
    /// <summary>HUR: hour.</summary>
    public const string Hour = "HUR";
    /// <summary>MIN: minute.</summary>
    public const string Minute = "MIN";
    /// <summary>DAY: day.</summary>
    public const string Day = "DAY";
    /// <summary>WEE: week.</summary>
    public const string Week = "WEE";
    /// <summary>MON: month.</summary>
    public const string Month = "MON";
    /// <summary>ANN: year.</summary>
    public const string Year = "ANN";
    /// <summary>KGM: kilogram.</summary>
    public const string Kilogram = "KGM";
    /// <summary>GRM: gram.</summary>
    public const string Gram = "GRM";
    /// <summary>TNE: tonne.</summary>
    public const string Tonne = "TNE";
    /// <summary>MTR: metre.</summary>
    public const string Metre = "MTR";
    /// <summary>KMT: kilometre.</summary>
    public const string Kilometre = "KMT";
    /// <summary>MTK: square metre.</summary>
    public const string SquareMetre = "MTK";
    /// <summary>MTQ: cubic metre.</summary>
    public const string CubicMetre = "MTQ";
    /// <summary>LTR: litre.</summary>
    public const string Litre = "LTR";
    /// <summary>KWH: kilowatt hour.</summary>
    public const string KilowattHour = "KWH";
    /// <summary>SET: set.</summary>
    public const string Set = "SET";
    /// <summary>PR: pair.</summary>
    public const string Pair = "PR";
    /// <summary>XPK: package.</summary>
    public const string Package = "XPK";
    /// <summary>LS: lump sum.</summary>
    public const string LumpSum = "LS";
    /// <summary>P1: percent.</summary>
    public const string Percent = "P1";
}

/// <summary>Frequently used CEF EAS electronic address schemes (BT-34-1, BT-49-1).</summary>
public static class ElectronicAddressScheme
{
    /// <summary>EM: electronic mail (SMTP).</summary>
    public const string Email = "EM";
    /// <summary>0204: German Leitweg-ID (public sector routing identifier).</summary>
    public const string LeitwegId = "0204";
    /// <summary>0088: GS1 Global Location Number.</summary>
    public const string Gln = "0088";
    /// <summary>9930: German VAT number.</summary>
    public const string GermanVatNumber = "9930";
    /// <summary>0060: DUNS number.</summary>
    public const string Duns = "0060";
}

/// <summary>Frequently used ISO 6523 ICD identifier schemes (BT-29-1, BT-46-1, BT-157-1).</summary>
public static class IdentifierScheme
{
    /// <summary>0088: GS1 Global Location Number (GLN), for parties.</summary>
    public const string Gln = "0088";
    /// <summary>0160: GS1 GTIN (item identifier).</summary>
    public const string Gtin = "0160";
    /// <summary>0060: DUNS number.</summary>
    public const string Duns = "0060";
    /// <summary>0204: German Leitweg-ID.</summary>
    public const string LeitwegId = "0204";
    /// <summary>9930: German VAT number.</summary>
    public const string GermanVatNumber = "9930";
}
