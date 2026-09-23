using System;
using System.Collections.Generic;

namespace Balsoft.Hive.EInvoice.CodeLists;

/// <summary>
/// The code lists EN 16931 permits (rules BR-CL-*), taken from the official validation
/// artefact. Lookups are case-sensitive, as in the standard.
/// </summary>
public static class CodeList
{
    private static readonly HashSet<string> InvoiceTypes = new(CodeListData.InvoiceTypeCodes, StringComparer.Ordinal);
    private static readonly HashSet<string> Currencies = new(CodeListData.CurrencyCodes, StringComparer.Ordinal);
    private static readonly HashSet<string> VatDates = new(CodeListData.VatDateCodes, StringComparer.Ordinal);
    private static readonly HashSet<string> ObjectSchemes = new(CodeListData.ObjectIdentifierSchemes, StringComparer.Ordinal);
    private static readonly HashSet<string> TextSubjects = new(CodeListData.TextSubjectCodes, StringComparer.Ordinal);
    private static readonly HashSet<string> IdSchemes = new(CodeListData.IdentifierSchemes, StringComparer.Ordinal);
    private static readonly HashSet<string> RegistrationSchemes = new(CodeListData.RegistrationIdentifierSchemes, StringComparer.Ordinal);
    private static readonly HashSet<string> ClassificationSchemes = new(CodeListData.ItemClassificationSchemes, StringComparer.Ordinal);
    private static readonly HashSet<string> Countries = new(CodeListData.CountryCodes, StringComparer.Ordinal);
    private static readonly HashSet<string> PaymentMeans = new(CodeListData.PaymentMeansCodes, StringComparer.Ordinal);
    private static readonly HashSet<string> AllowanceReasons = new(CodeListData.AllowanceReasonCodes, StringComparer.Ordinal);
    private static readonly HashSet<string> ChargeReasons = new(CodeListData.ChargeReasonCodes, StringComparer.Ordinal);
    private static readonly HashSet<string> Vatex = new(CodeListData.VatExemptionReasonCodes, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Units = new(CodeListData.UnitCodes, StringComparer.Ordinal);
    private static readonly HashSet<string> Mimes = new(CodeListData.MimeCodes, StringComparer.Ordinal);
    private static readonly HashSet<string> Eas = new(CodeListData.ElectronicAddressSchemes, StringComparer.Ordinal);

    /// <summary>UNTDID 1001 document type code permitted for BT-3 (BR-CL-01).</summary>
    public static bool IsInvoiceTypeCode(string? code) => Has(InvoiceTypes, code);

    /// <summary>ISO 4217 alpha-3 currency code (BR-CL-03/04/05).</summary>
    public static bool IsCurrencyCode(string? code) => Has(Currencies, code);

    /// <summary>VAT point date code (BR-CL-06).</summary>
    public static bool IsVatDateCode(string? code) => Has(VatDates, code);

    /// <summary>UNTDID 1153 object identifier scheme (BR-CL-07).</summary>
    public static bool IsObjectIdentifierScheme(string? code) => Has(ObjectSchemes, code);

    /// <summary>UNTDID 4451 text subject code (BR-CL-08).</summary>
    public static bool IsTextSubjectCode(string? code) => Has(TextSubjects, code);

    /// <summary>ISO 6523 ICD identifier scheme (BR-CL-10, BR-CL-21, BR-CL-26).</summary>
    public static bool IsIdentifierScheme(string? code) => Has(IdSchemes, code);

    /// <summary>ISO 6523 ICD scheme for legal registration identifiers (BR-CL-11).</summary>
    public static bool IsRegistrationIdentifierScheme(string? code) => Has(RegistrationSchemes, code);

    /// <summary>UNTDID 7143 item classification scheme (BR-CL-13).</summary>
    public static bool IsItemClassificationScheme(string? code) => Has(ClassificationSchemes, code);

    /// <summary>ISO 3166-1 alpha-2 country code (BR-CL-14, BR-CL-15).</summary>
    public static bool IsCountryCode(string? code) => Has(Countries, code);

    /// <summary>UNTDID 4461 payment means code (BR-CL-16).</summary>
    public static bool IsPaymentMeansCode(string? code) => Has(PaymentMeans, code);

    /// <summary>UNTDID 5189 allowance reason code (BR-CL-19).</summary>
    public static bool IsAllowanceReasonCode(string? code) => Has(AllowanceReasons, code);

    /// <summary>UNTDID 7161 charge reason code (BR-CL-20).</summary>
    public static bool IsChargeReasonCode(string? code) => Has(ChargeReasons, code);

    /// <summary>CEF VATEX exemption reason code (BR-CL-22).</summary>
    public static bool IsVatExemptionReasonCode(string? code) => Has(Vatex, code);

    /// <summary>UN/ECE Rec 20 (with Rec 21) unit code (BR-CL-23).</summary>
    public static bool IsUnitCode(string? code) => Has(Units, code);

    /// <summary>MIME code permitted for attachments (BR-CL-24).</summary>
    public static bool IsMimeCode(string? code) => Has(Mimes, code);

    /// <summary>CEF EAS electronic address scheme (BR-CL-25).</summary>
    public static bool IsElectronicAddressScheme(string? code) => Has(Eas, code);

    private static bool Has(HashSet<string> set, string? code) => code is not null && set.Contains(code.Trim());
}
