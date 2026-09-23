# Security policy

Please report vulnerabilities privately through GitHub's
[private vulnerability reporting](https://github.com/balsoft-gmbh/Balsoft.Hive.EInvoice/security/advisories/new),
not in a public issue. You will get an answer within five working days.

Released packages are author-signed with the code signing certificate of Balsoft GmbH
(SHA-256 fingerprint `B761F905A340CD6FC0FB3DB1BB06E768E199A4E2B5919ACDB273DE474530AFD5`).
The public certificate is in [`eng/BalsoftGmbH.cer`](eng/BalsoftGmbH.cer).
Verify a package with `dotnet nuget verify --all <package>.nupkg`.
