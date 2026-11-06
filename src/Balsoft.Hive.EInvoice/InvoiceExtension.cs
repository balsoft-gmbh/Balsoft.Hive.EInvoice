using System;
using System.Collections.Generic;

namespace Balsoft.Hive.EInvoice;

/// <summary>
/// Business terms that exist only in the Factur-X / ZUGFeRD EXTENDED profile. Every other profile
/// ignores them, and the pre-flight check warns when they are set there. Ids are those of the
/// Factur-X specification (BT-X-n, BG-X-n).
/// </summary>
public sealed class InvoiceExtension
{
    /// <summary>BT-X-1 The document is a test.</summary>
    public bool? IsTest { get; set; }

    /// <summary>BT-X-2 Document name.</summary>
    public string? DocumentName { get; set; }

    /// <summary>BT-X-3 The document is a copy.</summary>
    public bool? IsCopy { get; set; }

    /// <summary>BT-X-4 Invoice language code (ISO 639-2 alpha-3, for example "deu").</summary>
    public string? LanguageCode { get; set; }

    /// <summary>BT-X-6 Contractual due date of the invoice.</summary>
    public DateTime? ContractualDueDate { get; set; }

    /// <summary>BG-X-49 Sales agent.</summary>
    public ExtendedParty? SalesAgent { get; set; }

    /// <summary>BG-X-54 Buyer tax representative.</summary>
    public ExtendedParty? BuyerTaxRepresentative { get; set; }

    /// <summary>Seller tax representative with its EXTENDED terms. Takes the place of <see cref="Invoice.SellerTaxRepresentative"/> in this profile.</summary>
    public ExtendedParty? SellerTaxRepresentative { get; set; }

    /// <summary>BG-X-18 Product end user.</summary>
    public ExtendedParty? ProductEndUser { get; set; }

    /// <summary>BG-X-62 Buyer agent.</summary>
    public ExtendedParty? BuyerAgent { get; set; }

    /// <summary>Deliver to party with its EXTENDED terms. Takes the place of the party in <see cref="Invoice.Delivery"/> in this profile.</summary>
    public ExtendedParty? ShipTo { get; set; }

    /// <summary>BG-X-27 Ultimate ship to party.</summary>
    public ExtendedParty? UltimateShipTo { get; set; }

    /// <summary>BG-X-30 Ship from party.</summary>
    public ExtendedParty? ShipFrom { get; set; }

    /// <summary>Invoicer, the party that issues the invoice.</summary>
    public ExtendedParty? Invoicer { get; set; }

    /// <summary>Invoicee, the party the invoice is addressed to.</summary>
    public ExtendedParty? Invoicee { get; set; }

    /// <summary>Payee with its EXTENDED terms. Takes the place of <see cref="Invoice.Payee"/> in this profile.</summary>
    public ExtendedParty? Payee { get; set; }

    /// <summary>Payer, the party that pays the invoice.</summary>
    public ExtendedParty? Payer { get; set; }

    /// <summary>BG-X-22 Delivery terms.</summary>
    public DeliveryTerms? DeliveryTerms { get; set; }

    /// <summary>BT-X-152 Delivery method codes of the consignment (UN/ECE Recommendation 19).</summary>
    public List<string> TransportModeCodes { get; } = new();

    /// <summary>Invoice issuer reference.</summary>
    public string? InvoiceIssuerReference { get; set; }

    /// <summary>BT-X-146 Date of the seller's order confirmation (BT-14).</summary>
    public DateTime? SalesOrderDate { get; set; }

    /// <summary>BT-X-147 Date of the purchase order (BT-13).</summary>
    public DateTime? PurchaseOrderDate { get; set; }

    /// <summary>BT-X-148 Date of the contract (BT-12).</summary>
    public DateTime? ContractDate { get; set; }

    /// <summary>BT-X-405 Contract type code (UNTDID 1153).</summary>
    public string? ContractTypeCode { get; set; }

    /// <summary>BT-X-556 Date of the tender or lot document (BT-17).</summary>
    public DateTime? TenderOrLotDate { get; set; }

    /// <summary>BT-X-557 Date of the invoiced object document (BT-18).</summary>
    public DateTime? InvoicedObjectDate { get; set; }

    /// <summary>BT-X-200 Date of the despatch advice (BT-16).</summary>
    public DateTime? DespatchAdviceDate { get; set; }

    /// <summary>BT-X-201 Date of the receiving advice (BT-15).</summary>
    public DateTime? ReceivingAdviceDate { get; set; }

    /// <summary>BG-X-61 Quotation reference.</summary>
    public ExtendedDocumentReference? Quotation { get; set; }

    /// <summary>BG-X-23 Ultimate customer order references.</summary>
    public List<ExtendedDocumentReference> UltimateCustomerOrders { get; } = new();

    /// <summary>BT-X-202 Delivery note reference.</summary>
    public ExtendedDocumentReference? DeliveryNote { get; set; }

    /// <summary>BG-X-41 Exchange rate to the tax currency.</summary>
    public CurrencyExchange? TaxCurrencyExchange { get; set; }

    /// <summary>Writes BT-X-262 and BT-X-263, the line total and allowance and charge parts of each VAT basis, calculated from the invoice.</summary>
    public bool WriteVatBasisDetails { get; set; }

    /// <summary>BT-X-264 Invoicing period description.</summary>
    public string? InvoicingPeriodDescription { get; set; }

    /// <summary>BG-X-42 Logistics service charges. They count as document level charges in the totals of this profile.</summary>
    public List<LogisticsCharge> LogisticsCharges { get; } = new();

    /// <summary>Payment terms with their EXTENDED terms. When any is set they take the place of the payment terms, due date and mandate of the invoice in this profile.</summary>
    public List<ExtendedPaymentTerm> PaymentTerms { get; } = new();

    /// <summary>BG-34 Charges on behalf of a third party. They add to the amount due in this profile.</summary>
    public List<FinancialAdjustment> FinancialAdjustments { get; } = new();

    /// <summary>BT-X-290 Type code of the buyer accounting reference (BT-19).</summary>
    public string? BuyerAccountingTypeCode { get; set; }

    /// <summary>BG-X-45 Advance payments. Informational: <see cref="Invoice.PrepaidAmount"/> is what the totals use.</summary>
    public List<AdvancePayment> AdvancePayments { get; } = new();
}

/// <summary>BG-X-41 Exchange rate between two currencies.</summary>
public sealed class CurrencyExchange
{
    /// <summary>BT-X-258 Source currency. Mandatory.</summary>
    public string SourceCurrencyCode { get; set; } = "";

    /// <summary>BT-X-259 Target currency. Mandatory.</summary>
    public string TargetCurrencyCode { get; set; } = "";

    /// <summary>BT-X-260 Exchange rate. Mandatory.</summary>
    public decimal ConversionRate { get; set; }

    /// <summary>BT-X-261 Date of the exchange rate.</summary>
    public DateTime? Date { get; set; }
}

/// <summary>A tax on an EXTENDED charge or payment.</summary>
public sealed class ExtendedTax
{
    /// <summary>The tax amount, on an advance payment.</summary>
    public decimal? CalculatedAmount { get; set; }

    /// <summary>VAT category. Mandatory.</summary>
    public VatCategory Category { get; set; } = VatCategory.StandardRate;

    /// <summary>VAT rate.</summary>
    public decimal? Rate { get; set; }

    /// <summary>VAT exemption reason text.</summary>
    public string? ExemptionReason { get; set; }

    /// <summary>VAT exemption reason code.</summary>
    public string? ExemptionReasonCode { get; set; }
}

/// <summary>BG-X-42 Logistics service charge.</summary>
public sealed class LogisticsCharge
{
    /// <summary>BT-X-271 Description. Mandatory.</summary>
    public string Description { get; set; } = "";

    /// <summary>BT-X-272 Amount. Mandatory.</summary>
    public decimal Amount { get; set; }

    /// <summary>The taxes on the charge. At least one; the amount counts towards the VAT basis of the first.</summary>
    public List<ExtendedTax> Taxes { get; } = new();
}

/// <summary>One set of payment terms, with the terms only EXTENDED has.</summary>
public sealed class ExtendedPaymentTerm
{
    /// <summary>BT-20 Payment terms text.</summary>
    public string? Description { get; set; }

    /// <summary>BT-9 Due date.</summary>
    public DateTime? DueDate { get; set; }

    /// <summary>BT-89 Direct debit mandate.</summary>
    public string? MandateReference { get; set; }

    /// <summary>BT-X-275 Partial payment amount.</summary>
    public decimal? PartialPaymentAmount { get; set; }

    /// <summary>BG-X-43 Payment penalty.</summary>
    public PaymentAdjustment? Penalty { get; set; }

    /// <summary>BG-X-44 Payment discount.</summary>
    public PaymentAdjustment? Discount { get; set; }

    /// <summary>BG-X-77 Payee of this payment.</summary>
    public ExtendedParty? Payee { get; set; }
}

/// <summary>BG-X-43, BG-X-44 A payment penalty or discount.</summary>
public sealed class PaymentAdjustment
{
    /// <summary>Date the period starts from.</summary>
    public DateTime? BasisDate { get; set; }

    /// <summary>Length of the period.</summary>
    public decimal? Period { get; set; }

    /// <summary>Unit of the period (UN/ECE Recommendation 20).</summary>
    public string? PeriodUnitCode { get; set; }

    /// <summary>Base amount.</summary>
    public decimal? BaseAmount { get; set; }

    /// <summary>Percentage.</summary>
    public decimal? Percentage { get; set; }

    /// <summary>Amount.</summary>
    public decimal? Amount { get; set; }
}

/// <summary>BG-34 A charge collected on behalf of a third party.</summary>
public sealed class FinancialAdjustment
{
    /// <summary>BT-180 What the charge is. Mandatory.</summary>
    public string Reason { get; set; } = "";

    /// <summary>BT-179 Amount. Mandatory.</summary>
    public decimal Amount { get; set; }
}

/// <summary>BG-X-45 Advance payment.</summary>
public sealed class AdvancePayment
{
    /// <summary>BT-X-291 Amount paid. Mandatory.</summary>
    public decimal PaidAmount { get; set; }

    /// <summary>BT-X-292 Date of the payment.</summary>
    public DateTime? ReceivedDate { get; set; }

    /// <summary>BG-X-46 The taxes included. At least one.</summary>
    public List<ExtendedTax> Taxes { get; } = new();

    /// <summary>BT-X-558 Number of the invoice the payment belongs to.</summary>
    public string? InvoiceNumber { get; set; }

    /// <summary>BT-X-559 Type code of that invoice.</summary>
    public string? InvoiceTypeCode { get; set; }

    /// <summary>BT-X-560 Date of that invoice.</summary>
    public DateTime? InvoiceDate { get; set; }
}

/// <summary>A party with the terms EXTENDED gives each of its roles.</summary>
public sealed class ExtendedParty
{
    /// <summary>Party identifier without a scheme.</summary>
    public string? Identifier { get; set; }

    /// <summary>Global identifiers, each with a scheme.</summary>
    public List<SchemedIdentifier> GlobalIdentifiers { get; } = new();

    /// <summary>Party name. Mandatory.</summary>
    public string Name { get; set; } = "";

    /// <summary>Role code (UNTDID 3035).</summary>
    public string? RoleCode { get; set; }

    /// <summary>Additional legal information. Only the line level seller and manufacturer have it.</summary>
    public string? Description { get; set; }

    /// <summary>Legal registration identifier, with an optional scheme.</summary>
    public SchemedIdentifier? LegalRegistrationIdentifier { get; set; }

    /// <summary>Trading name.</summary>
    public string? TradingName { get; set; }

    /// <summary>Legal postal address. Document level parties only; a line level party ignores it.</summary>
    public PostalAddress? LegalAddress { get; set; }

    /// <summary>Contacts.</summary>
    public List<Contact> Contacts { get; } = new();

    /// <summary>Postal address.</summary>
    public PostalAddress? Address { get; set; }

    /// <summary>Electronic address, with a scheme.</summary>
    public SchemedIdentifier? ElectronicAddress { get; set; }

    /// <summary>VAT identifier.</summary>
    public string? VatIdentifier { get; set; }

    /// <summary>
    /// Local tax identifier. The official workbook lists it for the line level seller and manufacturer only, but the
    /// official rule BR-FXEXT-03 (Factur-X 1.09.2) rejects any scheme other than VA outside the seller, so a validator refuses it.
    /// </summary>
    public string? TaxRegistrationIdentifier { get; set; }
}

/// <summary>A reference to a document by its number and date.</summary>
public sealed class ExtendedDocumentReference
{
    /// <summary>The document number. Mandatory.</summary>
    public string Number { get; set; } = "";

    /// <summary>The document date.</summary>
    public DateTime? Date { get; set; }
}

/// <summary>BG-X-22 Delivery terms.</summary>
public sealed class DeliveryTerms
{
    /// <summary>BT-X-145 Delivery terms code (Incoterms). Mandatory.</summary>
    public string Code { get; set; } = "";

    /// <summary>BT-X-563 Country of the place the terms refer to.</summary>
    public string? LocationCountryCode { get; set; }

    /// <summary>BT-X-564 Name of the place the terms refer to.</summary>
    public string? LocationName { get; set; }
}

/// <summary>Invoice line terms that exist only in the Factur-X / ZUGFeRD EXTENDED profile.</summary>
public sealed class InvoiceLineExtension
{
    /// <summary>BT-X-304 The line this one belongs to.</summary>
    public string? ParentLineId { get; set; }

    /// <summary>BT-X-7 Line status code.</summary>
    public string? LineStatusCode { get; set; }

    /// <summary>
    /// BT-X-8 Line status reason code: DETAIL, GROUP or INFORMATION. A line with another value than DETAIL
    /// does not count in the totals of this profile.
    /// </summary>
    public string? LineStatusReasonCode { get; set; }

    /// <summary>BT-X-9 Line note content code.</summary>
    public string? NoteContentCode { get; set; }

    /// <summary>BT-X-10 Line note subject code.</summary>
    public string? NoteSubjectCode { get; set; }

    /// <summary>BT-X-305 Item identifier without a scheme.</summary>
    public string? ItemIdentifier { get; set; }

    /// <summary>BT-X-532 Item industry assigned identifier.</summary>
    public string? IndustryAssignedIdentifier { get; set; }

    /// <summary>BT-X-533 Item model identifier.</summary>
    public string? ModelIdentifier { get; set; }

    /// <summary>BT-X-534 Batch identifiers.</summary>
    public List<string> BatchIdentifiers { get; } = new();

    /// <summary>BT-X-535 Brand name.</summary>
    public string? BrandName { get; set; }

    /// <summary>BT-X-536 Model name.</summary>
    public string? ModelName { get; set; }

    /// <summary>BG-X-84 Individual product instances.</summary>
    public List<ProductInstance> ProductInstances { get; } = new();

    /// <summary>BG-X-93 Manufacturer.</summary>
    public ExtendedParty? Manufacturer { get; set; }

    /// <summary>BG-X-1 Items included in this one.</summary>
    public List<IncludedProduct> IncludedProducts { get; } = new();

    /// <summary>BG-X-87 Delivery terms of this line.</summary>
    public DeliveryTerms? DeliveryTerms { get; set; }

    /// <summary>BG-X-81 Sales order reference.</summary>
    public LineDocumentReference? SalesOrder { get; set; }

    /// <summary>BT-X-21 Purchase order number. The line reference is <see cref="InvoiceLine.OrderLineReference"/>.</summary>
    public string? PurchaseOrderNumber { get; set; }

    /// <summary>BT-X-22 Purchase order date.</summary>
    public DateTime? PurchaseOrderDate { get; set; }

    /// <summary>BG-X-47 Quotation reference.</summary>
    public LineDocumentReference? Quotation { get; set; }

    /// <summary>BG-X-2 Contract reference.</summary>
    public LineDocumentReference? Contract { get; set; }

    /// <summary>BG-X-3 Additional documents.</summary>
    public List<LineSupportingDocument> SupportingDocuments { get; } = new();

    /// <summary>BT-X-34 and following: allowances and charges on the gross price. Take the place of <see cref="InvoiceLine.PriceDiscount"/> when set.</summary>
    public List<AllowanceCharge> GrossPriceAllowancesAndCharges { get; } = new();

    /// <summary>BG-X-4 Tax included in the net price, for B2C.</summary>
    public ExtendedTax? NetPriceIncludedTax { get; set; }

    /// <summary>BG-X-90 Seller of this line.</summary>
    public ExtendedParty? ItemSeller { get; set; }

    /// <summary>BG-X-5 Ultimate customer order references.</summary>
    public List<LineDocumentReference> UltimateCustomerOrders { get; } = new();

    /// <summary>BT-X-46 Free of charge quantity.</summary>
    public decimal? ChargeFreeQuantity { get; set; }

    /// <summary>BT-X-46-0 Unit of the free of charge quantity.</summary>
    public string? ChargeFreeQuantityUnitCode { get; set; }

    /// <summary>BT-X-47 Number of packages.</summary>
    public decimal? PackageQuantity { get; set; }

    /// <summary>BT-X-47-0 Unit of the number of packages.</summary>
    public string? PackageQuantityUnitCode { get; set; }

    /// <summary>BT-X-561 Units per package.</summary>
    public decimal? PerPackageUnitQuantity { get; set; }

    /// <summary>BT-X-561-0 Unit of the units per package.</summary>
    public string? PerPackageUnitCode { get; set; }

    /// <summary>BG-X-7 Ship to party of this line.</summary>
    public ExtendedParty? ShipTo { get; set; }

    /// <summary>BG-X-10 Ultimate ship to party of this line.</summary>
    public ExtendedParty? UltimateShipTo { get; set; }

    /// <summary>BT-X-85 Delivery date of this line.</summary>
    public DateTime? DeliveryDate { get; set; }

    /// <summary>BG-X-13 Despatch advice reference.</summary>
    public LineDocumentReference? DespatchAdvice { get; set; }

    /// <summary>BG-X-82 Receiving advice reference.</summary>
    public LineDocumentReference? ReceivingAdvice { get; set; }

    /// <summary>BG-X-83 Delivery note reference.</summary>
    public LineDocumentReference? DeliveryNote { get; set; }

    /// <summary>BT-X-95 VAT amount of this line.</summary>
    public decimal? VatAmount { get; set; }

    /// <summary>BT-X-96 VAT exemption reason of this line.</summary>
    public string? VatExemptionReason { get; set; }

    /// <summary>BT-X-97 VAT exemption reason code of this line.</summary>
    public string? VatExemptionReasonCode { get; set; }

    /// <summary>BT-X-589 VAT point date code of this line.</summary>
    public string? VatPointDateCode { get; set; }

    /// <summary>BT-X-327 Stated total of the line charges.</summary>
    public decimal? ChargeTotal { get; set; }

    /// <summary>BT-X-328 Stated total of the line allowances.</summary>
    public decimal? AllowanceTotal { get; set; }

    /// <summary>BT-X-329 Stated VAT total of the line, in the invoice currency.</summary>
    public decimal? VatTotal { get; set; }

    /// <summary>BT-X-329-0 Currency of <see cref="VatTotal"/>.</summary>
    public string? VatTotalCurrency { get; set; }

    /// <summary>BT-X-590 Stated VAT total of the line, in the tax currency.</summary>
    public decimal? VatTotalInTaxCurrency { get; set; }

    /// <summary>BT-X-590-0 Currency of <see cref="VatTotalInTaxCurrency"/>.</summary>
    public string? VatTotalInTaxCurrencyCode { get; set; }

    /// <summary>BT-X-330 Stated grand total of the line.</summary>
    public decimal? GrandTotal { get; set; }

    /// <summary>BT-X-98 Stated total of the line allowances and charges.</summary>
    public decimal? AllowanceChargeTotal { get; set; }

    /// <summary>BG-X-48 Preceding invoice line.</summary>
    public LineDocumentReference? PrecedingInvoice { get; set; }

    /// <summary>BT-X-332 Type code of the preceding invoice.</summary>
    public string? PrecedingInvoiceTypeCode { get; set; }

    /// <summary>BT-X-99 Type code of the line accounting reference.</summary>
    public string? AccountingTypeCode { get; set; }
}

/// <summary>A reference from a line to a document, a line in it and its date.</summary>
public sealed class LineDocumentReference
{
    /// <summary>The document number.</summary>
    public string? Number { get; set; }

    /// <summary>The line in the document.</summary>
    public string? LineId { get; set; }

    /// <summary>The document date.</summary>
    public DateTime? Date { get; set; }
}

/// <summary>BG-X-3 A document referenced by a line.</summary>
public sealed class LineSupportingDocument
{
    /// <summary>BT-X-27 Document number. Mandatory.</summary>
    public string Number { get; set; } = "";

    /// <summary>BT-X-28 External location.</summary>
    public string? ExternalLocation { get; set; }

    /// <summary>BT-X-29 Line in the document.</summary>
    public string? LineId { get; set; }

    /// <summary>BT-X-30 Type code. Mandatory.</summary>
    public string TypeCode { get; set; } = "916";

    /// <summary>BT-X-299 Description.</summary>
    public string? Description { get; set; }

    /// <summary>BT-X-31 Attached content.</summary>
    public byte[]? Content { get; set; }

    /// <summary>BT-X-31-1 Attachment MIME code.</summary>
    public string? MimeCode { get; set; }

    /// <summary>BT-X-31-2 Attachment file name.</summary>
    public string? FileName { get; set; }

    /// <summary>BT-X-32 Reference type code.</summary>
    public string? ReferenceTypeCode { get; set; }

    /// <summary>BT-X-33 Document date.</summary>
    public DateTime? Date { get; set; }
}

/// <summary>BG-X-84 An individual instance of a product.</summary>
public sealed class ProductInstance
{
    /// <summary>BT-X-306 Batch identifier.</summary>
    public string? BatchIdentifier { get; set; }

    /// <summary>BT-X-307 Serial number.</summary>
    public string? SerialNumber { get; set; }
}

/// <summary>BG-X-1 An item included in a line's item.</summary>
public sealed class IncludedProduct
{
    /// <summary>BT-X-308 Identifier without a scheme.</summary>
    public string? Identifier { get; set; }

    /// <summary>BT-X-15 Global identifiers, each with a scheme.</summary>
    public List<SchemedIdentifier> GlobalIdentifiers { get; } = new();

    /// <summary>BT-X-16 Seller's identifier.</summary>
    public string? SellersIdentifier { get; set; }

    /// <summary>BT-X-17 Buyer's identifier.</summary>
    public string? BuyersIdentifier { get; set; }

    /// <summary>BT-X-309 Industry assigned identifier.</summary>
    public string? IndustryAssignedIdentifier { get; set; }

    /// <summary>BT-X-18 Name. Mandatory.</summary>
    public string Name { get; set; } = "";

    /// <summary>BT-X-19 Description.</summary>
    public string? Description { get; set; }

    /// <summary>BT-X-20 Quantity included.</summary>
    public decimal? Quantity { get; set; }

    /// <summary>BT-X-20-1 Unit of the quantity.</summary>
    public string? QuantityUnitCode { get; set; }
}

