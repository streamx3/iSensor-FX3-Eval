#!/usr/bin/env bash
# Drive the legacy FX3 stack under Mono against the libusb shims, capturing a trace.
#
#   ./run.sh --trace traces/session-01.jsonl [--probe]
#
# Without --probe this runs the TraceHarness; --probe runs a minimal
# construct-and-enumerate smoke test instead.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
STAGE="$HERE/stage"
TRACE=""
MODE="harness"

while [ $# -gt 0 ]; do
  case "$1" in
    --trace) TRACE="$2"; shift 2 ;;
    --probe) MODE="probe"; shift ;;
    *) echo "unknown option: $1" >&2; exit 2 ;;
  esac
done

[ -d "$STAGE" ] || { echo "error: stage/ missing — run ./build.sh first" >&2; exit 1; }

if [ -n "$TRACE" ]; then
  case "$TRACE" in /*) ;; *) TRACE="$HERE/$TRACE" ;; esac
  mkdir -p "$(dirname "$TRACE")"
  export CYUSB_TRACE="$TRACE"
  echo "tracing to $TRACE"
fi

# libusb lives outside Mono's default search path on macOS.
export DYLD_LIBRARY_PATH="${DYLD_LIBRARY_PATH:-}:/opt/homebrew/lib:/usr/local/lib"
export LD_LIBRARY_PATH="${LD_LIBRARY_PATH:-}:/usr/lib/x86_64-linux-gnu:/usr/local/lib"
export MONO_PATH="$STAGE"

cd "$STAGE"

if [ "$MODE" = "probe" ]; then
  [ -f probe.exe ] || {
    cat > probe.cs <<'EOF'
using System; using System.Reflection;
class Probe {
  static int Main() {
    try {
      var asm = Assembly.LoadFrom("FX3Api.dll");
      var t   = asm.GetType("FX3Api.FX3Connection");
      var dt  = asm.GetType("FX3Api.DeviceType");
      string fw = System.IO.Path.GetFullPath("fw") + "/";
      object c = Activator.CreateInstance(t, new object[]{ fw, fw, fw, Enum.Parse(dt, "IMU") });
      Console.WriteLine("FX3Connection constructed");
      foreach (var prop in new[]{"AvailableFX3s","BusyFX3s"}) {
        int n = 0;
        foreach (var x in (System.Collections.IEnumerable)t.GetProperty(prop).GetValue(c, null)) {
          Console.WriteLine($"  {prop}: {x}"); n++;
        }
        Console.WriteLine($"{prop} count = {n}");
      }
      return 0;
    } catch (Exception ex) {
      Console.WriteLine("FAIL: " + ex.GetType().Name + ": " + ex.Message);
      for (var e = ex.InnerException; e != null; e = e.InnerException)
        Console.WriteLine("  inner: " + e.GetType().Name + ": " + e.Message);
      return 1;
    }
  }
}
EOF
    mcs -out:probe.exe -r:System.dll probe.cs
  }
  exec mono probe.exe
fi

[ -f TraceHarness.exe ] || { echo "error: TraceHarness.exe not staged (not yet implemented) — use --probe" >&2; exit 1; }
exec mono TraceHarness.exe
