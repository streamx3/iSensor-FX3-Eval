# CyUSB shim — run the legacy FX3 stack on Linux/macOS and trace the USB protocol

Interop scaffolding for the Qt port (see [../../QT_PORT_DESIGN.md](../../research/QT_PORT_DESIGN.md), Part 6).
Recovered constants (opcodes, endpoints, enums, identities): [RECOVERED_PROTOCOL.md](../../research/RECOVERED_PROTOCOL.md).

It replaces the Windows-only USB layer under ADI's closed `FX3Api`/`adisInterface`
binaries with a **libusb-1.0** implementation, so the existing evaluation stack runs
on macOS and Linux — and logs every USB transfer it performs.

**Status: working.** `FX3Connection` constructs and enumerates on macOS ARM64 under Mono.
Hardware-attached verification is still outstanding (no FX3 board was present during bring-up).

> **This is scaffolding, not a deliverable.** It loads closed ADI binaries and must never
> ship. Its only outputs are the protocol traces in `traces/golden/`, which become the golden-file
> corpus for the C++ `ReplayTransport`.

---

## Why this exists

Three findings made it possible, all verified against the shipped binaries:

1. **Every DLL is managed .NET (CIL), none obfuscated.** No native disassembly is involved
   anywhere in this workflow.
2. **`CyUSB.dll` is the only assembly touching Win32.** `adisApi`, `adisInterface`,
   `SignalProcessing`, `FX3Api` and `RegMapClasses` import nothing but `mscoree.dll`. Replace
   the USB layer and the rest is portable as-is.
3. **The CyUSB coupling is tiny.** `FX3Api` uses 4 types and ~10 members, and drives every
   control transfer through one method that maps directly onto `libusb_control_transfer`.

## What gets replaced

| Assembly | Why | Identity that must match |
|---|---|---|
| `CyUSB.dll` | Cypress, proprietary, Windows-only | `CyUSB, 1.2.3.0, PublicKeyToken=01f1d2b6c851ae92` |
| `FX3USB.dll` | ADI (MIT), but wraps bulk I/O in Win32 `OVERLAPPED` | `FX3USB, 1.0.0.0` (not strong-named) |
| `Microsoft.VisualBasic.dll` | Mono's bottle ships a *reference* assembly only (no method bodies) | `Microsoft.VisualBasic, 10.0.0.0, PublicKeyToken=b03f5f7f11d50a3a` |

`FX3USB` is replaced rather than emulated: the original's
`CreateEvent → BeginDataXfer → WaitForSingleObject → FinishDataXfer → CloseHandle` dance exists
only to get a cancellable transfer out of the Windows driver. `libusb_bulk_transfer` is already
synchronous-with-timeout, so the whole thing collapses to one call — and the `CyUSB` shim then
needs no `OVERLAPPED`, `PInvoke` or async-endpoint surface at all.

The `Microsoft.VisualBasic` shim implements exactly the 7 members the ADI assemblies call
(measured across all three): `Information.IsNothing` (56 sites), `Operators.CompareString` (24),
`ProjectData.SetProjectError` (12), `ProjectData.ClearProjectError` (8), and three `Conversions`
methods. Full `ToString` overload set included because VB binds overloads by static type.

## Strong-name handling

`FX3Api` binds to `CyUSB` by strong name. We cannot sign with Cypress's private key, so the
shims use **OSS public signing**: the assembly carries the correct *public* key — extracted
from the shipped DLL's metadata, which is public data, not decompilation — and therefore the
correct `PublicKeyToken`. The signature is invalid, but **Mono does not verify strong-name
signatures**, so binding succeeds.

Consequence: these shims work on **Mono only**. They will not load on .NET Framework on
Windows without skip-verification registration. That is by design — Windows already has the
real `CyUSB.dll`.

Keys are extracted by `build.sh` from `resources/CyUSB.dll` and Mono's VB reference assembly.

## Legal boundary

- `CyUSB.dll` is **never decompiled** — the [Cypress agreement §1.2](../../resources/cypress_software_license.txt)
  forbids it. Its required surface was recovered from **ADI's own MIT-licensed metadata**
  (which types/members `FX3Api` references), plus Cypress's public API documentation.
- The FX3 RAM boot protocol in `DownloadFw` follows the publicly documented Cypress EZ-USB
  boot format (AN76405), as implemented by open tools such as `fxload`. No Cypress code is used.
- ADI assemblies are decompiled only as **reference for reading**; nothing is copied into the
  C++ port. See Part 6.2 / 6.5 of the design doc for the provenance rules.

---

## Build

Requires: .NET SDK (for building), Mono (for running), libusb-1.0.

```bash
brew install dotnet mono libusb        # macOS
./build.sh
```

`build.sh` extracts the public keys, builds the three shims, and stages a runnable tree in
`stage/` containing the ADI binaries with the shims layered over them.

## Capture a trace

```bash
./run.sh --trace traces/session-01.jsonl                 # ad-hoc, git-ignored
./run.sh --trace traces/golden/<sn>_<dut>_<scenario>.jsonl   # corpus, committed
```

Environment:

| Variable | Effect |
|---|---|
| `CYUSB_TRACE` | Path to the `.jsonl` trace. Tracing is off when unset. |
| `CYUSB_TRACE_HEX=0` | Omit payload bytes (much smaller traces) |
| `DYLD_LIBRARY_PATH` / `LD_LIBRARY_PATH` | Point at libusb if it is outside the default search path |

### Trace format

One JSON object per line, monotonic microsecond timestamps:

```json
{"t_us":1204,"kind":"control","bmRequestType":192,"bRequest":180,"wValue":0,"wIndex":0,
 "requested":4,"actual":4,"timeout_ms":2000,"rc":4,"dir":"in","data":"01000000"}
{"t_us":1990,"kind":"bulk","endpoint":130,"requested":4096,"transferred":4096,
 "timeout_ms":1000,"rc":0,"dir":"in","data":"A5A5..."}
{"t_us":2210,"kind":"enumerate","detail":"found=1"}
```

`bRequest` values map to `FX3Api.USBCommands` — 42 opcodes, transcribed to
`protocol/UsbCommands.hpp` in the port.

## Platform notes

- **macOS** — works as-is; vendor-class devices need no driver.
- **Linux** — add a udev rule granting access to the FX3 (VID `0x04B4` in bootloader mode,
  plus ADI's application-mode ID), or run as root for a first trace.
- **Windows** — not supported and not needed.

## Known limitations

- **Fixed 2026-10-10, still untested on hardware:** `FX3_FWDWNLOAD_MEDIA_TYPE` was numbered from 0;
  FX3Api passes `1` = RAM and `2` = I2CE2PROM as compiled-in integers, so every application-firmware
  download would have been rejected as an EEPROM write. Bring-up never hit it because no board
  was attached. Treat the first hardware connect as the real test of `DownloadFw`.
- `DownloadFw` implements **RAM** download only. `I2CE2PROM`/`SPIFLASH` (bootloader reflash)
  return `FAILED` — program the board once on Windows if its flash is blank.
- Hotplug events require `USBDeviceList.PollHotplug()` to be pumped by the harness; the real
  CyUSB hooks Windows device notifications, which have no libusb equivalent here.
- Endpoint addresses are hardcoded to the FX3 firmware's known set (`0x01`, `0x81`, `0x82`)
  rather than read from the configuration descriptor. Fine for this board; revisit if it changes.
- Not thread-safe beyond what the legacy stack already serialises via its own control mutex.

## Next steps

1. Attach an EVAL-ADIS-FX3 and capture a full session (connect → configure → register read →
   burst stream → disconnect).
2. Commit the captures under `traces/golden/` (logged in its README) as the golden-file corpus.
3. Cross-check the recovered `USBCommands` table against the firmware Doxygen.
4. Build `libfx3` in C++ and diff its EP0 output against these traces.
