# Openza Flow

A GitHub PR review inbox for your desktop. Stay on top of pull requests that need your attention.

## Features

- View PRs awaiting your review
- See PRs you've created
- Desktop notifications for new review requests
- Quick access to open PRs in browser
- Auto-refresh to stay updated

## Download

### Linux

Download the latest `.AppImage` from [Releases](https://github.com/openza/flow/releases).

```bash
# Make executable and run
chmod +x Flow-*.AppImage
./Flow-*.AppImage
```

## Setup

1. Launch Openza Flow
2. Enter your GitHub Personal Access Token (PAT)
   - Create one at [GitHub Settings > Developer settings > Personal access tokens](https://github.com/settings/tokens)
   - Required scopes: `repo`, `read:user`
3. Start reviewing PRs

## Building from Source

### Requirements

- Flutter SDK 3.10.3 or later
- Linux development dependencies

### Build

```bash
git clone https://github.com/openza/flow.git
cd flow

# Install dependencies
flutter pub get

# Run in development
flutter run -d linux

# Build release
flutter build linux --release
```

### Build AppImage

```bash
flutter build linux --release
chmod +x scripts/build-appimage.sh
./scripts/build-appimage.sh
```

## License

MIT License - see [LICENSE](LICENSE) for details.

## Author

**Deependra Solanky**
- GitHub: [@solankydev](https://github.com/solankydev)
- Email: deependra@solanky.dev
