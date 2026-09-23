# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/).

## [1.0.0] - 2026-09-23

First stable release. The API of 0.1.0-preview.1 is unchanged; hybrid PDFs now repair the
carrier defects found on a live AX 2009 installation, verified with veraPDF.

### Added
- `HybridPdfOptions.RemoveTrailingNotdef` (default on): removes glyph 0 at the end of text
  strings in two-byte Identity fonts, which some ERP print engines emit for line breaks and
  PDF/A forbids (ISO 19005-3, 6.2.11.8). Nothing visible changes.

### Fixed
- Font embedding picks the bold or italic font file from the font descriptor (ForceBold,
  Italic flags, weight, italic angle) when the font name carries no style, as with AX 2009,
  which names its bold font plain "Arial". Wrong glyph widths (6.2.11.5) no longer occur.
- An embedded CIDFontType2 gets an explicit `/CIDToGIDMap /Identity` (6.2.11.3.2).

## [0.1.0-preview.1] - 2026-09-23

First public preview, published as `Balsoft.Hive.EInvoice` and `Balsoft.Hive.EInvoice.Pdf`
(the `Hive.` package prefix on nuget.org is reserved by another publisher).

### Added
- EN 16931 invoice model named after the business terms (BT/BG), with documentation per term.
- `InvoiceCalculator`: line net amounts, document totals and VAT breakdown per EN 16931,
  rounding per VAT category, allowances and charges on line and document level, prepaid and
  rounding amounts.
- `CiiWriter`: UN/CEFACT CII D16B for XRechnung 3.0, EN 16931 and the Factur-X / ZUGFeRD 2.5
  profiles MINIMUM, BASIC WL, BASIC and EXTENDED. XRechnung Skonto syntax for cash discounts.
- `InvoiceValidator`: pre-flight check of the EN 16931, VAT category, code list and XRechnung
  (BR-DE) rules, with the official rule identifiers.
- `CodeList`: the EN 16931 code lists, extracted from the official validation artefact.
- `UblWriter`: UBL 2.1 Invoice and CreditNote for XRechnung, EN 16931 and Peppol BIS 3.0.
- `CiiReader`, `UblReader`, `InvoiceReader`: read CII and UBL back into the model, with the
  stated totals and `TotalsDiscrepancies()` against the EN 16931 calculation.
- `Balsoft.Hive.EInvoice.Pdf`: `HybridPdf.Create` builds PDF/A-3b hybrid invoices (Factur-X XMP,
  generated sRGB output intent, associated file, missing TrueType fonts embedded from the
  system); `HybridPdf.ExtractXml` and `HybridPdf.ReadInvoice` read them.
- Test suite: 17 samples accepted by KoSIT as CII and UBL (XRechnung, EN 16931), by Mustang
  in the Factur-X profiles, hybrid PDFs accepted by veraPDF, byte-exact round trips through
  the readers, and 31 negative cases proving the pre-flight reports KoSIT's rule ids.
