# Third-party notices

Openza Flow depends on third-party packages and platform components. This file summarizes direct dependencies used by the repository; package metadata and distributed license files remain the source of truth for each dependency.

## Runtime and app dependencies

| Component | Version | License metadata | Purpose |
| --- | --- | --- | --- |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | MVVM helpers |
| Microsoft.WindowsAppSDK | 2.0.1 | NuGet package file `license.txt` | WinUI 3, Windows notifications, and Windows App SDK runtime |

## Test dependencies

| Component | Version | License metadata | Purpose |
| --- | --- | --- | --- |
| xUnit.net | 2.9.3 | Apache-2.0 | Unit testing |
| xUnit.net Visual Studio runner | 3.1.4 | Apache-2.0 | Test discovery and execution |
| Microsoft.NET.Test.Sdk | 17.14.1 | MIT | Test SDK |
| coverlet.collector | 6.0.4 | MIT | Test coverage collection |

## Project-owned assets

Project-created screenshots are available under the repository's MIT License. Openza names, logos, and official app icons are reserved as described in [BRAND.md](BRAND.md).

## External integrations

Openza Flow interoperates with software and services that are not bundled with the app:

| Product or service | Provider | Use |
| --- | --- | --- |
| Codex CLI | OpenAI | Local agent-session discovery, preview, and resume |
| GitHub | GitHub, Inc. | Optional authentication and read-only developer activity |
| Windows Terminal | Microsoft | Launching resumed sessions |
| Windows Subsystem for Linux | Microsoft | Discovering and resuming Linux-hosted sessions |

These products and services remain subject to their providers' licenses, terms, privacy policies, and trademark rules. Openza Flow is an independent open-source project and is not affiliated with or endorsed by OpenAI, GitHub, or Microsoft.
