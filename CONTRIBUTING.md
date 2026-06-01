# Contributing

Thanks for helping improve Openza Flow.

## Development Setup

Requirements:

- Windows 10 22H2 or Windows 11
- .NET 10 SDK
- Visual Studio 2026 Community or newer with the Windows App SDK / WinUI workload
- PowerShell 7+

Build and test:

```powershell
dotnet restore src\Openza.Flow.Tests\Openza.Flow.Tests.csproj
dotnet test src\Openza.Flow.Tests\Openza.Flow.Tests.csproj -c Release --no-restore
dotnet restore src\Openza.Flow\Openza.Flow.csproj
dotnet build src\Openza.Flow\Openza.Flow.csproj -c Release --no-restore
```

## Pull Requests

- Keep changes focused.
- Add or update tests when changing GitHub API logic, authentication, caching, background refresh, or notification behavior.
- Include screenshots for UI changes.
- Do not commit certificates, package outputs, access tokens, `.msixupload` files, Visual Studio user files, or Partner Center private data.

## Docs

Docs live in `website/`.

```powershell
cd website
pnpm install
pnpm build
```

## Legacy Flutter App

The legacy Flutter implementation is preserved in the `legacy/flutter` branch and `legacy-flutter-v0.2.0` tag. New development should target the WinUI 3 app.
