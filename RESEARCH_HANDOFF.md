# Research handoff — FX3 connectivity

Snapshot of the `qt-port/research` branch as of 2026-10-10, written when the goal pivoted from
a Qt port of the eval GUI to **integrating FX3 connectivity into a Python application**.
Start here; follow links only as needed.

## What exists

| Artifact | What it is | Status |
|---|---|---|
| [QT_PORT_DESIGN.md](QT_PORT_DESIGN.md) | Licensing audit, feature inventory, Qt/C++ architecture with pluggable IMU descriptors, binary-analysis method (Part 6) | Design only. Parts 0, 1, 6 remain useful regardless of language |
| [tools/cyusb-shim/](tools/cyusb-shim) | Replaces `CyUSB` / `FX3USB` / `Microsoft.VisualBasic` so ADI's closed .NET stack runs on macOS/Linux under Mono over libusb, tracing every USB transfer | `FX3Connection` constructs and enumerates on macOS ARM64. **Never run against hardware** |
| [tools/cyusb-shim/RECOVERED_PROTOCOL.md](tools/cyusb-shim/RECOVERED_PROTOCOL.md) | 42 vendor opcodes, endpoints, all enums, device product strings, assembly identities, upstream build provenance | Verified twice against `FX3Api.dll` 2.9.5.0 |

Upstream (`analogdevicesinc/iSensor-FX3-Eval`) is configured as the `upstream` remote; `master`
mirrors it and was in sync at `42d7b95` / `v2.9.5.11-pub`.

## Facts that constrain any approach

- All ADI/Cypress DLLs are managed .NET. Only `CyUSB.dll` (Cypress, proprietary, Windows driver)
  and `FX3USB.dll` touch Win32. Everything else is portable as-is.
- `adisApi.dll` / `adisInterface.dll` are closed-source ADI binaries (no public source). They hold
  the DUT register-access logic (paging, 32-bit word order, automotive SPI CRC, ADcmXL framing).
- **Recovered so far: opcodes, not payloads.** We know *which* vendor command does what, but not
  the byte layout of each request/response (SPI config struct, stream setup, pin commands). Those
  live in the MIT firmware/API source upstream (ADI `iSensor-FX3-API`) or must be observed in traces.
- A factory-blank board needs flash/EEPROM programming the shim does not implement — program once
  on Windows.

## Python integration — options as understood at handoff (undecided)

1. **pythonnet + existing `FX3ApiWrapper`, on Windows.** Already supported upstream —
   [src_wrapper/examples/Python/](src_wrapper/examples/Python). Zero new code; Windows-only;
   depends on Cypress driver and closed ADI DLLs.
2. **pythonnet on Mono + the cyusb-shim, on macOS/Linux.** Same Python API as (1), cross-platform.
   Unverified on two counts: pythonnet-on-Mono with this stack, and the shim against hardware.
   Still ships closed ADI binaries plus an unsigned CyUSB lookalike — fine for internal tooling,
   questionable for distribution.
3. **Native Python over pyusb/libusb**, built from RECOVERED_PROTOCOL.md. No .NET, no closed
   binaries, cleanest long-term. Biggest work item: recovering per-command payload formats and
   reimplementing the `adisInterface` DUT logic for the parts actually needed. Option 2's tracer is
   the fastest way to get those payloads.

Options 2 and 3 compose: run 2 to capture golden traces, then build 3 and diff against them.

## First things to check when resuming

- Which DUT(s) and which features (register R/W only? burst streaming? GPIO/PWM?) the Python app
  needs — this decides how much of `adisInterface` must be reproduced.
- Target OS(es) for the Python app — Windows-only collapses the problem to option 1.
- Whether a board is available for hardware tracing.
