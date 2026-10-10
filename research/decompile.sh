#!/usr/bin/env bash
# Regenerate local decompiler listings of the ADI assemblies in resources/.
#
#   research/decompile.sh            -> research/decompiled/  (git-ignored)
#
# Listings are deliberately NOT committed (see research/README.md, "Decompiled listings"):
# two of these assemblies are ADI closed-source, and keeping reconstructed source out of the
# repo keeps a future native reimplementation's provenance clean. This script makes them
# reproducible instead; MANIFEST.txt records input hashes and tool version.
#
# Rules encoded here:
#   * CyUSB.dll is NEVER decompiled (Cypress licence §1.2) and is NOT placed next to the inputs.
#     If it were, ILSpy would resolve CyUSB enum values to *names*, hiding the integers FX3Api
#     actually compiles in — which is exactly how a wrong FX3_FWDWNLOAD_MEDIA_TYPE slipped
#     through in July 2025.
#   * Inputs are decompiled from copies in a temp dir: ILSpy writes embedded resources next to
#     its input, which once littered resources/ with FX3Api.*.txt files.
#   * sdpApi1.dll is skipped: unused by the app, and marked "ADI internal use only".
#   * AForge.Math is skipped: upstream source is public (LGPL-3.0).
#   * FX3ApiWrapper / StreamDataLogger are skipped: their source is in this repo.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$HERE/.." && pwd)"
OUT="$HERE/decompiled"

ASSEMBLIES=(FX3Api FX3USB adisApi adisInterface RegMapClasses SignalProcessing)

export DOTNET_ROOT="${DOTNET_ROOT:-/opt/homebrew/opt/dotnet/libexec}"
export PATH="$PATH:$HOME/.dotnet/tools"
command -v ilspycmd >/dev/null || {
  echo "error: ilspycmd not found. Install: dotnet tool install -g ilspycmd" >&2; exit 1; }

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
mkdir -p "$TMP/in" "$OUT"

{
  echo "Generated: $(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo "Tool:      ilspycmd $(ilspycmd --version 2>/dev/null | head -1 | sed 's/^ilspycmd: //')"
  echo "Repo:      $(git -C "$REPO" rev-parse --short HEAD 2>/dev/null || echo '?')"
  echo
  echo "Inputs (sha256):"
} > "$OUT/MANIFEST.txt"

for a in "${ASSEMBLIES[@]}"; do
  src="$REPO/resources/$a.dll"
  [ -f "$src" ] || { echo "skip: $a.dll not found"; continue; }
  cp "$src" "$TMP/in/"
  ( cd "$TMP" && shasum -a 256 "in/$a.dll" | sed "s|in/||" ) >> "$OUT/MANIFEST.txt"
done

for a in "${ASSEMBLIES[@]}"; do
  [ -f "$TMP/in/$a.dll" ] || continue
  printf '  %-18s ' "$a"
  mkdir -p "$TMP/out/$a"
  ( cd "$TMP/out/$a" && ilspycmd -o . "$TMP/in/$a.dll" >/dev/null 2>&1 ) || { echo "FAILED"; continue; }
  mv "$TMP/out/$a/$a.decompiled.cs" "$OUT/$a.decompiled.cs"
  echo "$(wc -l < "$OUT/$a.decompiled.cs") lines"
done

echo
echo "listings in $OUT (git-ignored). Manifest: $OUT/MANIFEST.txt"
