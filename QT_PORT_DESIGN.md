# iSensor FX3 Eval — Qt/C++ Reimplementation Design

**Target audience:** an implementing agent (Opus) with repo access, plus human reviewers.
**Source of truth analysed:** this repository at commit `42d7b95` (2026-05-01), branch `master`.
**Goal:** replace the Windows-only VB.NET WinForms application with a portable Qt/C++ application
(Linux / macOS / Windows), keeping the ability to add new IMUs without recompiling.

---

## Part 0 — Licensing audit (read this first)

### Short answer

**No. The application source is MIT, but the shipped product is not fully open source.**
The repo is MIT-licensed and the first-party sources are open, but the application does not build or
run from open source alone: every DLL it links against ships as a **prebuilt binary with no source in
this repository**, and two of the layers it depends on are **explicitly closed source by the
vendor's own statement**. On top of that, the shipped Windows driver and the USB library are
proprietary Cypress/Infineon components.

### Evidence

**1. The stated license is MIT, but every source file header contradicts it.**

[LICENSE](LICENSE) is a clean MIT license, `Copyright (c) 2018-2021 Analog Devices Inc`.
However, all ~40 first-party source files carry this header — for example
[TopGUI.vb:1](src/TopGUI.vb:1), [Wrapper.cs:2](src_wrapper/Wrapper.cs:2),
[Logger.vb:2](src_StreamDataLogger/Logger.vb:2):

> `'This software is proprietary to Analog Devices, Inc. and its licensors.`

MIT and "proprietary to ADI and its licensors" cannot both be true of the same file. In practice the
repository-level MIT grant is the operative one for the first-party code (it is the explicit,
deliberate grant), and the per-file line is legacy boilerplate — but the ambiguity is real and, if
this port is going anywhere beyond personal use, it is worth resolving in writing with ADI before
relying on it.

**2. The binary dependency wall.** Nothing in `src/`, `src_wrapper/`, or `src_StreamDataLogger/`
compiles without these, and none of them have source here — see
[iSensorFX3Eval.vbproj:74-100](src/iSensorFX3Eval.vbproj:74):

| Binary | Origin | Source available? | License |
|---|---|---|---|
| `FX3Api.dll` | ADI, repo `iSensor-FX3-API` | Yes, upstream (**not** in this fork — README says submodule, but `.gitmodules` is absent and `git submodule status` is empty) | MIT |
| `RegMapClasses.dll` | ADI, same upstream repo | Yes, upstream | MIT |
| `FX3USB.dll` | ADI | Presumed upstream | MIT |
| `adisApi.dll` | ADI `AdisApi` | **No** | **Closed source** |
| `adisInterface.dll` | ADI | **No** | **Closed source** |
| `SignalProcessing.dll` | ADI | **No** (not found published) | Undetermined |
| `sdpApi1.dll` | ADI legacy SDP | **No** (present but unreferenced by any project file) | Undetermined |
| `CyUSB.dll` | Cypress/Infineon EZ-USB FX3 SDK | **No** | **Proprietary** — see [cypress_software_license.txt](resources/cypress_software_license.txt) |
| `AForge.Math.dll` | AForge.NET | Yes, third party | **LGPL v3** (copyleft) |
| `FX3DriverSetup.exe` | Cypress CyUSB3 kernel driver | **No** | **Proprietary, Windows-only** |
| `FX3_Firmware.img`, `boot_fw.img`, `USBFlashProg.img` | ADI firmware built against the Cypress FX3 SDK | Firmware source upstream (MIT); Cypress SDK objects are linked in | Mixed |

**3. The vendor says so explicitly.** The upstream FX3 API repository states:

> "The FX3 API implements in-house closed-source interface libraries (IRegInterface and IPinFcns)... These libraries are required to maintain control and consistency with other iSensor devices and are defined in the AdisApi."

That is `adisApi.dll` / `adisInterface.dll` — and they are not incidental. `adisInterface` provides
`IDutInterface`, `adbfInterface`, `ZeusInterface`, `aducInterface`, `AdcmInterface{1,2,3}Axis`,
`iSensorAutomotiveSpi`, `ComponentSpi`, `IBufferedStreamProducer` — i.e. **the entire register
read/write and buffered-streaming abstraction the GUI is built on**. Every form in `src/` calls into
it.

**4. Cypress terms are restrictive.** [cypress_software_license.txt](resources/cypress_software_license.txt)
grants source use only for "Licensee's internal use", requires Source Code be kept **confidential**
(§1.4, §5.2), permits redistribution in **object code form only** (§1.3), and only "with the
applicable Licensee Product" incorporating a Cypress IC. §6.1 even forbids publishing benchmarks.
This is the opposite of an open-source license.

**5. Copyleft in the dependency graph.** `AForge.Math.dll` is LGPL v3
([AForge.NET License.txt](https://github.com/andrewkirillov/AForge.NET/blob/master/License.txt)).
Fine to link dynamically from an MIT app, but it is a copyleft obligation that must be honoured
(and is not documented anywhere in this repo). The generated `docs/` tree is Ms-PL (Sandcastle) —
see [docs/LICENSE](docs/LICENSE).

### What this means for the port

| Layer | Reusable? | Action |
|---|---|---|
| GUI logic, burst framing, logging, personalities, regmaps (this repo) | **Yes**, MIT | Port as reference behaviour |
| `FX3Api` + `RegMapClasses` (upstream MIT) | **Yes**, as *specification* — .NET assemblies, not linkable from C++ | Reimplement in C++ from upstream source |
| `adisApi` / `adisInterface` / `SignalProcessing` | **No** — closed binaries | **Must be reimplemented from scratch**, from observable behaviour + the MIT firmware protocol |
| `CyUSB.dll` + CyUSB3 driver | **No** — proprietary, Windows-only | Replace with **libusb-1.0** (LGPL-2.1) |
| FX3 firmware `.img` blobs | Redistributable as objects; source is upstream MIT | Keep shipping the blobs; do not attempt to rebuild without the Cypress SDK |

**Net:** a clean-room-ish Qt port is *more* open than the original, because reimplementing
`adisInterface` in C++ removes the only genuinely closed first-party layer. The remaining
non-open pieces are the FX3 firmware images (redistributable binaries) and the Cypress silicon
itself. That is an acceptable and honest position — but the README's implicit "this is open source"
framing should not be carried over verbatim.

**Open items to resolve before or during the port:**
- Confirm with ADI whether the per-file "proprietary" header or the repo MIT LICENSE governs.
- Confirm the FX3 board's application-mode USB VID/PID (not present in the shipped binaries in
  readable form; get it from upstream `FX3Connection` source or `lsusb` against real hardware).
- Decide whether the firmware `.img` blobs are redistributed with the Qt app or fetched at install time.

### Addendum — three compliance findings

**A. `sdpApi1.dll` should not be in a public MIT repo.** It embeds the string
*"This software is for ADI internal use only. A license file is required to run the application."*
and a support contact for hardware-ID-based licensing. It is referenced by **zero** project files —
dead weight. Do not carry it into the port; consider reporting it upstream.

**B. The AForge LGPL v3 situation is a real but curable violation.** `AForge.Math.dll` is LGPL v3
and the repo contains **no** attribution, no license text, and no source offer. Precisely:
- *Violated:* LGPL v3 §4(a)/(b) (prominent notice + shipped GPL/LGPL texts) and GPLv3 §6
  (Corresponding Source must *accompany* the binary — "it's on GitHub somewhere" is not sufficient).
- *Not violated:* §4(d)'s relink requirement is structurally satisfied — it is an unmodified,
  separately-loaded assembly the user can swap.

Curable with a `THIRD_PARTY_LICENSES.md` commit. **Moot for the port**: the Qt design drops AForge
entirely (PocketFFT/KissFFT, BSD-3), which was a deliberate choice partly for this reason.

**C. Cypress userland is avoidable on all three platforms.** Infineon does ship an FX3 SDK for
Linux and macOS containing `cyusb_linux` / a macOS CyUSB wrapper — but those are **thin C wrappers
over libusb-1.0**, not the .NET `CyUSB.dll` this repo links, and using them re-imposes the Cypress
agreement (internal use, confidentiality, object-only redistribution) on a layer that is ~200 lines
of libusb calls. On Linux and macOS the FX3 is a plain vendor-class device with **no kernel driver
at all**. **Decision: link libusb-1.0 directly; use no Cypress userland code on any platform.** The
only Cypress-specific behaviour needed is the bootloader's firmware-download vendor command (`0xA0`),
which is publicly documented and implemented by open tools (`fxload`, `cyusb_linux`).

---

## Part 1 — What the existing application does

~16.8 kLOC of VB.NET/C#, WinForms, .NET Framework 4.5, single-window-plus-satellite-forms design.

### Feature inventory (from [src/guide/guide.md](src/guide/guide.md) and the forms)

| # | Feature | Implementing form | Port priority |
|---|---|---|---|
| 1 | Enumerate / connect / reboot FX3 boards, multi-board select | `TopGUI`, `SelectFX3GUI` | **P0** |
| 2 | DUT personality selection (per-part SPI + pin + supply defaults) | `SelectDUTGUI`, `DutPersonality` | **P0** |
| 3 | Register map load from CSV, paged register grid, read/write, hex↔scaled | `TopGUI_RegisterForm` | **P0** |
| 4 | Continuous page read, data-ready frequency measurement | `TopGUI_RegisterForm` | **P0** |
| 5 | Register map dump to CSV / restore from CSV | `TopGUI_RegisterForm` | **P1** |
| 6 | Bulk register logging to CSV (DR-synchronous or async, multi-file) | `RegisterBulkReadGUI` + `Logger` | **P0** |
| 7 | Burst-mode streaming + logging, per-IMU burst framing | `IMUStreamingGUI` + `BurstManager` | **P0** |
| 8 | Time-domain real-time plotting + CSV playback | `DataPlotGUI` | **P1** |
| 9 | Frequency-domain (FFT) real-time plotting, averaging, 3 dB passband | `FrequencyPlotGUI` + `SignalProcessing` | **P1** |
| 10 | FX3 configuration (SCLK, stall, CPOL/CPHA, CS timing, word length, watchdog, supply) | `FX3ConfigGUI` | **P0** |
| 11 | GPIO/DIO control: read, drive, PWM, pulse drive, resistor config, freq measure | `TopGUI_GPIOForm` | **P1** |
| 12 | Bit-bang SPI (arbitrary SPI robustness testing) | `BitBangSpiGUI` | **P2** |
| 13 | Precise pulse-width measurement with SPI/pin trigger | `PulseMeasureGUI` | **P2** |
| 14 | Burst-transaction test bench (arbitrary MOSI / byte count) | `BurstTestGUI` | **P2** |
| 15 | FX3 NVM: error log dump, EEPROM read | `NVMInterfaceGUI` | **P2** |
| 16 | DUT factory reset + flash update | `FacResetGUI` | **P2** |
| 17 | ADcmXL real-time streaming + buffered (time/FFT) capture | `ADcmXLStreamingGUI`, `ADcmXLBufferedLogGUI` | **P3** (machine-health parts, not IMUs) |
| 18 | API/board info, USB latency measurement, CRC4 helper | `ApiInfoGUI`, `TopGUI` | **P2** |
| 19 | Binary pattern file writer | `BinaryFileWriterGUI` | **P3** (author himself calls it out of place, [BinaryFileWriterGUI.vb:5](src/BinaryFileWriterGUI.vb:5)) |
| 20 | Colour palette config, welcome guide, GitHub release check | `ColorPaletteGUI`, `WelcomeGuideGUI` | **P3** |
| 21 | `FX3ApiWrapper` for Python/MATLAB/LabVIEW interop | `src_wrapper` | **P2** (becomes a C API + Python binding) |

### Layer map of the current design

```
WinForms (src/*.vb)            ← 14 forms, all reach directly into TopGUI.FX3 and TopGUI.Dut
   ├── BurstManager.vb         ← per-IMU burst framing (hardcoded if/else chain)
   ├── DutPersonality.vb       ← CSV-driven per-part defaults
   └── StreamDataLogger        ← async CSV writer thread + bounded queue
        │
   adisInterface (CLOSED)      ← IDutInterface: ReadUnsigned/Signed/Scaled, Write*, buffered streams
   RegMapClasses (MIT)         ← RegClass / RegMapCollection, CSV regmap parser
        │
   FX3Api (MIT)                ← FX3Connection: USB vendor commands, SPI config, pins, stream engine
        │
   CyUSB.dll (PROPRIETARY) → CyUSB3.sys (PROPRIETARY, Windows) → FX3 board
```

### Structural problems to fix, not carry over

1. **God object.** `TopGUI` (1710 lines + 1444 designer lines) holds the FX3 handle, the DUT
   handle, the regmap, plot state, personality state, and the register grid. Every satellite form
   takes a `TopGUI` reference and mutates it. There is no model layer.
2. **Hardware coupling from the UI thread.** Forms call blocking SPI directly
   (e.g. [TopGUI_RegisterForm.vb:197](src/TopGUI_RegisterForm.vb:197) does a page read inside a
   cell-click handler). `System.Timers.Timer` callbacks then `BeginInvoke` back to the UI.
3. **Per-device logic hardcoded in a switch.** [BurstManager.vb:561-808](src/HelperClasses/BurstManager.vb:561)
   is a 250-line `If personality.Contains("1657") ... ElseIf ...` chain. **Adding an IMU means
   editing and recompiling this file.** This is the single biggest blocker to the modularity goal.
4. **String-matching heuristics.** Burst channel discovery matches register labels for
   `"GYR"`/`"ACC"`/`"ANG"`/`"VEL"` ([BurstManager.vb:473-516](src/HelperClasses/BurstManager.vb:473)),
   and scratch/command registers are found by trying a list of ten candidate names
   ([TopGUI.vb:901](src/TopGUI.vb:901), [TopGUI.vb:1003](src/TopGUI.vb:1003)). The author's own
   comment: *"I'm not in love with how this is being done, but here we are."*
5. **Swallowed exceptions.** `Catch ex As Exception` + `'squash` appears throughout; a failed
   personality parse silently yields a half-populated object
   ([DutPersonality.vb:339-345](src/HelperClasses/DutPersonality.vb:339)).
6. **Windows-isms.** `\` path separators hardcoded ([Logger.vb:224](src_StreamDataLogger/Logger.vb:224),
   [TopGUI.vb:117](src/TopGUI.vb:117)), `My.Settings`, `MsgBox`, registry-backed user config,
   `System.Windows.Forms.DataVisualization` charting.
7. **Global mutable hardware state.** Forms flip `FX3.DrActive` on `Activated`
   ([IMUStreamingGUI.vb:114](src/IMUStreamingGUI.vb:114)) to undo what another form did. Two forms
   fighting over the board is prevented only by disabling buttons.

---

## Part 2 — Target architecture

### Guiding principles

- **Layered, with a hard boundary at the hardware edge.** UI never touches USB. All device access
  goes through one owning object on one thread.
- **Devices are data, not code.** Adding an IMU = dropping in a descriptor file + regmap CSV.
  No recompile.
- **Protocol quirks are strategies.** Where a device genuinely needs code (a novel SPI framing),
  that is a small plugin implementing a narrow interface — not a branch in a shared function.
- **Everything testable without hardware.** A mock transport replays captured USB traffic.

### Module layout

```
isensor-fx3/
├── libfx3/                  # C++17/20, no Qt GUI dependency (QtCore only, or std-only)
│   ├── usb/                 # libusb transport, device enumeration, hotplug
│   ├── protocol/            # FX3 vendor command set (port of FX3Api.USBCommands)
│   ├── board/               # Fx3Board: SPI cfg, pins, PWM, timers, flash, error log
│   └── stream/              # stream engine: generic / burst / real-time / I2C
├── libdut/                  # device abstraction, no Qt GUI dependency
│   ├── regmap/              # RegMap CSV parser, Register, RegMapCollection
│   ├── transport/           # RegisterTransport strategies (see §2.4)
│   ├── device/              # DeviceDescriptor loader, DeviceRegistry
│   └── burst/               # declarative burst descriptors + packet parser
├── libdaq/                  # acquisition + logging + DSP, no Qt GUI dependency
│   ├── logger/              # async CSV/binary writer (port of StreamDataLogger)
│   └── dsp/                 # FFT, windowing, averaging (replaces SignalProcessing.dll)
├── app/                     # Qt Widgets application
│   ├── model/               # Qt models: RegisterTableModel, DeviceListModel, ...
│   ├── controller/          # DeviceController (owns libfx3+libdut on worker thread)
│   ├── views/               # one widget per legacy form
│   └── plot/                # plotting widgets
├── devices/                 # ← SHIPPED DATA. one dir per supported part
├── regmaps/                 # existing 92 CSVs, carried over unchanged
├── firmware/                # FX3_Firmware.img, boot_fw.img, USBFlashProg.img
├── bindings/                # C ABI + Python (replaces src_wrapper)
└── tests/
```

Rationale for splitting `libfx3` / `libdut` / `libdaq` from `app`: the current codebase has no
headless path at all, yet `src_wrapper` exists precisely because people want one. Building the
libraries GUI-free from day one gives the Python/MATLAB binding for free and makes CI possible
without a display.

### 2.1 `libfx3/usb` — transport

Replace `CyUSB.dll` with **libusb-1.0**.

```cpp
class UsbTransport {                       // interface — enables the mock
public:
    virtual ~UsbTransport() = default;
    virtual std::vector<BoardInfo> enumerate() = 0;
    virtual void open(const std::string& serial) = 0;
    virtual void close() = 0;
    // EP0 vendor control transfer
    virtual int controlTransfer(Direction, uint8_t req, uint16_t value,
                                uint16_t index, std::span<uint8_t> data,
                                std::chrono::milliseconds timeout) = 0;
    // Bulk data endpoints
    virtual int bulkIn (std::span<uint8_t> buf, std::chrono::milliseconds) = 0;
    virtual int bulkOut(std::span<const uint8_t> buf, std::chrono::milliseconds) = 0;
    virtual void submitAsyncIn(std::span<uint8_t> buf, TransferCallback) = 0;  // for streaming
};
```

Implementations: `LibUsbTransport` (real), `ReplayTransport` (tests), `SimulatedFx3` (synthetic IMU).

**Platform notes — read before starting:**
- **Linux:** libusb + a udev rule granting the user access to the FX3 VID/PID. No driver install.
- **macOS:** libusb works out of the box for vendor-class devices. No kext.
- **Windows:** libusb requires **WinUSB** bound to the device. The existing `FX3DriverSetup.exe`
  installs *CyUSB3*, which libusb cannot use. You must ship a WinUSB `.inf` (or use
  libusb's `libusbK`/`WinUSB` install path). **This is a breaking change for existing users** and a
  migration note is mandatory. Consider supporting both: detect CyUSB3 binding and emit a clear,
  actionable error telling the user how to switch.
- Use **asynchronous** libusb transfers with a pool of in-flight bulk-IN buffers for streaming.
  The current design manages a stream thread inside `FX3Connection`; keep that shape — one dedicated
  libusb event thread, buffers handed to a lock-free queue.

### 2.2 `libfx3/protocol` — the vendor command set

This is the crux of the port and the part with the least local documentation. The FX3 firmware
exposes ~60 vendor commands on EP0; `FX3Api.dll` wraps them. The enum `FX3Api.USBCommands` exists in
the shipped `FX3Api.xml` metadata, confirming the design, but **the opcode values are not in this
repository**.

**Source the opcodes from the MIT-licensed upstream, not by reverse-engineering the DLL:**
- `analogdevicesinc/iSensor-FX3-API` — the `USBCommands` enum and `FX3Connection` partial classes.
- The mirror `github.com/xsparc/iSensor-FX3-API` is reachable and confirms MIT + the closed-source
  `AdisApi` caveat; the canonical ADI URL 404'd at the time of writing — verify before relying on it.
- The firmware side (`FX3_Firmware/`, Doxygen at `analogdevicesinc.github.io/iSensor-FX3-API/firmware/docs/`)
  is the authoritative definition of each command's payload layout.

Deliverable: `protocol/UsbCommands.hpp` — a 1:1 C++ `enum class` mirroring `USBCommands`, plus
`protocol/` request builders. Keep the enum names identical to the VB names so the mapping is
auditable. Every command must carry a comment citing the upstream firmware function it invokes.

**If upstream ever becomes unavailable:** capture EP0 traffic with Wireshark/usbmon against the
existing Windows app and rebuild the table empirically. This is legitimate interoperability work,
but it is slower and less complete — prefer the MIT source.

### 2.3 `libfx3/board` — `Fx3Board`

Direct port of `FX3Connection`'s public surface. The full surface extracted from
[FX3Api.xml](resources/FX3Api.xml) is in the appendix; group it as:

| Group | Members |
|---|---|
| Lifecycle | `enumerate`, `waitForBoard`, `connect`, `disconnect`, `resetAll`, `reboot`, `programAppFirmware`, `programBootloader` |
| SPI config | `sclkFrequency`, `stallTime`, `cpol`, `cpha`, `wordLength`, `isLsbFirst`, `chipSelect{Control,Polarity,LeadTime,LagTime}`, `dutSupplyMode`, `partType`, `sensorType` |
| Data ready | `drActive`, `drPolarity`, `drPin`, `readDrFreq` |
| Register I/O | `readRegWord/Byte`, `writeRegWord/Byte`, `readRegArray`, `transfer`, `transferArray`, `writeReadTransferArray` |
| Streams | `startBufferedStream`, `startBurstStream`, `startGenericStream`, `startRealTimeStreaming`, `stopStream`, `cancelStreamAsync`, `getBufferedStreamDataPacket`, `getBuffer`, `getNumBuffersRead` |
| Burst cfg | `burstMode`, `burstByteCount`, `burstMosiData`, `stripBurstTriggerWord`, `triggerReg`, `setupBurstMode`, `clearBurstMode` |
| Pins | `setPin`, `readPin(s)`, `setPinResistorSetting`, `startPwm`, `stopPwm`, `pulseDrive`, `pulseWait`, `measurePinFreq`, `measurePinDelay`, `measureBusyPulse` |
| Bit-bang SPI | `bitBangSpi`, `bitBangSpiConfig`, `setBitBangSpiFreq`, `setBitBangStallTime`, `restoreHardwareSpi` |
| I2C | `i2cReadBytes`, `i2cWriteBytes`, `i2cBitRate`, `startI2cStream` |
| Diagnostics | `getFirmwareVersion`, `getBoardStatus`, `getErrorLog`, `clearErrorLog`, `readFlash`, `getTimerValue`, `watchdogEnable`, `userLed{On,Off,Blink}` |

Events (`NewBufferAvailable`, `StreamFinished`, `UnexpectedDisconnect`, `DisconnectFinished`)
become Qt signals on the controller, or `std::function` callbacks in the GUI-free library.

**Behavioural details worth preserving** (they encode hard-won hardware knowledge):
- Disconnect timeout handling with a 10 s timer ([TopGUI.vb:214](src/TopGUI.vb:214)).
- "Board busy" recovery: offer to reset all FX3s, then re-enumerate
  ([TopGUI.vb:1468](src/TopGUI.vb:1468)).
- Debugger-attached boards may be shared ([TopGUI.vb:1396](src/TopGUI.vb:1396)) — keep as an
  explicit `allowSharedBoard` flag rather than a debugger check.
- Watchdog is disabled on connect ([TopGUI.vb:1450](src/TopGUI.vb:1450)).

### 2.4 `libdut/transport` — replacing the closed `adisInterface`

This layer must be written from scratch. Define one narrow interface:

```cpp
class RegisterTransport {
public:
    virtual ~RegisterTransport() = default;

    virtual uint32_t              readUnsigned(const Register&) = 0;
    virtual std::vector<uint32_t> readUnsigned(std::span<const Register>,
                                               uint32_t captures = 1,
                                               uint32_t buffers  = 1) = 0;
    virtual void writeUnsigned(const Register&, uint32_t value) = 0;

    // buffered streaming
    virtual void startBufferedStream(std::span<const Register>, uint32_t captures,
                                     uint32_t buffers, std::chrono::seconds timeout) = 0;
    virtual std::optional<std::vector<uint16_t>> nextPacket() = 0;
    virtual void cancelStream() = 0;

    // word order for 32-bit registers composed of two 16-bit regs
    virtual void setLowerWordFirst(bool) = 0;
};
```

Signed/scaled variants are **free functions** over `Register` metadata, not virtuals — the original
put `ReadSigned` / `ReadScaledValue` / `ScaleRegData` / `UnscaleRegData` on the interface, which
forced every protocol to reimplement identical arithmetic. Scaling is `value * reg.scale + reg.offset`
([Logger.vb:629](src_StreamDataLogger/Logger.vb:629)); two's-complement conversion is in
[SignedConverter.vb](src_StreamDataLogger/SignedConverter.vb).

Concrete implementations, one per SPI framing (mirroring the closed classes they replace):

| New class | Replaces | Framing |
|---|---|---|
| `StandardSpiTransport` | `adbfInterface` | 16-bit addr/data, `0x80` write flag; ADIS1647x/49x/50x/55x/57x |
| `PagedSpiTransport` | `PagedDutBase` | writes `PAGE_ID` (addr 0) before access; wraps `StandardSpiTransport` |
| `LegacySpiTransport` | `aducInterface` | ADIS1644x family |
| `AutomotiveSpiTransport` | `iSensorAutomotiveSpi` + `ZeusInterface` | 32-bit frames w/ CRC4 (seed 0xA), transaction counter, state vector; ADIS1655x/1660x |
| `ComponentSpiTransport` | `ComponentSpi` + `ComponentInterface` | configurable addr/data/write-bit positions & polarity — parameters already exist in `DutPersonality` |
| `AdcmTransport` | `AdcmInterface{1,2,3}Axis` | ADcmXL machine-health parts (P3) |

**Recoverable specification for each:** the automotive CRC4 algorithm is fully present in
[HelperFunctions.vb:64](src/HelperClasses/HelperFunctions.vb:64) (`CalcCRC28Bit`, seed `0b1010`);
the CCITT-16 burst CRC is at [HelperFunctions.vb:225](src/HelperClasses/HelperFunctions.vb:225);
the component-SPI field positions come from `AddrPosition`/`DataPosition`/`WriteBitPosition`/
`WriteBitPolarity` in [DutPersonality.vb:77-94](src/HelperClasses/DutPersonality.vb:77) and the CSV
in [dut_personalities.csv](src/UserConfig/dut_personalities.csv). Exception semantics to reproduce:
`SpiException`, `InvalidCRCException`, `TransactionCounterException`, `StateVectorException`,
`SpiAddressException` — with the "log, don't throw" mode the register form relies on
([TopGUI.vb:220](src/TopGUI.vb:220), [TopGUI_RegisterForm.vb:555](src/TopGUI_RegisterForm.vb:555)).

**Verification plan:** wire the old .NET app and the new C++ transport to the same board and diff
SPI traffic on a logic analyser, register by register, for one part per family.

### 2.5 `libdut/regmap` — register map

Straight port of `RegMapClasses`. The CSV schema is stable across all 92 files
(header from [ADIS16133_RegMap.csv](regmaps/ADIS16133_RegMap.csv)):

```
Label,Page,Addr,NumBytes,RegOffset,RegScale,ReadLen,ReadFlag,WriteFlag,TwosCompl,Hidden,Default,CalReg
```

```cpp
struct Register {
    std::string label;
    uint32_t page = 0, address = 0, numBytes = 2, readLen = 16;
    double   scale = 1.0, offset = 0.0;
    bool     readable = true, writeable = false, twosComp = false, hidden = false;
    std::optional<uint32_t> defaultValue;
    // extended fields present in RegMapClasses: auxAddress, calReg, calScale,
    // isFlashable, isEmbedded, isFloat, isReadProtected, isWriteProtected, hexFormat
};

class RegMap {                       // ordered + label-indexed, like RegMapCollection
    // ReadFromCSV equivalent, plus:
    //   burstReadList(), burstReadTrig(), hasPages(), hasEmbeddedRegs()
    //   errorText()  ← keep: the current UI surfaces parse errors to the user
};
```

**Keep the CSV format byte-compatible.** 92 regmaps and every user's custom map depend on it.
Parse strictly, report errors with line numbers — better than the current
"`Catch ex As Exception` → half-populated object" behaviour.

The burst-read section of a regmap is parsed by `RegMapCollection.ParseBurstMode` /
`ValidateBurstModeInfo` upstream; inspect a burst-capable CSV (e.g. `ADIS1657x_RegMap.csv`) to
recover the exact syntax before writing the parser.

### 2.6 `libdut/device` — **the modularity mechanism**

This is the answer to "keep some modularity to be able to add new IMUs". Replace the flat
`dut_personalities.csv` + hardcoded `BurstManager` switch with one **descriptor file per device**.

```
devices/
├── adis1657x.toml
├── adis1650x.toml
├── adis1655x.toml
├── adis1660x.toml
└── ...
regmaps/
├── ADIS1657x_RegMap.csv
└── ...
```

```toml
# devices/adis1657x.toml
schema_version = 1

[device]
id            = "adis1657x"
display_name  = "ADIS1657x"
family        = "adis1657x"           # replaces DutPersonality.Parent
regmap        = "ADIS1657x_RegMap.csv"
image         = "ADIS16575-chip-illustration.png"
# optional: auto-detect by PROD_ID instead of relying on the user picking correctly
prod_id_register = "PROD_ID"
prod_id_values   = [16575, 16576, 16577]

[spi]
transport   = "standard"              # -> StandardSpiTransport ; see §2.4 table
mode        = 3                       # CPOL<<1 | CPHA
frequency   = 2000000
stall_us    = 20
word_length = 16
lsb_first   = false
lower_word_first = true

[power]
supply = "3v3"                        # off | 3v3 | 5v0
vddio  = 3.3

[data_ready]
dio      = 1
polarity = "rising"

[commands]                            # replaces the "try ten register names" heuristics
scratch_register = "USER_SCR_1"
command_register = "GLOB_CMD"
soft_reset_bit   = 7
flash_update_bit = 3

[burst]
supported     = true
setup_required = true                 # send one throwaway trigger transaction first
trigger       = { source = "regmap" } # or explicit = [0x00,0x00,0x0A,0x00] (ADIS1655x style)
padding_bytes = 0
sync_word     = 0x0000                # 0 = none; ADIS1649x/1654x use 0xA5A5 / 0xC3C3
strip_leading_words = 1               # legacy "remove first two bytes" behaviour

  [burst.word_size]                   # configurable 16/32-bit
  configurable = true
  default_16bit = true
  control = { register = "MSC_CTRL", bit = 9, set_means = "32bit" }

  [burst.data_type]                   # inertial vs delta
  configurable = true
  default_inertial = true
  control = { register = "MSC_CTRL", bit = 8, set_means = "delta" }

  [burst.checksum]
  configurable = false
  present      = true
  register     = { label = "BURST_CHECKSUM", bits = 16 }

  # Explicit channel layout — replaces label-substring matching entirely
  [[burst.layout]]
  role = "fixed"   ; from_regmap = true          # leading status/diag regs
  [[burst.layout]]
  role = "gyro";  axes = ["X","Y","Z"]
                  inertial_labels = ["X_GYRO","Y_GYRO","Z_GYRO"]
                  delta_labels    = ["X_DELTANG","Y_DELTANG","Z_DELTANG"]
  [[burst.layout]]
  role = "accel"; axes = ["X","Y","Z"]
                  inertial_labels = ["X_ACCL","Y_ACCL","Z_ACCL"]
                  delta_labels    = ["X_DELTVEL","Y_DELTVEL","Z_DELTVEL"]
  [[burst.layout]]
  role = "fixed"   ; from_regmap = true
                   ; follows_word_size = ["DATA_CNT"]   # ADIS1657x quirk

[plot]
default_registers = [
  { pattern = "_GYRO", plot = 0 },
  { pattern = "_ACCL", plot = 1 },
  { pattern = "DATA_CNT", plot = 2 },
]
```

`DeviceRegistry` scans `devices/` at startup plus a per-user override directory
(`QStandardPaths::AppDataLocation/devices/`), validates against the schema, and reports bad
descriptors in the UI instead of silently degrading.

**Adding a new IMU then requires: one regmap CSV + one TOML file. No recompile.** Only a genuinely
new SPI framing needs C++ — and that is a new `RegisterTransport` subclass registered by name, which
can be a runtime plugin (`QPluginLoader`) if you want third-party extension without touching the tree.

**Migration:** write a one-shot converter from
[dut_personalities.csv](src/UserConfig/dut_personalities.csv) (+ the `BurstManager` switch) to the
new descriptors, and keep reading legacy `custom_personality.csv` for one release.

TOML is suggested for comment support and human editability (`toml++`, MIT). JSON via `QJsonDocument`
is acceptable if you want zero new dependencies — but then keep a schema file and good error messages.

### 2.7 `libdut/burst` — declarative burst engine

`BurstPacketParser` consumes a `BurstDescriptor` (from §2.6) plus the loaded regmap, and produces:
- the ordered channel list for the current configuration (word size × data type × checksum), and
- a `parse(std::span<const uint16_t> raw) -> BurstPacket` that handles sync-word search, leading-word
  stripping, padding, length validation, and checksum verification.

Port the existing behaviours faithfully — they are correct and non-obvious:
- ADIS1655x/1657x: whole packet is payload ([BurstManager.vb:300](src/HelperClasses/BurstManager.vb:300)).
- ADIS1649x/1654x: scan forward to the sync word, then past repeated sync words
  ([BurstManager.vb:303-324](src/HelperClasses/BurstManager.vb:303)).
- Everything else: drop the first word ([BurstManager.vb:325](src/HelperClasses/BurstManager.vb:325)).
- Short packet → zero-pad and set a parse-error string, do not throw
  ([BurstManager.vb:330](src/HelperClasses/BurstManager.vb:330)) — the UI shows it in the status label.
- ADIS1655x transmits upper 16 bits first despite the higher address
  ([IMUStreamingGUI.vb:163](src/IMUStreamingGUI.vb:163)) — becomes `lower_word_first = false` in the descriptor.

Add what the original lacks: **checksum verification is computed but never checked** in the streaming
path. Verify it, count failures, and surface a per-stream error rate.

### 2.8 `libdaq/logger`

Port [Logger.vb](src_StreamDataLogger/Logger.vb) — the design is sound: a producer callback enqueues
buffers on a lock-free/mutex queue, a dedicated writer thread consumes a command queue
(`CreateFile` / `WriteData` / `FinalizeFile`), files roll at `FileMaxDataRows` (default 1 000 000, to
stay Excel-openable), a watchdog timer cancels on buffer starvation.

Improvements to make while porting:
- Use `std::filesystem::path`; drop the `"\\"` assumption ([Logger.vb:224](src_StreamDataLogger/Logger.vb:224)).
- Bound the data queue (currently unbounded `ConcurrentQueue` — a slow disk grows RAM without limit).
- Offer a binary/HDF5-style output alongside CSV; at 16 kHz × 20 channels CSV is the bottleneck.
- Keep the exact CSV column semantics: one column per register, optional trailing
  `USB_TIMESTAMP_MS` (ms since stream start), optional scaling
  ([Logger.vb:562-648](src_StreamDataLogger/Logger.vb:562)).

### 2.9 `libdaq/dsp` — replacing `SignalProcessing.dll`

`FFT_Streamer` (used by [FrequencyPlotGUI.vb:35](src/FrequencyPlotGUI.vb:35)) is closed source.
Reimplement: streams a register list, accumulates N samples (N = 2^n, n = 3..14), applies a window,
FFT, magnitude, optional DC null, optional averaging over M frames, and reports −3 dB passband
between user-set min/max.

**FFT library:** use **PocketFFT** (BSD-3) or **KissFFT** (BSD-3). **Do not use FFTW** — GPL, which
would relicense the whole app. This also lets you drop the LGPL `AForge.Math` dependency entirely.

Sample rate comes from measuring the data-ready pin frequency
([FrequencyPlotGUI.vb:186](src/FrequencyPlotGUI.vb:186)) — keep that, and re-measure periodically.

### 2.10 `app` — the Qt layer

**Toolkit: Qt 6 Widgets.** Not QML. Justification: this is a dense, table-and-form-heavy instrument
application with a 90-row register grid, multi-column list views, and native file dialogs. Widgets
map 1:1 onto the existing forms, are faster to port, and behave better on Linux desktops. QML would
be a rewrite, not a port, and buys nothing here.

**Threading model** (fixes structural problem #2):

```
GUI thread                      Device thread (QThread)
──────────                      ───────────────────────
Views  ──signal──►  DeviceController ──►  Fx3Board / RegisterTransport (libusb)
Models ◄─signal──   (QObject, moveToThread)     │
                                                └── Stream thread (libusb events)
                                                        │
                                                        └── Writer thread (Logger)
```

`DeviceController` is the **only** object holding the board. All hardware operations are
queued-connection slots returning results via signals. No view ever blocks. This also naturally
serialises the "two forms fighting over `DrActive`" problem: the controller owns configuration state
and emits `configurationChanged` so every open view stays in sync.

**Plotting — pick deliberately, this is a licensing trap:**

| Option | License | Verdict |
|---|---|---|
| **Qt Charts** | **GPL v3** in open-source Qt (not LGPL) | ❌ would force GPL on the app |
| **Qt Graphs** (Qt 6.6+) | **GPL v3** | ❌ same |
| **Qwt** | Qwt License 1.0 (LGPL 2.1 + linking exceptions) | ✅ **recommended default** — mature, built for instrumentation |
| **QCustomPlot** | GPL v3 / commercial | ❌ unless paying |
| Custom `QPainter`/RHI widget | yours | ✅ best performance; ~1–2 weeks of work |

Recommendation: **Qwt** for v1, behind a thin `PlotWidget` interface so a custom renderer can replace
it later if the 200-sample-window real-time update rate ([PlotterSettings.vb:19](src/HelperClasses/PlotterSettings.vb:19))
proves too slow.

**Settings:** `QSettings` replaces `My.Settings` (window geometry, colours, last board SN, last
personality, log options, serialised `PlotterSettings`). Use INI format explicitly for portability,
in `QStandardPaths::AppConfigLocation` — not the Windows registry.

**View inventory** — one widget per legacy form, same names, so review is mechanical:

| Legacy form | Qt widget | Notes |
|---|---|---|
| `TopGUI` | `MainWindow` | Split: it currently holds the model. Model goes to `DeviceController`. |
| `TopGUI_RegisterForm` | `RegisterView` | `QTableView` + `RegisterTableModel`; page combo; continuous-read via controller timer |
| `TopGUI_GPIOForm` | `PinView` | |
| `SelectDUTGUI` | `DeviceSelectDialog` | now driven by `DeviceRegistry` |
| `SelectFX3GUI` | `BoardSelectDialog` | |
| `SelectRegmapGUI` | `RegmapSelectDialog` | |
| `FX3ConfigGUI` | `BoardConfigDialog` | |
| `RegisterBulkReadGUI` | `RegisterLogView` | |
| `IMUStreamingGUI` | `BurstStreamView` | |
| `DataPlotGUI` | `TimePlotView` | incl. CSV playback |
| `FrequencyPlotGUI` | `FftPlotView` | |
| `BurstTestGUI` | `BurstTestView` | |
| `BitBangSpiGUI` | `BitBangSpiView` | |
| `PulseMeasureGUI` | `PulseMeasureView` | |
| `NVMInterfaceGUI` | `BoardNvmView` | |
| `FacResetGUI` | `FactoryResetView` | |
| `ApiInfoGUI` | `AboutDialog` | |
| `ADcmXL*GUI` | `AdcmXL*View` | P3, may be deferred or dropped — confirm with stakeholders |
| `ColorPaletteGUI` | Preferences page | |
| `WelcomeGuideGUI` | `QTextBrowser` on `guide.md` | render markdown, drop `guide.txt` duplicate |
| `BinaryFileWriterGUI` | — | recommend dropping; unrelated utility |

### 2.11 `bindings` — replacing `src_wrapper`

`FX3ApiWrapper` exists so Python/MATLAB/LabVIEW users can drive the board
([Wrapper.cs](src_wrapper/Wrapper.cs)). Reimplement as:
- a stable **C ABI** (`libfx3_c.h`) over `libfx3` + `libdut`, and
- a **pybind11** Python module (`pyfx3`) mirroring the `Wrapper` method names so existing scripts
  in [src_wrapper/examples/](src_wrapper/examples) port with minimal edits.

MATLAB `loadlibrary` and LabVIEW's Call Library node both consume the C ABI directly — this is
strictly better than requiring .NET interop, and it works on Linux/macOS.

---

## Part 3 — Cross-cutting concerns

### Error handling

Replace `Catch ex As Exception` + `'squash` with a typed error model:
`std::expected<T, DeviceError>` (C++23) or an explicit `Result<T>`. Three severities:
**fatal** (board gone → disconnect and tell the user), **operation failed** (surface in status bar,
keep running), **degraded** (CRC error counter, parse-error string — the pattern
[BurstManager.vb:127](src/HelperClasses/BurstManager.vb:127) already uses).

Preserve the crash log: [TopGUI.vb:791-847](src/TopGUI.vb:791) writes a CSV of
date/user/OS/regmap/version/FX3 uptime/SN/board type/exception. Keep it (minus `Environment.UserName`
— that is unnecessary PII in a file users email to support; log it only on explicit opt-in).

### Configuration & data files

| File | Now | Ported |
|---|---|---|
| Register maps | `regmaps/*.csv`, copied to `$(OutDir)RegMaps` | unchanged format; installed to app data dir, user override dir searched first |
| Personalities | `UserConfig/dut_personalities.csv` (+ `aux_`, `custom_`) | `devices/*.toml`, plus per-user override dir |
| Plot colours | `UserConfig/plot_colors.txt` (hex, one per line) | keep format; move into `QSettings` or a themed palette |
| App settings | `My.Settings` (per-user .NET config) | `QSettings` INI |
| Firmware | `FX3Binaries/*.img` via post-build copy | `firmware/` in app data dir |

### Testing strategy

The current codebase has **zero tests**. Non-negotiable additions:

1. **Unit:** regmap CSV parser (all 92 files must round-trip), scaling/two's-complement conversion,
   CRC4 and CCITT-16 against the VB implementations, burst layout resolution for every device
   descriptor × configuration combination, CSV logger output byte-compatibility.
2. **Golden-file:** capture burst packets from real hardware once per family, replay through
   `BurstPacketParser`, assert channel values. This is the regression net for the highest-risk code.
3. **Protocol:** `ReplayTransport` fed with usbmon captures from the .NET app; assert the C++
   command builders emit identical EP0 traffic.
4. **Integration (hardware-gated):** a CI job tagged `@hardware` that runs against a real
   EVAL-ADIS-FX3 + one IMU; skipped elsewhere.
5. **Simulated device:** `SimulatedFx3` that synthesises a sine-wave IMU so the whole GUI, plotting,
   FFT, and logging path can be exercised with no board. Also makes the app demo-able.

### Build & dependencies

CMake ≥ 3.21, C++20. Dependencies and their licenses (all permissive or LGPL-dynamic — deliberately
no GPL anywhere):

| Dep | Purpose | License |
|---|---|---|
| Qt 6 (Core, Widgets, Gui) | GUI | LGPL v3 (dynamic link) |
| Qwt | plotting | Qwt License 1.0 |
| libusb-1.0 | USB transport | LGPL 2.1 (dynamic link) |
| PocketFFT / KissFFT | FFT | BSD-3 |
| toml++ | device descriptors | MIT |
| Catch2 or GoogleTest | tests | BSL-1.0 / BSD-3 |
| pybind11 | Python binding | BSD-3 |

Ship a `THIRD_PARTY_LICENSES.md` — the current repo has an LGPL dependency with no attribution
anywhere, which should not be repeated.

Packaging: AppImage/Flatpak (Linux), `.dmg` (macOS), MSI + WinUSB driver package (Windows).

---

## Part 4 — Execution plan

| Phase | Deliverable | Exit criteria |
|---|---|---|
| **0. Recon** | Vendored copy of upstream `iSensor-FX3-API` source; `USBCommands` opcode table; confirmed VID/PID; usbmon capture of a full session from the .NET app | Opcode table reviewed against firmware Doxygen |
| **1. Transport** | `libfx3/usb` + `libfx3/protocol`; connect, read firmware version, blink LED, read one register | LED blinks on Linux and macOS from C++ |
| **2. Board** | Full `Fx3Board`: SPI config, pins, PWM, DR measurement | `BoardConfigDialog`-equivalent operations verified on a logic analyser |
| **3. Regmap + standard SPI** | `libdut/regmap`, `StandardSpiTransport`, `PagedSpiTransport` | All 92 regmaps parse; register read/write matches .NET app on ADIS1650x |
| **4. Minimal GUI** | `MainWindow` + `RegisterView` + connect/select-device | Feature parity for guide sections "Reading/Writing DUT Register Data" |
| **5. Device descriptors** | `devices/*.toml` for all parts in `dut_personalities.csv`; `DeviceRegistry`; migration tool | A new IMU can be added with no code change — demonstrate with a scratch descriptor |
| **6. Streaming** | `libdaq/logger`, `BurstPacketParser`, `BurstStreamView`, `RegisterLogView` | CSV output byte-identical to .NET for the same capture on ≥3 IMU families |
| **7. Plotting + DSP** | `TimePlotView`, `FftPlotView`, `libdaq/dsp` | FFT matches `SignalProcessing.dll` output within float tolerance on recorded data |
| **8. Remaining transports** | Automotive SPI, legacy, component SPI | ADIS1655x/1660x and ADIS1644x verified on hardware |
| **9. Long tail** | Bit-bang SPI, pulse measure, burst test, NVM, factory reset, GPIO view | |
| **10. Bindings + packaging** | C ABI, `pyfx3`, installers, WinUSB migration guide | Existing Python examples run unmodified against `pyfx3` |
| **11. ADcmXL** *(optional)* | ADcmXL views + transport | Only if machine-health parts are still in scope |

Phases 1–6 are the critical path to a usable replacement. Phases 7–11 can proceed in parallel once
the controller boundary is stable.

---

## Part 5 — Risks

| Risk | Impact | Mitigation |
|---|---|---|
| Upstream `iSensor-FX3-API` unavailable (canonical URL 404'd during this analysis) | **High** — the vendor command set is the port's foundation | Mirror the repo *now*; fall back to usbmon reverse-engineering |
| Closed `adisInterface` semantics not fully recoverable (esp. automotive SPI state vector / transaction counter) | **High** | Logic-analyser diff against the .NET app per family; treat automotive SPI as its own phase |
| Windows users must migrate CyUSB3 → WinUSB | Medium — support burden | Detect the wrong driver and give an actionable message; ship a signed WinUSB package |
| Qt Charts' GPL accidentally adopted | Medium — relicenses the app | Decided up front: Qwt. Add a license-scan CI step |
| Burst regmap section syntax undocumented locally | Medium | Recover from upstream `RegMapCollection.ParseBurstMode` before writing the parser |
| Behaviour regressions in undocumented quirks (padding bytes, sync words, first-packet discard) | Medium | Golden-file tests from real captures, one per family, before touching the logic |
| Scope creep from ADcmXL / legacy utilities | Low | Explicitly P3; confirm they are still needed |

---

## Part 6 — Binary analysis & protocol recovery

### 6.0 The decisive fact: this is managed code, not native code

Verified against all 11 assemblies in `resources/` (CLR `BSJB` metadata signature present in each):

```
AForge.Math.dll        MANAGED .NET v2.0.50727      RegMapClasses.dll    MANAGED .NET v2.0.50727
CyUSB.dll              MANAGED .NET v2.0.50727      SignalProcessing.dll MANAGED .NET v4.0.30319
FX3Api.dll             MANAGED .NET v4.0.30319      StreamDataLogger.dll MANAGED .NET v4.0.30319
FX3ApiWrapper.dll      MANAGED .NET v4.0.30319      adisApi.dll          MANAGED .NET v2.0.50727
FX3USB.dll             MANAGED .NET v4.0.30319      adisInterface.dll    MANAGED .NET v4.0.30319
                                                    sdpApi1.dll          MANAGED .NET v2.0.50727
```

**There is no native code to disassemble.** CIL retains full type metadata, member names, parameter
names and structured control flow, and none of these assemblies are obfuscated (all identifiers are
intact). A .NET decompiler therefore *reverses the compiler* rather than reconstructing C from
optimised machine code — output is near-original C#/VB, frequently recompilable with minor edits.

**Consequence for tooling: IDA, Ghidra, radare2 and Binary Ninja are all the wrong tool and are not
needed.** The only native binaries in the tree are the FX3 firmware `.img` blobs (ARM926EJ-S), and
those are explicitly **out of scope** — their source is MIT upstream and the blobs ship unmodified.

### 6.1 Toolchain (all free; all permissive except where noted)

| Tool | License | Role |
|---|---|---|
| **ILSpy / `ilspycmd`** | MIT | Primary decompiler. `dotnet tool install -g ilspycmd`. Native on macOS/Linux |
| **AvaloniaILSpy** | MIT | GUI type-tree browsing |
| **Mono.Cecil** | MIT | Scripted metadata extraction (API-surface diffing, call-graph dumps) |
| `monodis` / `ikdasm` | MIT | Raw IL when decompiled C# is ambiguous |
| dnSpy / dnSpyEx | GPL v3 | *Optional*, Windows-only, edit-and-continue debugging. Analysis tool only — vendor nothing from it |
| **usbmon + Wireshark** | GPL v2 | USB wire capture (Linux). `USBPcap` on Windows, `XHC20`/`usbdump` on macOS |
| **sigrok / PulseView** | GPL v3 | Logic-analyser SPI verification |
| **.NET 8 SDK + Mono** | MIT | Running the legacy stack headless for instrumentation (§6.4) |

GPL-licensed *analysis tools* impose no obligation on the software being analysed — only on code
derived from them. Keep that boundary clean and there is no licensing consequence.

### 6.2 Legal posture

| Target | Position | Basis |
|---|---|---|
| `adisApi`, `adisInterface`, `SignalProcessing`, `FX3Api`, `RegMapClasses`, `FX3USB` | **Decompile freely** | Repo LICENSE is MIT, which permits it outright. No anti-RE clause exists on these files — verified: embedded strings contain only a bare `AssemblyCopyright` attribute, no EULA text |
| Same, under the pessimistic "per-file proprietary header governs" reading | **Still permitted for interoperability** | EU Software Directive Art. 6 makes the decompilation-for-interop right **non-waivable by contract**; Art. 5(3) grants observation/study rights. US: *Sega v. Accolade*, *Sony v. Connectix* |
| `CyUSB.dll` | **DO NOT decompile** | [Cypress agreement §1.2](resources/cypress_software_license.txt:53) explicitly prohibits decompiling/disassembling/reverse engineering |
| `sdpApi1.dll` | **Do not touch** | Marked ADI-internal; irrelevant to the port |
| FX3 firmware `.img` | **Do not touch** | Source is MIT upstream; blobs ship unmodified |

`CyUSB.dll` needs no decompilation regardless. Its entire in-use surface is four types and seven
methods (§6.3), documented in Cypress's public *CyUSB.NET Programmer's Reference*, and the wire
behaviour is observable by capturing traffic on **your own hardware** — black-box observation of a
device you own is not reverse engineering of the software.

> **Not legal advice.** The reasoning above is a practical engineering posture. If this port will be
> distributed commercially, have counsel confirm the MIT-vs-header question with ADI in writing.

**The one operational rule that keeps this clean:**

> **Decompiler output is a specification you read and take notes from — never source you paste.**

Every artefact produced in §6.5 is a *description of behaviour* (opcode tables, frame layouts, state
machines) in your own words. No decompiled statement, expression or comment is copied into
`libfx3`/`libdut`. This preserves the interoperability defence, keeps the port cleanly MIT, and — as
a practical bonus — forces you to actually understand each protocol rather than transliterate it.

### 6.3 Reference graph (extracted from assembly metadata)

```
adisApi          → System.Windows.Forms, System.Drawing        ⚠
adisInterface    → adisApi, RegMapClasses
FX3Api           → CyUSB, FX3USB, adisApi, adisInterface, RegMapClasses
FX3USB           → CyUSB
SignalProcessing → AForge.Math, adisInterface, RegMapClasses
StreamDataLogger → adisInterface, RegMapClasses
```

Two structural findings:

1. **`adisApi` depends on WinForms and `System.Drawing`.** The nominally-low-level hardware
   abstraction drags in the GUI stack. It is not portable even in principle — this closes off any
   "just recompile against .NET 8" shortcut and confirms reimplementation is the only path.
2. **`CyUSB` coupling is thin and well-contained.** `FX3Api` references only `USBDeviceList`,
   `CyUSBDevice`, `CyControlEndPoint`, `CyUSBEndPoint`, using `Open`/`Close`/`Reset`/`XferData`/
   `BeginDataXfer`/`WaitForXfer`/`FinishDataXfer`. That is a near-1:1 map onto
   `libusb_control_transfer` + `libusb_submit_transfer` / `libusb_handle_events`.

### 6.4 Two independent recovery channels (use both — they cross-check each other)

**Channel A — static: decompile and read.**
Fast, complete, gives you names and constants. Risk: you may misread intent.

**Channel B — dynamic: run the legacy stack headless and log every USB transfer.**
This is available *because* the assemblies are pure managed code with **zero P/Invoke** — verified:
`adisApi`, `adisInterface`, `SignalProcessing`, `FX3Api`, `RegMapClasses` and `FX3USB` import only
`mscoree.dll`. **`CyUSB.dll` is the sole assembly touching Win32** (`setupapi`, `cfgmgr32`, `hid`,
`kernel32`, `user32`).

So: write a **managed `CyUSB` shim** — an assembly exposing the same four types and seven methods,
backed by `LibUsbDotNet` (or P/Invoke to libusb) — drop it in place of the real `CyUSB.dll`, and the
entire closed stack runs on Linux/macOS under .NET 8 or Mono. Every control transfer and bulk
transfer passes through code you wrote, so you can log it.

This yields, with **no disassembly at all**:
- a complete, timestamped, ground-truth trace of the vendor command protocol,
- an executable **oracle** for differential testing against the C++ implementation,
- and, as a side effect, a working Linux/macOS build of the *existing* app — de-risking the whole
  project long before the Qt UI exists.

Costs ~1–2 weeks. Recommended even though it is throwaway scaffolding, because it converts the
highest-risk unknown (the protocol) into observed fact and gives phases 1–8 a reference to diff
against. Note the shim is **interop scaffolding, not a deliverable** — it links closed ADI binaries
and must not ship.

#### Status: BUILT AND WORKING — see [tools/cyusb-shim/](tools/cyusb-shim)

Full recovered constant tables (42 opcodes, endpoints, every enum the port needs, assembly
identities, CyUSB/VB surfaces, upstream build provenance) are in
[tools/cyusb-shim/RECOVERED_PROTOCOL.md](tools/cyusb-shim/RECOVERED_PROTOCOL.md). Decompiler output
itself is intentionally not committed — that file says how to regenerate it.

> **2026-10-10 re-verification.** Fork confirmed in sync with upstream (`42d7b95`, release
> `v2.9.5.11-pub`; no upstream commits since 2026-05-01), so the analysed binaries are current.
> Regenerating the tables with ILSpy 10.1 reproduced every value — and exposed a shim bug:
> `FX3_FWDWNLOAD_MEDIA_TYPE` was numbered from 0, but FX3Api compiles in `1` = RAM and
> `2` = I2CE2PROM. Every application-firmware download would have been rejected. Fixed; still
> needs a board to confirm. Lesson for the port: **enum values crossing a binary boundary are
> data, not names** — take them from the consumer's IL, never from a decompiler that may have
> resolved them against a neighbouring assembly.

Bring-up completed on macOS ARM64 / Mono 6.14 / libusb 1.0.30. `FX3Connection` constructs and
enumerates; every transfer is traced to JSONL. Hardware-attached capture is the remaining step.

Findings that changed the plan:

1. **Three assemblies needed replacing, not one.**

   | Replaced | Reason | Identity that must match |
   |---|---|---|
   | `CyUSB.dll` | Cypress, proprietary, Windows-only | `CyUSB, 1.2.3.0, PublicKeyToken=01f1d2b6c851ae92` |
   | `FX3USB.dll` | ADI (MIT) but wraps bulk I/O in Win32 `OVERLAPPED` via `CyUSB.PInvoke` | `FX3USB, 1.0.0.0` (not strong-named) |
   | `Microsoft.VisualBasic.dll` | Mono's Homebrew bottle ships a **reference assembly only** (no method bodies); `mono-basic` is not packaged | `Microsoft.VisualBasic, 10.0.0.0, PublicKeyToken=b03f5f7f11d50a3a` |

   The VB gap was the only genuine surprise. It is small: exactly **7 members** are called across
   `FX3Api` + `adisInterface` + `adisApi` — `Information.IsNothing` (56 sites),
   `Operators.CompareString` (24), `ProjectData.SetProjectError` (12), `ProjectData.ClearProjectError` (8),
   and three `Conversions` methods. ~60 lines. (VB binds `ToString` overloads by *static* type, so
   the full primitive overload set is required even though most are unreachable.)

2. **Strong naming is handled by OSS public signing.** `FX3Api` binds `CyUSB` by strong name. The
   public key is extracted from the shipped DLL's metadata (public data — *not* decompilation, so
   Cypress §1.2 is not engaged) and the shim is built with `PublicSign=true`. It carries the correct
   token with an invalid signature; **Mono does not verify strong-name signatures**, so binding
   succeeds. Confirmed at runtime:
   `ref -> CyUSB, Version=1.2.3.0, PublicKeyToken=01f1d2b6c851ae92` resolved to the shim.
   Corollary: the shims are **Mono-only** by construction, which is fine — Windows has the real DLL.

3. **Replacing `FX3USB` rather than emulating it was the key simplification.** The original's
   `CreateEvent → BeginDataXfer → WaitForSingleObject → FinishDataXfer → CloseHandle` sequence exists
   only to obtain a cancellable transfer from the Windows driver. `libusb_bulk_transfer` is already
   synchronous-with-timeout, so it collapses to one call — and the `CyUSB` shim then needs no
   `OVERLAPPED`, `PInvoke`, `XMODE` or async-endpoint surface at all.

4. **`FX3Api` uses only synchronous `XferData`.** Despite `CyUSB.dll` exporting
   `BeginDataXfer`/`WaitForXfer`/`FinishDataXfer`, `FX3Api` never calls them directly — all bulk I/O
   is funnelled through `FX3USB.USB.XferData`. The shim's endpoint surface is correspondingly small:
   `Address`, `LastError`, `Reset`, plus control-transfer properties.

5. **Device filtering is by USB product string, not VID/PID.** Real CyUSB filters on driver binding,
   which libusb has no equivalent for. `FX3Api` compares `FriendlyName` against four literals —
   `"Cypress FX3 USB BootLoader Device"`, `"Cypress FX3 USB BootProgrammer Device"`,
   `"Analog Devices iSensor FX3 Bootloader"`, `"Analog Devices iSensor FX3 Demonstration Platform"` —
   so the shim matches `iProduct` against those, with VID `0x04B4` as a fallback. **This resolves the
   open VID/PID question from Part 0**: identification is by product string, and no application-mode
   VID/PID constant needs recovering.

6. **The vendor command table is recovered: 42 opcodes** (`FX3Api.USBCommands`), spanning I2C
   (`0x10–0x14`), board control (`0xB0–0xBA`), streaming/pins/SPI (`0xC0–0xD2`), LEDs (`0xEC–0xEF`)
   and flash (`0xF0–0xF3`). Endpoints confirmed: `0x01` OUT, `0x81` streaming IN, `0x82` data IN.
   Control transfers are `bmRequestType = Direction | 0x40 | 0x00` — plain vendor requests to the
   device, mapping directly onto `libusb_control_transfer`.

### 6.5 Execution sequence

Insert as **Phase 0** (recon) in the Part 4 plan; steps 4–7 run alongside phases 1–8.

| Step | Action | Output artefact |
|---|---|---|
| **0** | Mirror upstream `iSensor-FX3-API` (canonical ADI URL 404'd during analysis; `xsparc` mirror is live). **Do this first** — everything else is a fallback if it disappears | `third_party/iSensor-FX3-API/` (MIT source) |
| **1** | `ilspycmd -p -o decomp/ resources/{FX3Api,RegMapClasses,adisApi,adisInterface,SignalProcessing,FX3USB}.dll` into a **`decomp/` directory that is `.gitignore`d and never referenced by the build** | Local reference only |
| **2** | Diff decompiled `FX3Api`/`RegMapClasses` against upstream MIT source. Confirms the shipped DLLs match, and validates decompiler fidelity on code whose source you have | Fidelity report; confidence measure for steps 3–4 |
| **3** | Extract `USBCommands` opcode values + per-command payload layouts. Cross-check every entry against the firmware Doxygen (`analogdevicesinc.github.io/iSensor-FX3-API/firmware/docs/`) | **`protocol/UsbCommands.hpp`** — hand-written, each entry citing the firmware function it invokes |
| **4** | Read `adisInterface` transports (`adbfInterface`, `PagedDutBase`, `aducInterface`, `iSensorAutomotiveSpi`/`ZeusInterface`, `ComponentSpi`). Write prose specs: frame layout, bit positions, CRC placement, error conditions | **`docs/protocols/*.md`** — one per transport, in your own words |
| **5** | Read `SignalProcessing.FFT_Streamer`. Record: window function, scaling convention, averaging (linear vs power), DC-null method, magnitude units | **`docs/protocols/fft.md`** |
| **6** | Build the managed CyUSB shim (§6.4). Run the legacy app on Linux against real hardware; capture full sessions | **`traces/*.jsonl`** — ground-truth USB traces |
| **7** | Cross-validate: replay traces through the C++ `ReplayTransport`; assert byte-identical EP0 traffic. Where static and dynamic disagree, **dynamic wins** | Golden-file test corpus (feeds §Testing strategy) |
| **8** | Independent SPI verification on a logic analyser for one part per family — catches errors where *both* channels share a wrong assumption | Per-family sign-off |

**Provenance discipline** (makes the clean-room claim defensible if ever challenged):
- `decomp/` in `.gitignore`; never referenced by CMake; never committed.
- `docs/protocols/*.md` are the *only* bridge from analysis to implementation.
- Each spec doc carries a header: source assembly, version/hash, date, author, and whether the
  finding was confirmed dynamically.
- Commits implementing a protocol cite the spec doc, not the DLL.
- Optional and cheap: have the person who reads the decompiler output write the specs, and a
  *different* person implement `libfx3`/`libdut` from the specs alone. Not legally required given
  the MIT grant, but it converts a defensible position into an unassailable one at near-zero cost.

### 6.6 What this changes in the risk table

The Part 5 top risk — *"upstream unavailable → vendor command set unrecoverable"* — drops from
**High** to **Low**. There are now three independent recovery paths (upstream MIT source, static
decompilation, dynamic capture via the shim), and the second and third do not depend on any
third party remaining online.

The second risk — *"closed `adisInterface` semantics not fully recoverable, esp. automotive SPI
state vector / transaction counter"* — drops from **High** to **Medium**: that logic is plain
readable CIL, and the shim makes it observable at runtime.

---

## Appendix A — `FX3Connection` surface to port

Extracted from [resources/FX3Api.xml](resources/FX3Api.xml). Properties: `ActiveFX3`,
`ActiveFX3SerialNumber`, `AvailableFX3s`, `BusyFX3s`, `FX3BoardAttached`, `BitBangSpiConfig`,
`BootloaderPath`, `FirmwarePath`, `FlashProgrammerPath`, `BufferAvailable`, `BurstByteCount`,
`BurstMOSIData`, `BurstMode`, `ChipSelectControl`, `ChipSelectLagTime`, `ChipSelectLeadTime`,
`ChipSelectPolarity`, `Cpha`, `Cpol`, `CrcFirstIndex`, `CrcLastIndex`, `CrcResultIndex`,
`DIO1`–`DIO4`, `FX3_GPIO1`–`FX3_GPIO4`, `FX3_LOOPBACK1`/`2`, `DrActive`, `DrPin`, `DrPolarity`,
`DutSupplyMode`, `GetBootStatus`, `GetFX3ApiInfo`, `GetFirmwareVersion`, `GetNumBuffersRead`,
`GetTargetSerialNumber`, `I2CBitRate`, `I2CRetryCount`, `IsLSBFirst`, `NumFramesPurged`,
`NumFramesSkipped`, `PartType`, `SensorType`, `PinStart`, `PinExit`, `ReadyPin`, `ResetPin`,
`SclkFrequency`, `StallTime`, `StreamTimeoutSeconds`, `StripBurstTriggerWord`,
`TimerTickScaleFactor`, `TriggerReg`, `WatchdogEnable`, `WatchdogTimeoutSeconds`, `WordCount`,
`WordLength`.

Enums to mirror: `USBCommands`, `StreamCommands`, `StreamType`, `DUTType`, `DeviceType`,
`DutVoltage`, `FX3BoardType`, `FX3PinResistorSetting`, `SpiChipselectControl`, `SpiLagLeadTime`,
`EndpointAddresses`.

Exceptions to mirror: `FX3Exception`, `FX3BadStatusException`, `FX3CommunicationException`,
`FX3ConfigurationException`, `FX3ProgrammingException`.

## Appendix B — files worth reading before writing code

| File | Why |
|---|---|
| [src/HelperClasses/BurstManager.vb](src/HelperClasses/BurstManager.vb) | Every per-IMU burst quirk, in one place. The spec for `devices/*.toml`. |
| [src/HelperClasses/DutPersonality.vb](src/HelperClasses/DutPersonality.vb) | Personality schema and FX3 application semantics |
| [src_StreamDataLogger/Logger.vb](src_StreamDataLogger/Logger.vb) | Async logging design worth keeping |
| [src/TopGUI_RegisterForm.vb](src/TopGUI_RegisterForm.vb) | Register grid semantics, hex/scaled toggling, page validation |
| [src/HelperClasses/HelperFunctions.vb](src/HelperClasses/HelperFunctions.vb) | CRC4 and CCITT-16 reference implementations |
| [src/UserConfig/dut_personalities.csv](src/UserConfig/dut_personalities.csv) | The full supported-device list with real settings |
| [src/guide/guide.md](src/guide/guide.md) | User-facing feature contract |
| [src_wrapper/Wrapper.cs](src_wrapper/Wrapper.cs) | The headless API surface users actually depend on |
