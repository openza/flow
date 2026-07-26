# Openza Flow

Openza Flow is a local-first Windows companion for developer workflows, built with WinUI 3. It brings agent-session history from Windows and WSL into one place and keeps useful GitHub activity close without requiring GitHub sign-in for its local features.

Openza Flow is Windows-first and packaged for distribution through the Microsoft Store. The earlier Flutter implementation is preserved in the `legacy/flutter` branch and `legacy-flutter-v0.2.0` tag for historical context, but the active app in this repository is the WinUI 3 version.

## Features

- Unified, searchable history of interactive Codex sessions from native Windows and detected WSL distributions
- Session grouping by date or repository/folder, environment filtering, and short on-demand conversation previews
- Safe resume in Windows Terminal using the original Windows or WSL environment and working directory
- A local-first Home dashboard with recent agent sessions and environment status
- Optional GitHub OAuth device-flow sign-in with a Personal Access Token fallback
- Review requests, created pull requests, recently reviewed and recently created history, organization filtering, search, pagination, and refresh
- Read-only release and GitHub Actions feeds across the first 50 repositories in the selected organization
- Secure token storage through Windows Credential Locker
- Local JSON cache for default first-page data so the dashboard can show useful state quickly
- Optional background mode with tray icon and Windows toast notifications for new review requests
- Optional packaged startup task for launching with Windows
- No telemetry or analytics

## Requirements

- Windows 10 22H2 or newer, or Windows 11
- x64 PC
- For Agent Sessions: a working OpenAI Codex CLI installation on Windows, in WSL, or both
- For session resume: Windows Terminal
- Optional: WSL 2 for Linux-hosted Codex sessions
- Optional: a GitHub account for GitHub Activity
- Visual Studio 2026 Community or newer with Windows App SDK / WinUI workload for packaging and debugging
- Windows SDK 10.0.26100 or newer
- .NET 10 SDK

Flow does not install or configure Codex, Windows Terminal, WSL, or GitHub. If Codex is not detected, the rest of the app remains usable.

## Build

```powershell
dotnet restore src\Openza.Flow.Tests\Openza.Flow.Tests.csproj
dotnet test src\Openza.Flow.Tests\Openza.Flow.Tests.csproj -c Release --no-restore
dotnet restore src\Openza.Flow\Openza.Flow.csproj
dotnet build src\Openza.Flow\Openza.Flow.csproj -c Release --no-restore
```

For local packaged debugging in Visual Studio, set the startup project to `Openza.Flow`, use the `Package` profile, and enable Deploy for the project in Configuration Manager.

## Store Packaging

The release package is associated with the Microsoft Store identity `Openza.OpenzaFlow`. Release builds use the Store identity and publisher, while Debug builds use the separate `Openza.OpenzaFlow.Dev` identity for local development. Create signed `.msixupload` submissions from Visual Studio using the Microsoft Store packaging wizard.

Do not commit generated packages, certificates, `.msixupload` files, Visual Studio user files, or Partner Center private data.

## GitHub Authentication

GitHub sign-in is optional and is not required for Home, Agent Sessions, or Settings. When connected, Openza Flow uses GitHub OAuth device flow by default. The OAuth client ID is public application metadata, not a secret. PAT sign-in is kept as a fallback for users who prefer manually scoped credentials. Classic tokens need `repo` or `public_repo`, plus `read:user` and `read:org`, for the app's read-only pull request, release, and Actions views.

Tokens are stored in Windows Credential Locker. The WinUI migration intentionally does not migrate credentials or cache from the legacy Flutter app; users sign in again.

## Documentation

User guide: [solanky.dev/openza/flow](https://solanky.dev/openza/flow/)

## Privacy

Openza Flow does not send telemetry, analytics, crash reports, agent-session content, or GitHub data to Openza. Agent-session discovery happens locally through the installed Codex CLI, and optional GitHub requests go directly to GitHub. See [PRIVACY.md](PRIVACY.md).

## Third-party services

Openza Flow is an independent open-source project. It is not affiliated with or endorsed by OpenAI, GitHub, or Microsoft. Codex, GitHub, Windows, Windows Terminal, and WSL remain products and services of their respective owners.

## License

The source code and documentation are available under the [MIT License](LICENSE). Openza names, logos, and official app icons are reserved brand assets; see [BRAND.md](BRAND.md). Third-party dependencies remain subject to their respective licenses; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Author

**Deependra Solanky**

- GitHub: [@solankydev](https://github.com/solankydev)
- Email: deependra@solanky.dev
