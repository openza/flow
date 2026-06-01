# Openza Flow Agent Notes

Also follow the shared Openza guidance in `../AGENTS.md`. Keep this file limited to Flow-specific constraints and commands.

Openza Flow is a Windows-first GitHub pull-request dashboard.

## Technical Defaults

- .NET 10 LTS
- Windows App SDK 2.0.x
- WinUI 3
- CommunityToolkit.Mvvm
- MSIX-first packaging

## Project Structure

- `src/Openza.Flow/` holds the WinUI app shell, pages, controls, Windows notifications, tray/startup behavior, and app settings.
- `src/Openza.Flow.Core/` holds GitHub auth, API/query logic, caching, background refresh, and testable business logic.
- `src/Openza.Flow.Tests/` holds auth, query, cache, mapping, and refresh tests.
- Keep legacy Flutter artifacts only as history/reference; do not reintroduce active Flutter CI unless explicitly requested.

## Product Constraints

- Preserve the review-request, created PR, recently reviewed, recently created, organization filter, search, pagination, refresh, and browser-open workflows.
- Keep GitHub OAuth/PAT credentials in Windows secure storage; do not commit tokens, package outputs, certificates, or Store-private data.
- Keep tray/background/startup behavior opt-in.
- No telemetry or analytics.

## Security And Public Hygiene

- This is a public open-source repo. Do not commit tokens, cached GitHub responses with private organization data, local databases, package outputs, certificates, Store-private metadata, logs, or screenshots containing private PR data.
- Treat OAuth client IDs as public metadata only when intentionally documented; never treat PATs or refresh tokens as public.
- Keep GitHub API tests fixture-based or sanitized. Do not record private organization names, PR titles, avatars, or user data in committed fixtures.
- Run `gitleaks detect --source . --verbose` before commit-readiness, PRs, or public-release checks.

## Verification

- Run `dotnet restore Openza.Flow.slnx`.
- Run `dotnet test src/Openza.Flow.Tests/Openza.Flow.Tests.csproj -c Release --no-restore`.
- Run `dotnet build src/Openza.Flow/Openza.Flow.csproj -c Release --no-restore`.
