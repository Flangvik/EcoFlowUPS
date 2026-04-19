#!/usr/bin/env bash
#
# Build EcoFlow UPS Monitor for macOS (Apple Silicon or Intel).
#
# Usage:
#   ./scripts/build-macos.sh [VERSION] [RID]
#
# Examples:
#   ./scripts/build-macos.sh                       # dev build for host arch
#   ./scripts/build-macos.sh 1.2.3                 # stamp version 1.2.3
#   ./scripts/build-macos.sh 1.2.3 osx-arm64       # explicit Apple Silicon
#   ./scripts/build-macos.sh 1.2.3 osx-x64         # Intel (cross-build from arm64)
#
# Requires: .NET 10 SDK + `macos` workload + Xcode matching the workload's
# required SDK version.
#
# Output:
#   dist/EcoFlowMonitor-v<VERSION>-<RID>.zip    (ready to ship)
#   src/EcoFlowMonitor.App/bin/Release/net10.0-macos/<RID>/EcoFlowMonitor.App.app
set -euo pipefail

REPO_ROOT=$(cd "$(dirname "$0")/.." && pwd)
cd "$REPO_ROOT"

VERSION="${1:-0.0.0-local}"
# Default RID to host arch; allow override
if [[ -z "${2:-}" ]]; then
  HOST_ARCH=$(uname -m)
  case "$HOST_ARCH" in
    arm64)  RID="osx-arm64" ;;
    x86_64) RID="osx-x64"   ;;
    *)      echo "Unknown host arch: $HOST_ARCH"; exit 1 ;;
  esac
else
  RID="$2"
fi

echo "==> Building EcoFlow UPS Monitor $VERSION for $RID"
echo "    repo: $REPO_ROOT"

if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "ERROR: this script must run on macOS (uname reports '$(uname -s)')." >&2
  exit 1
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "ERROR: 'dotnet' not found on PATH. Install the .NET 10 SDK first." >&2
  exit 1
fi

# Ensure the macos workload is installed. `dotnet workload install` is idempotent.
if ! dotnet workload list 2>/dev/null | grep -q '^macos '; then
  echo "==> Installing 'macos' workload (one-time)..."
  dotnet workload install macos
fi

TFM="net10.0-macos"
APP_CSPROJ="src/EcoFlowMonitor.App/EcoFlowMonitor.App.csproj"
ENT="src/EcoFlowMonitor.App/Entitlements.plist"
APP_DIR="src/EcoFlowMonitor.App/bin/Release/$TFM/$RID"

echo "==> Publishing..."
# NOTE: do NOT pass `-o` on macOS — it rewrites the publish to a .pkg installer
# and hides the .app bundle. LinkMode=None keeps reflection-loaded platform
# assemblies intact; CreatePackage=false skips the .pkg installer step.
dotnet publish "$APP_CSPROJ" \
  -c Release \
  -f "$TFM" \
  -r "$RID" \
  --self-contained true \
  -p:Version="$VERSION" \
  -p:ApplicationDisplayVersion="$VERSION" \
  -p:ApplicationVersion=1 \
  -p:DebugType=embedded \
  -p:CreatePackage=false \
  -p:LinkMode=None

APP=$(ls -d "$APP_DIR"/*.app 2>/dev/null | head -n1 || true)
if [[ -z "$APP" ]]; then
  echo "ERROR: no .app bundle found in $APP_DIR" >&2
  ls -la "$APP_DIR" >&2 || true
  exit 1
fi

# --- Re-sign ad-hoc with entitlements ---
# Without disable-library-validation, macOS 15+ kernel refuses to map the
# bundled dylibs (SIGKILL / CODESIGNING / Invalid Page) because the .NET SDK
# signs the main executable BEFORE bundle packaging modifies the dylibs.
echo "==> Re-signing ad-hoc with hardened runtime + library validation entitlement..."
codesign --force --sign - --entitlements "$ENT" --timestamp=none -o runtime \
  "$APP/Contents/MacOS/EcoFlowMonitor.App"

find "$APP" -type f \( -name "*.dylib" -o -name "*.so" \) -print0 \
  | while IFS= read -r -d '' f; do
      codesign --force --sign - --timestamp=none "$f"
    done

codesign --force --sign - --entitlements "$ENT" --timestamp=none "$APP"
codesign --verify --verbose=2 "$APP"

# --- Package ---
mkdir -p dist
OUT="dist/EcoFlowMonitor-v${VERSION}-${RID}.zip"
rm -f "$OUT"
(cd "$(dirname "$APP")" && ditto -c -k --keepParent "$(basename "$APP")" "$REPO_ROOT/$OUT")

echo
echo "==> Done."
echo "    App bundle : $APP"
echo "    Archive    : $OUT ($(du -sh "$OUT" | cut -f1))"
echo
echo "Run it with:"
echo "    open '$APP'"
