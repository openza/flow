#!/bin/bash
set -e

# GitDesk Flatpak Build Script
# This script builds a Flatpak bundle from the Flutter Linux build

APP_ID="com.openza.gitdesk"
VERSION=$(grep 'version:' pubspec.yaml | head -1 | sed 's/version: //' | sed 's/+.*//')

echo "Building Flatpak for GitDesk v$VERSION..."

# Ensure Flutter build exists
if [ ! -d "build/linux/x64/release/bundle" ]; then
    echo "Error: Flutter build not found. Run 'flutter build linux --release' first."
    exit 1
fi

# Create flatpak desktop file with correct icon path
mkdir -p flatpak/build
cat > flatpak/build/com.openza.gitdesk.desktop << EOF
[Desktop Entry]
Name=GitDesk
Comment=GitHub PR Review Inbox
Exec=gitdesk
Icon=com.openza.gitdesk
Type=Application
Categories=Development;
Terminal=false
StartupWMClass=gitdesk
EOF

# Build the flatpak
cd flatpak
flatpak-builder --force-clean --repo=repo build-dir com.openza.gitdesk.yml

# Create bundle
flatpak build-bundle repo "../GitDesk-$VERSION.flatpak" "$APP_ID"

echo "Flatpak bundle created: GitDesk-$VERSION.flatpak"
