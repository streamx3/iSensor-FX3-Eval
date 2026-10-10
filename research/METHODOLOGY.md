# Methodology, environment and lessons

How the research was done, so it can be reproduced, extended or audited. Covers the parts that
otherwise lived only in a chat transcript: commands, versions, dead ends and gotchas.

## Timeline

| Date | What happened |
|---|---|
| 2025-07-28 | Licensing audit; codebase analysis; Qt port design written |
| 2025-07-28 | Licensing follow-ups: CyUSB on Mac/Linux, reverse-engineering posture, AForge LGPL compliance |
| 2025-07-28 | Discovered all DLLs are managed .NET → binary-analysis plan rewritten (no IDA/Ghidra needed) |
| 2025-07-28 | CyUSB shim built; ADI stack running on macOS under Mono; tracer verified |
| 2026-10-10 | Upstream sync check (already current); research committed to `qt-port/research` |
| 2026-10-10 | Facts regenerated with a newer ILSpy → found `FX3_FWDWNLOAD_MEDIA_TYPE` shim bug, fixed |
| 2026-10-10 | Goal pivoted to Python integration; research consolidated into `research/` |
| 2026-10-10 | Hardware confirmed available (Windows app + ADIS16507-2, see screenshots) |

## Environment

macOS (Apple Silicon), Homebrew.

| Tool | Version | Install | Used for |
|---|---|---|---|
| .NET SDK | 10.0.302 → 10.0.401 | `brew install dotnet` | building shims, running ILSpy |
| Mono | 6.14.1 | `brew install mono` | running the legacy .NET Framework stack; `monodis`, `mcs` |
| ILSpy CLI | 10.1.1.8388 | `dotnet tool install -g ilspycmd` | decompiling ADI assemblies |
| libusb | 1.0.30 | `brew install libusb` | shim USB backend |
| `Microsoft.NETFramework.ReferenceAssemblies` | 1.0.3 (NuGet) | via csproj | building `net48` on macOS |

Environment variables that matter:

```bash
export DOTNET_ROOT=/opt/homebrew/opt/dotnet/libexec   # dotnet tools fail to start without it
export PATH="$PATH:$HOME/.dotnet/tools"               # ilspycmd
export DYLD_LIBRARY_PATH=/opt/homebrew/lib            # Mono's DllImport won't find libusb otherwise
export MONO_PATH=<stage dir>                          # resolve the shims before anything else
```

## Input fingerprints

SHA-256 prefixes of the analysed binaries (`resources/` at upstream `42d7b95` / `v2.9.5.11-pub`).
`research/decompile.sh` writes full hashes to `decompiled/MANIFEST.txt`.

| File | sha256 (first 16) |
|---|---|
| `AForge.Math.dll` | `a4f24c9a46705c66` |
| `CyUSB.dll` | `5ccf43ac35cfa7a9` |
| `FX3Api.dll` | `abf805072281fc94` |
| `FX3ApiWrapper.dll` | `43b0b7d45a3774dd` |
| `FX3USB.dll` | `98439ceef3c45c26` |
| `RegMapClasses.dll` | `4e058dcfbe2c2496` |
| `SignalProcessing.dll` | `2b11ea2dc4e75009` |
| `StreamDataLogger.dll` | `9d8eff4dc2807f61` |
| `adisApi.dll` | `8197ecc25301a9fe` |
| `adisInterface.dll` | `47a711777628fa56` |
| `sdpApi1.dll` | `b587b5490c81431b` |
| `FX3_Firmware.img` | `2c8ffb5db42261e5` |
| `USBFlashProg.img` | `159a80d21051d172` |
| `boot_fw.img` | `9ea2eac1d708be31` |

## How each fact was obtained

| Fact | Method |
|---|---|
| All assemblies managed | `file *.dll` ("Mono/.Net assembly") + presence of the CLR metadata signature `BSJB` |
| Which assemblies touch Win32 | native DLL names in `strings`; only `CyUSB.dll` lists `setupapi`, `hid`, `cfgmgr32`, `kernel32`, `user32` |
| Licence strings | `strings -a <dll> \| grep -iE 'license\|copyright\|proprietar'` — found `sdpApi1`'s "ADI internal use only" |
| Types FX3Api uses from CyUSB | `monodis --typeref FX3Api.dll` |
| Assembly identities / strong names | `monodis --assemblyref FX3Api.dll`, `monodis --assembly <dll>` |
| Members, enums, call flow | ILSpy listing of `FX3Api` / `FX3USB` (never `CyUSB`) |
| VB runtime surface | grep of ILSpy listings for `Information.`, `Operators.`, `ProjectData.`, `Conversions.` |
| Public keys for shims | byte-search for the RSA1 public-key blob header (`00 24 00 00 04 80 00 00 94 00 00 00 06 02 …`), copy 160 bytes |
| Upstream provenance | embedded resources of `FX3Api.dll` (`CurrentURL`, `CurrentCommit`, `BuildDate`) |
| Runtime binding works | `mcs`-compiled probes loading `FX3Api.dll` via reflection under Mono |

Verification evidence from bring-up:

```
loaded: FX3Api, Version=2.9.5.0, Culture=neutral, PublicKeyToken=null
  ref -> CyUSB, Version=1.2.3.0, Culture=neutral, PublicKeyToken=01f1d2b6c851ae92   (resolved to shim)
  ref -> FX3USB, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
FX3Connection CONSTRUCTED ok
AvailableFX3s count = 0          (no board attached)
trace: {"t_us":462,"kind":"enumerate","detail":"found=0"}  x4 during construction
```

## Dead ends and gotchas

Each cost time once; recorded so it doesn't happen twice.

| Symptom | Cause / fix |
|---|---|
| `monodis --mref FX3Api.dll` prints nothing | Not the right switch for this purpose. Use `--typeref` for types, ILSpy for members |
| `monodis --output=… FX3Api.dll` aborts: `Assertion at get.c:913` | monodis can't fully disassemble this assembly. ILSpy handles it |
| Five `FX3Api.*.txt` / `.resources` files appeared in `resources/` | ILSpy writes embedded resources next to its input. Decompile from a temp copy |
| Enum shown as `FX3_FWDWNLOAD_MEDIA_TYPE.RAM` in one run, `(FX3_FWDWNLOAD_MEDIA_TYPE)1` in another | ILSpy resolves names against assemblies **next to the input**. With `CyUSB.dll` present, the integer is hidden. This masked a shim bug (RAM numbered 0, real value 1) for 14 months. Keep `CyUSB.dll` off the reference path and trust the integers |
| ILSpy 10 names locals differently from older runs | Greps keyed on variable names (`usb.`, `cyUSBEndPoint.`) catch fewer sites. Grep on member names instead |
| `Could not load … Microsoft.VisualBasic, Version=10.0.0.0` under Mono | Homebrew Mono ships VB as a *reference assembly* only (detect: `strings … \| grep ReferenceAssembly`); `mono-basic` isn't packaged. Shimmed the 7 used members |
| `MissingMethodException: Conversions.ToString(char)` | VB binds overloads by the argument's static type; grep counts hide that. Ship the full overload set |
| Shim builds as `Version 0.0.0.0` | `GenerateAssemblyInfo=false` drops `<AssemblyVersion>`. Set it `true` |
| Strong-named reference to `CyUSB` won't bind | Build with the extracted public key + `PublicSign=true`. Mono doesn't verify signatures; .NET Framework on Windows does |
| Trace file empty after run | `StreamWriter` with `AutoFlush=false` and no flush on exit. Hook `AppDomain.ProcessExit` |
| `build.sh` silently stops after the first key | `find` over a missing root (Linux path on macOS) exits non-zero; `set -o pipefail` kills the script. `\|\| true` inside the group |
| `WebFetch` 404 on `github.com/analogdevicesinc/iSensor-FX3-API`; `git ls-remote` asks for credentials | ADI's API repo is not public (private or removed). Public copy: `xsparc/iSensor-FX3-API` (see SOURCES.md) |

## Principles that held up

- **Read what the consumer compiled, not what the provider declares.** Every interop fact (types,
  members, enum values) came from what `FX3Api` references, which is both legally cleaner (no Cypress
  decompilation) and more accurate (it's what actually executes).
- **Replace, don't emulate.** Replacing `FX3USB` deleted the entire Win32 `OVERLAPPED` problem.
- **Re-verify recovered facts with a different tool or run.** The second pass found a real bug.
- **Write facts down as tables, not code.** Keeps the clean-room line clear and survives
  tool-version churn.
