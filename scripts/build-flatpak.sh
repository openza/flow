#!/bin/bash
set -e

# Openza Flow Flatpak Build Script
# This script builds a Flatpak bundle from the Flutter Linux build

APP_ID="com.openza.flow"
VERSION=$(grep 'version:' pubspec.yaml | head -1 | sed 's/version: //' | sed 's/+.*//')

echo "Building Flatpak for Openza Flow v$VERSION..."

# Ensure Flutter build exists
if [ ! -d "build/linux/x64/release/bundle" ]; then
    echo "Error: Flutter build not found. Run 'flutter build linux --release' first."
    exit 1
fi

# Create flatpak desktop file with correct icon path
mkdir -p flatpak/build
cat > flatpak/build/com.openza.flow.desktop << EOF
[Desktop Entry]
Name=Openza Flow
Comment=GitHub PR Review Inbox
Exec=flow
Icon=com.openza.flow
Type=Application
Categories=Development;
Terminal=false
StartupWMClass=flow
EOF

# Build the flatpak
cd flatpak
flatpak-builder --force-clean --repo=repo build-dir com.openza.flow.yml

# Create bundle
flatpak build-bundle repo "../Flow-$VERSION.flatpak" "$APP_ID"

echo "Flatpak bundle created: Flow-$VERSION.flatpak"
