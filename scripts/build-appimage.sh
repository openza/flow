#!/bin/bash
set -e

# Openza Flow AppImage Build Script
# This script packages the Flutter Linux build into an AppImage

APP_NAME="Flow"
APP_ID="com.openza.flow"
BINARY_NAME="flow"
VERSION=$(grep 'version:' pubspec.yaml | head -1 | sed 's/version: //' | sed 's/+.*//')

echo "Building AppImage for $APP_NAME v$VERSION..."

# Create AppDir structure
APPDIR="$APP_NAME.AppDir"
rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/bin"
mkdir -p "$APPDIR/usr/share/applications"
mkdir -p "$APPDIR/usr/share/icons/hicolor/256x256/apps"

# Copy Flutter build output - keep bundle structure intact in bin/
cp -r build/linux/x64/release/bundle/* "$APPDIR/usr/bin/"

# Copy desktop file
cp linux/flow.desktop "$APPDIR/usr/share/applications/$APP_ID.desktop"
cp linux/flow.desktop "$APPDIR/$BINARY_NAME.desktop"

# Copy icon
cp assets/icon/icon.png "$APPDIR/usr/share/icons/hicolor/256x256/apps/$BINARY_NAME.png"
cp assets/icon/icon.png "$APPDIR/$BINARY_NAME.png"

# Create AppRun script
cat > "$APPDIR/AppRun" << 'EOF'
#!/bin/bash
SELF=$(readlink -f "$0")
HERE=${SELF%/*}
export PATH="${HERE}/usr/bin:${PATH}"
export LD_LIBRARY_PATH="${HERE}/usr/bin/lib:${LD_LIBRARY_PATH}"
cd "${HERE}/usr/bin"
exec "${HERE}/usr/bin/flow" "$@"
EOF
chmod +x "$APPDIR/AppRun"

# Download appimagetool if not present
if [ ! -f appimagetool-x86_64.AppImage ]; then
    echo "Downloading appimagetool..."
    wget -q "https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-x86_64.AppImage"
    chmod +x appimagetool-x86_64.AppImage
fi

# Build AppImage
ARCH=x86_64 ./appimagetool-x86_64.AppImage --appimage-extract-and-run "$APPDIR" "$APP_NAME-$VERSION-x86_64.AppImage"

echo "AppImage created: $APP_NAME-$VERSION-x86_64.AppImage"
