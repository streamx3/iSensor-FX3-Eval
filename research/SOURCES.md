# Sources

External references consulted during the research, with what each was used for. Checked
2025-07-28 unless noted; status re-checked 2026-10-10 where marked.

## ADI — this product

| Source | Used for |
|---|---|
| [analogdevicesinc/iSensor-FX3-Eval](https://github.com/analogdevicesinc/iSensor-FX3-Eval) | Upstream of this fork. 2026-10-10: last commit `42d7b95` (#48, 2026-05-01) |
| [Releases](https://github.com/analogdevicesinc/iSensor-FX3-Eval/releases/) | Latest `v2.9.5.11-pub` (2026-05-04) = `42d7b95` |
| [EVAL-ADIS-FX3 user guide (wiki)](https://wiki.analog.com/resources/eval/user-guides/inertial-mems/evaluation-systems/eval-adis-fx3) | GUI documentation |
| [EVAL-ADIS-FX3 developer resources (wiki)](https://wiki.analog.com/resources/eval/user-guides/inertial-mems/evaluation-systems/eval-adis-fx3-developer-resources) | API overview; confirms closed-source interface libraries |
| [Product page](https://www.analog.com/en/resources/evaluation-hardware-and-software/evaluation-boards-kits/eval-adis-fx3.html) | Board |
| [FX3 API & firmware docs (GitHub Pages)](https://analogdevicesinc.github.io/iSensor-FX3-API/) | API reference (Sandcastle), firmware reference (Doxygen) |
| [Firmware Doxygen index](https://analogdevicesinc.github.io/iSensor-FX3-API/firmware/docs/index.html) | "Unique vendor commands trigger subroutines embedded in the firmware"; source file layout (`main.c`, `SpiFunctions.c`, `PinFunctions.h`, `Flash.c`, `HelperFunctions.c`) |
| [FX3Connection class reference](https://analogdevicesinc.github.io/iSensor-FX3-API/api/html/8620563f-3f40-5410-ed4b-dfba03527986.htm) | API surface |
| [Eval API docs](https://analogdevicesinc.github.io/iSensor-FX3-Eval/) | Wrapper / API docs for this repo |

## ADI — FX3 API source (where payload formats live)

| Source | Status |
|---|---|
| `github.com/analogdevicesinc/iSensor-FX3-API` | **Not public.** 404 on 2025-07-28; `git ls-remote` prompts for credentials on 2026-10-10 |
| `bitbucket.analog.com/scm/isensorfx3/isensor-fx3-api.git` | ADI-internal origin of the shipped `FX3Api.dll` (commit `d246c510`, built 2025-01-16), from the DLL's embedded resources. Not accessible |
| **[xsparc/iSensor-FX3-API](https://github.com/xsparc/iSensor-FX3-API)** | **Only known public copy.** MIT. Not a GitHub fork; last push 2020-10-17 (`d0d6775c`). Contains `src/` (VB API), `firmware/` (`FX3_Firmware`, `boot_fw`, `cyfxflashprog`, Doxygen), `hardware/`, `drivers/`. Its README states the interface libraries (`IRegInterface`, `IPinFcns`, defined in `AdisApi`) are "in-house closed-source" |

The mirror predates the shipped binary by ~4 years (`FX3Api` 2.9.5.0, 2025). Expect protocol
drift; cross-check anything taken from it against RECOVERED_PROTOCOL.md and, ideally, hardware
traces. It is a single third-party copy and could disappear — consider forking it.

## Cypress / Infineon (USB stack)

| Source | Used for |
|---|---|
| [EZ-USB FX3 SDK](https://www.infineon.com/design-resources/development-tools/sdk/usb-controllers-sdk/ez-usb-fx3-software-development-kit) | SDK exists for Linux/macOS incl. a libusb-based CyUSB suite — not needed; plain libusb suffices |
| [Linux drivers for USB devices (KB)](https://community.infineon.com/t5/Knowledge-Base-Articles/LINUX-Drivers-for-USB-Devices/ta-p/249166) | `cyusb_linux` is a libusb wrapper |
| [cyusb-linux libusb error thread](https://community.infineon.com/t5/USB-superspeed-peripherals/no-response-using-data-transfer-with-cyusb-linux-libusb-error/td-p/39214) | same |
| [FX3 SDK on Ubuntu 20](https://community.infineon.com/t5/USB-Superspeed-Peripherals/FX3-SDK-on-Ubuntu-20/td-p/286309) | Linux SDK support |
| [Loading .img to EEPROM (C# example thread)](https://community.infineon.com/t5/USB-superspeed-peripherals/Is-there-any-C-example-on-how-to-load-the-img-file-to-an-EEPROM/m-p/116652) | `DownloadFw` to EEPROM needs the boot-programmer image in RAM first — consistent with FX3Api's flow |
| `resources/cypress_software_license.txt` | §1.2 no reverse engineering of Cypress object code; §1.3 object-only redistribution; §1.4/§5 source confidentiality |
| Cypress AN76405 (EZ-USB FX3 boot options) | RAM boot image format and vendor request `0xA0`, as implemented by open tools like `fxload` |

## Third-party licences

| Source | Used for |
|---|---|
| [AForge.NET licence](https://github.com/andrewkirillov/AForge.NET/blob/master/License.txt) | `AForge.Math` is LGPL-3.0 (only `AForge.Video.FFMPEG` is GPL-3.0) |
| [AForge.NET repo](https://github.com/andrewkirillov/AForge.NET) | Public source exists — so the repo's missing notice is curable, not a hidden derivative |
| `docs/LICENSE` | Generated docs: Sandcastle Help File Builder, Ms-PL |
| [Visual Studio Community licence](https://visualstudio.microsoft.com/license-terms/mlt031819/) | Referenced by upstream README for building |

## Legal background (no URLs; standard references)

- *Sega v. Accolade* (9th Cir. 1992) — intermediate copying for interoperability can be fair use (US).
- EU Software Directive 2009/24/EC, Art. 6 — decompilation for interoperability; cannot be excluded by contract.
- LGPL-3.0 §4 and GPL-3.0 §6 — notice and corresponding-source obligations when conveying binaries.

Not legal advice; these framed the posture in QT_PORT_DESIGN.md Part 0 / 6.2.
