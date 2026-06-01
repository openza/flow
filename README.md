# Openza Flow

Openza Flow is a Windows-native GitHub pull request dashboard built with WinUI 3. It keeps review requests, created pull requests, recently reviewed work, and organization filters in one focused desktop app.

V1 is Windows-first and Store-ready. The earlier Flutter implementation is preserved in the `legacy/flutter` branch and `legacy-flutter-v0.2.0` tag for historical releases, but the active app in this repository is now the WinUI 3 version.

## Features

- Native Windows 10/11 interface with WinUI 3, NavigationView, CommandBar, Mica where available, and Windows theme support
- GitHub OAuth device-flow sign-in with a Personal Access Token fallback
- Review requests, created PRs, recently reviewed PRs, recently created PRs, organization filter, search, pagination, and refresh
- Secure token storage through Windows Credential Locker
- Local JSON cache for default first-page data so the dashboard can show useful state quickly
- Optional background mode with tray icon and Windows toast notifications for new review requests
- Optional packaged startup task for launching with Windows
- No telemetry or analytics

## Requirements

- Windows 10 22H2 or newer, or Windows 11
- x64 PC for the V1 package
- Visual Studio 2026 Community or newer with Windows App SDK / WinUI workload for packaging and debugging
- Windows SDK 10.0.26100 or newer
- .NET 10 SDK

## Build

```powershell
dotnet restore src\Openza.Flow.Tests\Openza.Flow.Tests.csproj
dotnet test src\Openza.Flow.Tests\Openza.Flow.Tests.csproj -c Release --no-restore
dotnet restore src\Openza.Flow\Openza.Flow.csproj
dotnet build src\Openza.Flow\Openza.Flow.csproj -c Release --no-restore
```

For local packaged debugging in Visual Studio, set the startup project to `Openza.Flow`, use the `Package` profile, and enable Deploy for the project in Configuration Manager.

## Store Packaging

The package identity target is `Openza.OpenzaFlow`. Store association, publisher identity, signing, and `.msixupload` creation are done from Visual Studio using the Microsoft Store packaging wizard.

Do not commit generated packages, certificates, `.msixupload` files, Visual Studio user files, or Partner Center private data.

## GitHub Authentication

Openza Flow uses GitHub OAuth device flow by default. The OAuth client ID is public application metadata, not a secret. PAT sign-in is kept as a fallback for users who prefer manually scoped credentials.

Tokens are stored in Windows Credential Locker. The WinUI migration intentionally does not migrate credentials or cache from the legacy Flutter app; users sign in again.

## Documentation

The docs site lives in `website/` and is built with Astro Starlight.

```powershell
cd website
pnpm install
pnpm build
```

## Privacy

Openza Flow does not collect telemetry, analytics, crash reports, or personal data for Openza. It connects directly to GitHub using the credentials you provide. See [PRIVACY.md](PRIVACY.md).

## License

MIT License - see [LICENSE](LICENSE) for details.

## Author

**Deependra Solanky**

- GitHub: [@solankydev](https://github.com/solankydev)
- Email: deependra@solanky.dev
