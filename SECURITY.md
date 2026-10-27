# Security policy

## Reporting a vulnerability

Please do not open a public issue for security problems. Report them privately through
[GitHub private vulnerability reporting](https://github.com/ageledger/ageledger/security/advisories/new).

Include what you found, how to reproduce it, what an attacker could do with it and the package version
(and platform and OS version, if the issue is in the Android or iOS wrapper). We aim to reply within 7 days.
Ageledger is maintained part-time, so fixes may take longer; we will keep you updated and credit you in the
release notes unless you prefer not to be named.

## Scope

Ageledger wraps the platform age-signal APIs and keeps a minimal local decision log. Issues of particular interest:

- personal data reaching the decision log beyond its five fields (for example through a gap in validation);
- log file handling: path or temporary-file problems, oversized or hostile input;
- platform exception text or identifiers leaking into the log, results or messages;
- the iOS or Android wrapper presenting system UI unexpectedly;
- the package containing machine paths or unintended files.

## Out of scope

- Problems in Google Play Age Signals, Microsoft's Android binding or Apple's DeclaredAgeRange: report them to those vendors, since Ageledger only wraps them.
- An app's own decision policy, or whether an app meets any law.

## Supported versions

Only the latest release receives security fixes (before the first release: the `main` branch).
