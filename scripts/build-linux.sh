#!/usr/bin/env bash
#
# Build EcoFlow UPS Monitor for Linux.
#
# Usage:
#   ./scripts/build-linux.sh [VERSION] [RID]
#
# Examples:
#   ./scripts/build-linux.sh                     # dev build, host arch
#   ./scripts/build-linux.sh 1.2.3               # stamp version 1.2.3
#   ./scripts/build-linux.sh 1.2.3 linux-x64     # explicit x64
#   ./scripts/build-linux.sh 1.2.3 linux-arm64   # arm64 (cross-build)
#
# Requires: .NET 10 SDK on PATH.
# Runtime requires: BlueZ (`bluetoothd`), user in `bluetooth` group.
#
# Output:
#   dist/EcoFlowMonitor-v<VERSION>-<RID>.tar.gz
#   publish/<RID>/EcoFlowMonitor.App                (self-contained single file)
set -euo pipefail

REPO_ROOT=$(cd "$(dirname "$0")/.." && pwd)
cd "$REPO_ROOT"

VERSION="${1:-0.0.0-local}"
if [[ -z "${2:-}" ]]; then
  HOST_ARCH=$(uname -m)
  case "$HOST_ARCH" in
    x86_64)   RID="linux-x64"   ;;
    aarch64)  RID="linux-arm64" ;;
    *)        echo "Unknown host arch: $HOST_ARCH"; exit 1 ;;
  esac
else
  RID="$2"
fi

echo "==> Building EcoFlow UPS Monitor $VERSION for $RID"
echo "    repo: $REPO_ROOT"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "ERROR: 'dotnet' not found on PATH. Install the .NET 10 SDK first." >&2
  exit 1
fi

TFM="net10.0"
APP_CSPROJ="src/EcoFlowMonitor.App/EcoFlowMonitor.App.csproj"
OUT_DIR="publish/$RID"

echo "==> Publishing self-contained single-file..."
dotnet publish "$APP_CSPROJ" \
  -c Release \
  -f "$TFM" \
  -r "$RID" \
  --self-contained true \
  -p:Version="$VERSION" \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=embedded \
  -o "$OUT_DIR"

# Make sure the entrypoint is executable (tarball preserves this)
chmod +x "$OUT_DIR/EcoFlowMonitor.App" 2>/dev/null || true

mkdir -p dist
OUT="dist/EcoFlowMonitor-v${VERSION}-${RID}.tar.gz"
rm -f "$OUT"
tar -C "$OUT_DIR" -czf "$OUT" .

echo
echo "==> Done."
echo "    Publish dir : $OUT_DIR/"
echo "    Archive     : $OUT ($(du -sh "$OUT" | cut -f1))"
echo
echo "Run it with:"
echo "    $OUT_DIR/EcoFlowMonitor.App"
echo
echo "If BLE discovery fails:"
echo "    sudo systemctl status bluetooth           # daemon up?"
echo "    groups | grep -q bluetooth || sudo usermod -aG bluetooth \$USER && echo 'log out/in'"
