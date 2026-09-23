# Contributing

Issues and pull requests are welcome.

- **Bugs in generated documents:** attach the invoice XML (with personal data removed) and
  the validator report that rejects it (KoSIT, Mustang, or your receiver's portal).
- **Every change keeps the samples valid.** Run the full suite with the external validators
  before opening a pull request:

  ```
  bash eng/get-validators.sh
  export HIVE_VALIDATORS=$PWD/.tools/validators
  dotnet test
  ```

- **New rules** go into `InvoiceValidator` with the official rule identifier, and get a row
  in `tests/Hive.EInvoice.Tests/NegativeCases.cs` that proves KoSIT reports the same id.
- **Code lists** are never edited by hand: rerun `eng/codelists/extract_codelists.py`
  against the current KoSIT configuration.
- Business terms keep their EN 16931 names and numbers in code and documentation.

By contributing you agree that your contribution is licensed under the Apache License 2.0.
