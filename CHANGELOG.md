# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/).

## [0.1.0-preview.1] - 2026-09-23

First public preview.

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
- Test suite validating every sample with the KoSIT validator and Mustang, and 30 negative
  cases proving the pre-flight reports the same rule ids as KoSIT.
