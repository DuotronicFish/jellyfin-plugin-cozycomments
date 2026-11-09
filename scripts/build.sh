#!/usr/bin/env bash
# Build the Cozy Comments plugin and package it as a Jellyfin plugin zip.
#
# Usage:
#   scripts/build.sh                       # -> dist/cozycomments_<version>.zip
#   scripts/build.sh --release-url URL     # also add this version to manifest.json, with the zip at URL
#                                          #   e.g. https://github.com/OWNER/jellyfin-plugin-cozycomments/releases/download/v1.2.0.0/cozycomments_1.2.0.0.zip
#   scripts/build.sh --jellyfin 12.4.0     # compile against a specific 12.x package version (default 12.0.0)
#
# This branch builds the Jellyfin 12.x plugin (2.x, targetAbi 12.0.0.0). The Jellyfin 10.11 build is 1.2.2.0 (tag v1.2.2.0).
# Needs the .NET 10 SDK (https://dotnet.microsoft.com/download). Set DOTNET=/path/to/dotnet if it isn't on PATH.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJ="$ROOT/src/Jellyfin.Plugin.CozyComments/Jellyfin.Plugin.CozyComments.csproj"
DIST="$ROOT/dist"
RELEASE_URL=""
JF_VERSION=""
CHANGELOG="See CHANGELOG.md"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --release-url) RELEASE_URL="$2"; shift 2 ;;
    --jellyfin) JF_VERSION="$2"; shift 2 ;;
    --changelog) CHANGELOG="$2"; shift 2 ;;
    -h|--help) sed -n '2,13p' "$0"; exit 0 ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done

DOTNET="${DOTNET:-$(command -v dotnet || true)}"
[[ -z "$DOTNET" && -x "$HOME/.dotnet/dotnet" ]] && DOTNET="$HOME/.dotnet/dotnet"
if [[ -z "$DOTNET" ]]; then
  echo "dotnet not found. Install the .NET 10 SDK or set DOTNET=/path/to/dotnet." >&2
  exit 1
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

VERSION=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$PROJ" | head -1)
# Lowest Jellyfin version this build loads on (it is compiled against the Jellyfin 12.0.0 packages).
TARGET_ABI="12.0.0.0"
GUID="dc9c7d6f-2de4-4ee4-9187-6a87ca998510"
STAMP=$(date -u +%Y-%m-%dT%H:%M:%SZ)
ZIP="cozycomments_${VERSION}.zip"

echo "== Building Cozy Comments $VERSION"
EXTRA=()
[[ -n "$JF_VERSION" ]] && EXTRA+=("-p:JellyfinVersion=$JF_VERSION")
rm -rf "$DIST/build" "$DIST/stage"
"$DOTNET" build "$PROJ" -c Release -o "$DIST/build" --nologo -v q "${EXTRA[@]}"

echo "== Packaging $ZIP"
mkdir -p "$DIST/stage"
cp "$DIST/build/Jellyfin.Plugin.CozyComments.dll" "$DIST/stage/"
cat > "$DIST/stage/meta.json" <<EOF
{
  "category": "General",
  "changelog": "$CHANGELOG",
  "description": "Comments, optional 1-5 star ratings, spoilers and replies on movie, series and episode pages, plus a recent-comments page.",
  "guid": "$GUID",
  "name": "Cozy Comments",
  "overview": "A comment and rating system for shared users on Jellyfin",
  "owner": "DuotronicFish",
  "targetAbi": "$TARGET_ABI",
  "timestamp": "$STAMP",
  "version": "$VERSION",
  "status": "Active",
  "autoUpdate": true,
  "imagePath": "",
  "assemblies": ["Jellyfin.Plugin.CozyComments.dll"]
}
EOF
rm -f "$DIST/$ZIP"
python3 -I - "$DIST/stage" "$DIST/$ZIP" <<'PY'
import os, sys, zipfile
src, out = sys.argv[1:3]
with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
    for name in sorted(os.listdir(src)):
        z.write(os.path.join(src, name), name)
PY
MD5=$(python3 -I -c 'import hashlib,sys;print(hashlib.md5(open(sys.argv[1],"rb").read()).hexdigest())' "$DIST/$ZIP")
echo "   $DIST/$ZIP"
echo "   md5 $MD5"

if [[ -n "$RELEASE_URL" ]]; then
  python3 -I "$ROOT/scripts/update_manifest.py" --manifest "$ROOT/manifest.json" --version "$VERSION" \
    --url "$RELEASE_URL" --checksum "$MD5" --abi "$TARGET_ABI" --timestamp "$STAMP" --changelog "$CHANGELOG"
  echo "== manifest.json updated for $VERSION"
fi
