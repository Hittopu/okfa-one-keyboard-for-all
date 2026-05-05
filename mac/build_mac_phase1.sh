#!/bin/zsh
set -euo pipefail
setopt null_glob globstarshort

ROOT_DIR="$(cd "$(dirname "$0")" && pwd)"
BUILD_DIR="$ROOT_DIR/build"
IDENTITY_FILE="$ROOT_DIR/app_identity.sh"
if [[ -f "$IDENTITY_FILE" ]]; then
  source "$IDENTITY_FILE"
fi

APP_NAME="${APP_NAME:-okfa}"
APP_DISPLAY_NAME="${APP_DISPLAY_NAME:-$APP_NAME}"
APP_BUNDLE_ID="${APP_BUNDLE_ID:-com.keyboardforall.${APP_NAME}.mac}"
APP_BLE_NAME="${APP_BLE_NAME:-$APP_NAME}"
APP_LOG_NAME="${APP_LOG_NAME:-${APP_NAME}.log}"
APP_LOG_PREFIX="${APP_LOG_PREFIX:-$APP_NAME}"
APP_DIR="$BUILD_DIR/$APP_NAME.app"
BIN_DIR="$APP_DIR/Contents/MacOS"
RES_DIR="$APP_DIR/Contents/Resources"
SOURCE_RES_DIR="$ROOT_DIR/Resources"
GENERATED_SWIFT="$BUILD_DIR/AppIdentity.generated.swift"
SWIFT_FILES=("$ROOT_DIR"/Sources/**/*.swift(.N))

if (( ${#SWIFT_FILES[@]} == 0 )); then
  echo "No Swift source files found under $ROOT_DIR/Sources" >&2
  exit 1
fi

rm -rf "$APP_DIR"
mkdir -p "$BIN_DIR" "$RES_DIR"

if [[ -d "$SOURCE_RES_DIR" ]]; then
  ditto "$SOURCE_RES_DIR" "$RES_DIR"
fi

cat > "$GENERATED_SWIFT" <<EOF
import Foundation

enum AppIdentity {
    static let appName = "${APP_NAME}"
    static let displayName = "${APP_DISPLAY_NAME}"
    static let bundleID = "${APP_BUNDLE_ID}"
    static let bleLocalName = "${APP_BLE_NAME}"
    static let logFileName = "${APP_LOG_NAME}"
    static let logPrefix = "${APP_LOG_PREFIX}"
}
EOF

swiftc \
  -framework AppKit \
  -framework CoreBluetooth \
  -framework ApplicationServices \
  "$GENERATED_SWIFT" \
  "${SWIFT_FILES[@]}" \
  -o "$BIN_DIR/$APP_NAME"

sed \
  -e "s#__APP_BUNDLE_ID__#$APP_BUNDLE_ID#g" \
  -e "s#__APP_NAME__#$APP_NAME#g" \
  -e "s#__APP_DISPLAY_NAME__#$APP_DISPLAY_NAME#g" \
  "$ROOT_DIR/Info.plist" > "$APP_DIR/Contents/Info.plist"

if [[ -n "${CODESIGN_IDENTITY:-}" ]]; then
  codesign --force --deep --sign "$CODESIGN_IDENTITY" --identifier "$APP_BUNDLE_ID" "$APP_DIR" >/dev/null
  echo "Signed app bundle with identity: $CODESIGN_IDENTITY"
else
  codesign --force --deep --sign - --identifier "$APP_BUNDLE_ID" "$APP_DIR" >/dev/null
  echo "Signed app bundle with ad-hoc identity for local development."
fi

echo "Built app bundle at:"
echo "$APP_DIR"
echo
echo "Open it with:"
echo "open \"$APP_DIR\""
