# Security Policy

## Supported Versions

Security fixes target the current Windows-native version of Openza Flow on `main`.

The legacy Flutter implementation is preserved for history and old releases, but it is not actively maintained.

## Reporting a Vulnerability

Please do not open a public issue for sensitive security reports.

Email Deependra Solanky at deependra@solanky.dev with:

- A clear description of the issue
- Steps to reproduce, if possible
- Impact and affected versions
- Any suggested fix or mitigation

## Credential Handling

Openza Flow stores GitHub tokens in Windows Credential Locker. Tokens, certificates, package outputs, and Partner Center private data must never be committed to the repository.
