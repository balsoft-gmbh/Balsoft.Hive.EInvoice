# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/).

## [Unreleased]

## [1.1.0] - 2026-10-23

### Added
- `Invoice.ProjectName`: the project name CII writes into `SpecifiedProcuringProject/Name` next
  to the project reference (BT-11). Before, that mandatory element always said "Project
  reference". EN 16931 has no business term for the name, so UBL leaves it out and the invoice
  stays the same EN 16931 invoice in both syntaxes. Reading a CII document fills it unless the
  name is the placeholder.

### Fixed
- `ReadResult.TotalsDiscrepancies()` formats amounts with the invariant culture. On a machine
  with a German culture it said "stated 2616,80" instead of "stated 2616.80".

### Security
- `Balsoft.Hive.EInvoice.Pdf` references System.Security.Cryptography.Pkcs 8.0.2. PDFsharp
  6.2.4 brings 8.0.1, which is affected by CVE-2026-84008 and CVE-2026-70162 (denial of
  service, published 2026-10-13).

### Changed
- Documentation and package description name the exact versions the test suite validates
  against: XRechnung 3.0.2 (KoSIT validator 1.6.3, configuration 2026-08-31) and
  ZUGFeRD 2.5.2 / Factur-X 1.09.2 (Mustang 2.26). The output is unchanged; CI passed against
  these versions on 2026-10-11.

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
