# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.1.0] - Unreleased

### Added
- Local-first Home dashboard for recent agent work, environment status, and optional GitHub items needing attention.
- Unified Agent Sessions history from native Windows and Codex-enabled WSL distributions.
- Search, environment filters, date or repository/folder grouping, incremental loading, and short on-demand conversation previews.
- Safe session resume in Windows Terminal using the original environment and working directory.
- Agent-environment enablement and terminal launch preferences in Settings.

### Changed
- Repositioned Flow as a Windows developer companion with Agent Sessions as its primary local workflow.
- Made Home the default page and kept local features available without GitHub sign-in.
- Reorganized GitHub functionality under clear Pull Requests, Releases, and Workflow Runs navigation.
- Improved page loading, cancellation, responsiveness, empty states, and visual consistency across the app.
- Updated application identity assets, About information, documentation, privacy disclosures, and Store metadata.

### Fixed
- Prevented rapid GitHub navigation from leaving stale or blank release and workflow views.
- Prevented repeated full session-list reconstruction while Codex history is paginating.
- Fixed WSL discovery, environment refresh, Windows npm Codex resolution, and terminal argument safety edge cases.

## [1.0.0] - 2026-07-21

### Changed
- Published the WinUI 3 application as the first stable Microsoft Store release.
- Finalized the Store package identity, separate development identity, application icons, privacy information, and support links.

## [0.3.0] - 2026-05-09

### Changed
- Rebuilt Openza Flow as a Windows-native WinUI 3 app.
- Preserved the Flutter implementation as legacy history instead of maintaining it side by side.
- Updated documentation, CI, and open-source project files for the Windows-native app.

### Added
- GitHub OAuth device-flow sign-in and PAT fallback in the WinUI app.
- Review requests, created pull requests, recently reviewed, recently created, search, organization filter, pagination, and refresh.
- Read-only Releases and Actions pages across the first 50 recently pushed repositories in the selected organization.
- Windows Credential Locker token storage.
- Local JSON cache for first-page/default dashboard data.
- Optional background mode with tray icon and Windows toast notifications.
- Optional packaged startup task support.

## [0.2.0] - 2025-12-25

### Added
- GitHub OAuth Device Flow for secure authentication
- Organization filter to scope PR views by org
- Starlight documentation site

### Fixed
- Race condition in SelectedOrgNotifier async initialization causing duplicate API calls
- OAuthService cancellation race condition when restarting device flow quickly
- Legacy package URL opening now works correctly
- OAuth screen now displays correct app logo

### Changed
- Added AppStream metainfo for better software center integration

## [0.1.0] - 2025-12-17

### Added
- Initial release
- View PRs requiring your review
- View PRs you created
- View PRs you reviewed
- PR details with diff viewer
- Desktop notifications for new PRs
- Auto-refresh every 5 minutes
- Dark/Light theme support
- Legacy desktop packaging
