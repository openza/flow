# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.2.0] - 2025-12-25

### Added
- GitHub OAuth Device Flow for secure authentication
- Organization filter to scope PR views by org
- Starlight documentation site

### Fixed
- Race condition in SelectedOrgNotifier async initialization causing duplicate API calls
- OAuthService cancellation race condition when restarting device flow quickly

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
- AppImage and Flatpak packaging
