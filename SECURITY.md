# Security Policy

## Supported Versions

Security fixes target the current Windows-native version of Openza Flow on `main`.

The legacy Flutter implementation is preserved for history and old releases, but it is not actively maintained.

## Reporting a Vulnerability

Please do not open a public issue for sensitive security reports.

Use **Report a vulnerability** on the repository's Security page to open a private report. If private vulnerability reporting is unavailable, email Deependra Solanky at deependra@solanky.dev with:

- A clear description of the issue
- Steps to reproduce, if possible
- Impact and affected versions
- Any suggested fix or mitigation

## Credential Handling

Openza Flow stores GitHub tokens in Windows Credential Locker. Tokens, certificates, package outputs, and Partner Center private data must never be committed to the repository.

## Agent Session Data

Agent-session summaries and selected previews may contain private prompts, generated responses, repository names, branches, working directories, and session identifiers. Flow keeps this information in memory and must not write it to its GitHub cache, application settings, telemetry, or diagnostic logs.

Flow uses the installed Codex app-server as the history authority. It must not directly read, move, edit, or delete Codex JSONL or SQLite state files.

## External Process Boundary

Flow discovers and invokes native Codex, WSL, and Windows Terminal executables. Process execution must:

- Use `ProcessStartInfo.ArgumentList` or an equivalent structured argument API.
- Never concatenate a shell command for execution.
- Never route Codex through `.cmd` or `.bat` shims.
- Validate the owning environment and original working directory before resuming.
- Preserve Windows and Linux path semantics without implicit translation.
- Avoid adding approval, sandbox, model, profile, or bypass flags.

An unavailable or incompatible environment must fail independently without blocking other environments.

## Logging

Logs must contain only operational information needed for diagnosis, such as a sanitized error category, environment identifier, Codex version, operation, and duration.

Never log:

- Credentials, OAuth codes, or authorization headers
- Session identifiers, titles, prompts, previews, or resume commands
- Working directories, repository names, branches, or remote URLs
- GitHub response bodies or private work-item metadata

Users should be warned to review local logs before sharing them.
