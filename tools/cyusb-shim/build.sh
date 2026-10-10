#!/usr/bin/env bash
# Build the CyUSB/FX3USB/Microsoft.VisualBasic shims and stage a runnable tree.
#
# Output: stage/ containing ADI's binaries with our shims layered on top.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
STAGE="$HERE/stage"

export DOTNET_ROOT="${DOTNET_ROOT:-/opt/homebrew/opt/dotnet/libexec}"

need() { command -v "$1" >/dev/null 2>&1 || { echo "error: $1 not found. brew install $2" >&2; exit 1; }; }
need dotnet dotnet
need mono mono

# --- 1. extract public keys ------------------------------------------------
# Public halves only, read from assembly metadata. Not decompilation.
extract_key() {  # <source dll> <output snk>
  python3 - "$1" "$2" <<'PY'
import sys
sig = bytes([0x00,0x24,0x00,0x00,0x04,0x80,0x00,0x00,0x94,0x00,0x00,0x00,
             0x06,0x02,0x00,0x00,0x00,0x24,0x00,0x00,0x52,0x53,0x41,0x31])
d = open(sys.argv[1],'rb').read()
i = d.find(sig)
if i < 0:
    sys.exit(f"public key blob not found in {sys.argv[1]}")
open(sys.argv[2],'wb').write(d[i:i+160])
print(f"  {sys.argv[2]}: offset {hex(i)}")
PY
}

echo "==> extracting public keys"
extract_key "$REPO/resources/CyUSB.dll" "$HERE/cyusb-public.snk"

# find exits nonzero when a search root is absent (e.g. the Linux path on macOS);
# pipefail would otherwise abort the script here.
VBREF="$( { find /opt/homebrew/Cellar/mono /usr/lib/mono /usr/local/lib/mono \
              -name Microsoft.VisualBasic.dll 2>/dev/null || true; } | head -1 )"
[ -n "$VBREF" ] || { echo "error: Mono's Microsoft.VisualBasic.dll not found" >&2; exit 1; }
extract_key "$VBREF" "$HERE/msvb-public.snk"

# --- 2. build shims --------------------------------------------------------
echo "==> building shims"
for proj in CyUSB FX3USB MicrosoftVisualBasic; do
  dotnet build "$HERE/src/$proj/$proj.csproj" -v q --nologo -c Release
done

# --- 3. stage --------------------------------------------------------------
echo "==> staging into $STAGE"
rm -rf "$STAGE"
mkdir -p "$STAGE/fw"

cp "$REPO"/resources/*.dll "$STAGE/"
cp "$REPO"/resources/*.img "$STAGE/fw/"
cp "$REPO"/regmaps/*.csv   "$STAGE/fw/../" 2>/dev/null || true

# shims last — they overwrite the originals
cp "$HERE/src/CyUSB/bin/Release/net48/CyUSB.dll"                              "$STAGE/"
cp "$HERE/src/FX3USB/bin/Release/net48/FX3USB.dll"                            "$STAGE/"
cp "$HERE/src/MicrosoftVisualBasic/bin/Release/net48/Microsoft.VisualBasic.dll" "$STAGE/"

echo
echo "staged. verify identities:"
for a in CyUSB FX3USB Microsoft.VisualBasic; do
  printf "  %-24s " "$a.dll"
  monodis --assembly "$STAGE/$a.dll" 2>/dev/null | grep -E '^Version:' || echo "?"
done
echo
echo "next: ./run.sh --trace traces/session-01.jsonl"
