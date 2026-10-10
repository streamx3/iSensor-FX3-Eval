# Research — EVAL-ADIS-FX3 connectivity outside Windows

Analysis of the EVAL-ADIS-FX3 evaluation software and its USB stack, done to make the board usable
from other platforms and languages. Started as a Qt/C++ port (2025-07); now feeding a Python
integration (2026-10). Lives on branch `qt-port/research`; `master` stays a clean mirror of upstream.

## Reading order

1. **[HANDOFF.md](HANDOFF.md)** — current state, observed hardware, options for Python. Start here.
2. **[RECOVERED_PROTOCOL.md](RECOVERED_PROTOCOL.md)** — USB-level facts: opcodes, endpoints, enums, device IDs.
3. **[LEGACY_APP_BEHAVIOUR.md](LEGACY_APP_BEHAVIOUR.md)** — app-level facts: file formats, connect flow, burst rules, logging.
4. [QT_PORT_DESIGN.md](QT_PORT_DESIGN.md) — licensing audit (Part 0), feature inventory (Part 1),
   C++ architecture (Parts 2–5), binary-analysis method (Part 6).
5. [METHODOLOGY.md](METHODOLOGY.md) — timeline, environment, how each fact was obtained, dead ends.
6. [SOURCES.md](SOURCES.md) — every external reference, incl. where the API source can still be found.

## Contents

| Path | |
|---|---|
| `HANDOFF.md` | State of play and next decisions |
| `RECOVERED_PROTOCOL.md` | Interop constants recovered from the binaries |
| `LEGACY_APP_BEHAVIOUR.md` | Behaviour of the VB.NET app, with source line references |
| `QT_PORT_DESIGN.md` | Original design document |
| `METHODOLOGY.md` | Reproducibility record |
| `SOURCES.md` | External references |
| `screenshots/` | Official GUI on real hardware (ADIS16507-2), 2026-10-10 |
| `decompile.sh` | Regenerates decompiler listings into `decompiled/` |
| [`../tools/cyusb-shim/`](../tools/cyusb-shim) | Working code: libusb-backed CyUSB replacement + USB tracer |

## What is deliberately not committed

| Item | Why | How to get it |
|---|---|---|
| **Decompiler listings** of `FX3Api`, `FX3USB`, `adisApi`, `adisInterface`, `RegMapClasses`, `SignalProcessing` | `adisApi` / `adisInterface` are ADI closed-source; publishing reconstructed source in a public repo is redistribution ADI hasn't granted. Keeping all listings out also keeps a native reimplementation's provenance clean: facts are recorded here as tables, code is never copied. | `research/decompile.sh` → `research/decompiled/` (git-ignored), with input hashes in `MANIFEST.txt`. Deterministic for a given ILSpy version (10.1.1.8388 used) |
| `CyUSB.dll` listing | Never produced. Cypress licence §1.2 forbids it; not needed | — |
| Shim signing keys (`*.snk`) | Derived data; public halves only | `tools/cyusb-shim/build.sh` extracts them |
| Ad-hoc USB traces | Noise | `tools/cyusb-shim/run.sh --trace …`; hardware captures meant as reference go in `tools/cyusb-shim/traces/golden/` and **are** committed |
| `resources/FX3Api.{BuildDate,CurrentBranch,CurrentCommit,CurrentURL}.txt`, `FX3Api.Resources.resources` | Accidental ILSpy output from 2025-07. Contents recorded in RECOVERED_PROTOCOL.md (provenance row) | Safe to delete |

## Status at a glance (2026-10-10)

| | |
|---|---|
| Protocol constants | ✅ recovered, verified twice |
| Per-command payload formats | ❌ not yet — see HANDOFF.md for sources |
| ADI stack on macOS via shim | ✅ loads, constructs, enumerates (no board attached) |
| Shim against hardware | ❌ untested; `DownloadFw` enum bug fixed blind |
| Hardware available | ✅ Windows + official GUI + ADIS16507-2 |
| Python approach | ⏳ undecided |
