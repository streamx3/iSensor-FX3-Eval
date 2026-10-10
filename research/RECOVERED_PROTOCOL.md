# Recovered protocol & interop facts

Facts recovered from the shipped binaries in [`resources/`](../resources) during the
CyUSB-shim bring-up. This file exists so the research survives without the decompiler output,
which is deliberately **not** committed (see design doc Part 6.2 — decompiled ADI code is
reference material only, never copied into the port).

Everything below is interface data — constants, names, signatures — not implementation.

| | |
|---|---|
| Source binary | `resources/FX3Api.dll`, assembly version **2.9.5.0** |
| Upstream build | `https://bitbucket.analog.com/scm/isensorfx3/isensor-fx3-api.git` @ `master` `d246c5103656c53ec11858aec0e253bd8ee9dccb`, built 2025-01-16 12:25 (from `FX3Api` embedded resources) |
| Eval repo state | in sync with `analogdevicesinc/iSensor-FX3-Eval` `42d7b95` / release `v2.9.5.11-pub` (verified 2026-10-10) |
| First recovered | 2025-07-28; re-verified 2026-10-10 with ILSpy 10.1 — values identical |

The bitbucket URL is ADI-internal; the public mirror of that repo is the
`iSensor-FX3-API` project (MIT). Use the commit hash to align against it.

## Regenerating

```bash
research/decompile.sh                       # -> research/decompiled/ (git-ignored)
awk '/public enum USBCommands$/,/^\t}/' research/decompiled/FX3Api.decompiled.cs
```

The script decompiles from temp copies (ILSpy writes embedded resources next to its input — how
five stray `FX3Api.*.txt` files once landed in `resources/`), never touches `CyUSB.dll`
(Cypress agreement §1.2), and records input hashes in `MANIFEST.txt`. See
[METHODOLOGY.md](METHODOLOGY.md) for why `CyUSB.dll` must also stay *off* the reference path.

---

## USB vendor commands — `FX3Api.USBCommands` (42)

Sent as `bRequest` on EP0. Port target: `libfx3/protocol/UsbCommands.hpp`.

| Opcode | Hex | Name |
|---:|---|---|
| 16 | `0x10` | `ADI_I2C_SET_BIT_RATE` |
| 17 | `0x11` | `ADI_I2C_READ_BYTES` |
| 18 | `0x12` | `ADI_I2C_WRITE_BYTES` |
| 19 | `0x13` | `ADI_I2C_READ_STREAM` |
| 20 | `0x14` | `ADI_I2C_RETRY_COUNT` |
| 176 | `0xB0` | `ADI_FIRMWARE_ID_CHECK` |
| 177 | `0xB1` | `ADI_HARD_RESET` |
| 178 | `0xB2` | `ADI_SET_SPI_CONFIG` |
| 179 | `0xB3` | `ADI_READ_SPI_CONFIG` |
| 180 | `0xB4` | `ADI_GET_STATUS` |
| 181 | `0xB5` | `ADI_SERIAL_NUMBER_CHECK` |
| 182 | `0xB6` | `ADI_WARM_RESET` |
| 183 | `0xB7` | `ADI_SET_DUT_SUPPLY` |
| 184 | `0xB8` | `ADI_GET_BUILD_DATE` |
| 185 | `0xB9` | `ADI_SET_BOOT_TIME` |
| 186 | `0xBA` | `ADI_GET_BOARD_TYPE` |
| 192 | `0xC0` | `ADI_STREAM_GENERIC_DATA` |
| 193 | `0xC1` | `ADI_STREAM_BURST_DATA` |
| 195 | `0xC3` | `ADI_READ_PIN` |
| 196 | `0xC4` | `ADI_READ_TIMER_VALUE` |
| 197 | `0xC5` | `ADI_PULSE_DRIVE` |
| 198 | `0xC6` | `ADI_PULSE_WAIT` |
| 199 | `0xC7` | `ADI_SET_PIN` |
| 200 | `0xC8` | `ADI_MEASURE_DR` |
| 201 | `0xC9` | `ADI_PWM_CMD` |
| 202 | `0xCA` | `ADI_TRANSFER_BYTES` |
| 203 | `0xCB` | `ADI_BUSY_MEASURE` |
| 204 | `0xCC` | `ADI_TRANSFER_STREAM` |
| 205 | `0xCD` | `ADI_BITBANG_SPI` |
| 206 | `0xCE` | `ADI_RESET_SPI` |
| 207 | `0xCF` | `ADI_PIN_DELAY_MEASURE` |
| 208 | `0xD0` | `ADI_STREAM_REALTIME` |
| 209 | `0xD1` | `ADI_NULL_COMMAND` |
| 210 | `0xD2` | `ADI_SET_PIN_RESISTOR` |
| 236 | `0xEC` | `ADI_LED_ON` ¹ |
| 237 | `0xED` | `ADI_LED_OFF` ¹ |
| 238 | `0xEE` | `ADI_LED_BLINKING_OFF` ¹ |
| 239 | `0xEF` | `ADI_LED_BLINKING_ON` ¹ |
| 240 | `0xF0` | `ADI_READ_BYTES` |
| 241 | `0xF1` | `ADI_WRITE_BYTE` |
| 242 | `0xF2` | `ADI_CLEAR_FLASH_LOG` |
| 243 | `0xF3` | `ADI_READ_FLASH` |

¹ Per FX3Api's XML docs, the LED commands work only with the ADI bootloader firmware.

Gaps worth noting: `0xC2` and `0xBB–0xBF` are unassigned. `0xA0` is not ADI's — it is the
standard Cypress bootloader RAM-write request used by `DownloadFw`.

## Control transfer composition

`FX3Api.ConfigureControlEndpoint` always sets:

| CyUSB property | Value | USB field |
|---|---|---|
| `ReqType` | `0x40` (vendor) | `bmRequestType` bits 6–5 |
| `Target` | `0x00` (device) | `bmRequestType` bits 4–0 |
| `Direction` | `0x00` OUT / `0x80` IN | `bmRequestType` bit 7 |
| `ReqCode` | opcode above | `bRequest` |
| `Value`, `Index` | 0 unless the command overrides | `wValue`, `wIndex` |

So `bmRequestType` is always `0x40` (to device) or `0xC0` (from device) — a plain
`libusb_control_transfer`. Typical timeout passed to `XferControlData`: 2000 ms
(e.g. firmware ID read: 32-byte IN).

### `XferControlData` quirks — do not replicate

All EP0 traffic goes through one private method. Its behaviour (re-verified 2026-10-10):

1. Acquire `m_ControlMutex`, waiting at most `Timeout` ms; fail if not acquired.
2. Call `ControlEndPt.XferData(ref buf, ref len)`.
3. **Overwrite the transfer result** with `elapsed_ms <= Timeout`.

Consequences:

- **A control transfer that fails quickly is reported as success.** Only slow transfers fail.
  Callers then parse whatever is in the buffer. A reimplementation must check the real transfer
  status (and actual length).
- The `Timeout` argument never reaches USB. `FX3Api` never assigns `CyUSBEndPoint.TimeOut`, so the
  bus-level timeout is CyUSB's endpoint default (not recovered; the shim uses 1000 ms).
- Control transfers are serialised by the mutex; bulk transfers are not.

## Endpoints — `FX3Api.EndpointAddresses`

| Address | Name | Direction / use |
|---|---|---|
| `0x01` | `ADI_FROM_PC_ENDPOINT` | bulk OUT |
| `0x81` | `ADI_STREAMING_ENDPOINT` | bulk IN — all stream types |
| `0x82` | `ADI_TO_PC_ENDPOINT` | bulk IN — command responses, register array reads |

Bound by address in `EnumerateEndpointsOnTarget`; not discovered from descriptors.

## Other enums

| Enum | Members (ordinal = position, from 0) |
|---|---|
| `StreamCommands` | `ADI_STREAM_DONE_CMD`, `ADI_STREAM_START_CMD`, `ADI_STREAM_STOP_CMD` |
| `StreamType` | `None`, `BurstStream`, `RealTimeStream`, `GenericStream`, `TransferStream`, `I2CReadStream` |
| `FX3BoardType` | `CypressFX3Board`, `iSensorFX3Board_A` … `iSensorFX3Board_H` |
| `DeviceType` | `IMU`, `ADcmXL`, `AutomotiveSpi`, `ComponentSensor` |
| `DUTType` | `ADcmXL1021`, `ADcmXL2021`, `ADcmXL3021`, `IMU`, `LegacyIMU` |
| `DutVoltage` | `Off`, `On3_3Volts`, `On5_0Volts` |
| `SpiChipselectControl` | `SPI_SSN_CTRL_FW`, `SPI_SSN_CTRL_HW_END_OF_XFER`, `SPI_SSN_CTRL_HW_EACH_WORD`, `SPI_SSN_CTRL_HW_CPHA_BASED`, `SPI_SSN_CTRL_NONE` |
| `SpiLagLeadTime` | `SPI_SSN_LAG_LEAD_ZERO_CLK`, `…_HALF_CLK`, `…_ONE_CLK`, `…_ONE_HALF_CLK` |
| `FX3PinResistorSetting` | `None`, `PullDown`, `PullUp` |

`DeviceType`, `DUTType` and `DutVoltage` are persisted **by name** in
`src/UserConfig/dut_personalities.csv` (`SENSORTYPE`, `PARTTYPE`, `SUPPLY` columns), so the port
must keep these spellings for file compatibility.

## Device identification

CyUSB enumerates by driver binding; `FX3Api` then classifies boards by `FriendlyName`
(= USB `iProduct` string), exact match:

| Product string | Meaning | FX3Api action |
|---|---|---|
| `Cypress FX3 USB BootLoader Device` | blank FX3, ROM bootloader | `ProgramFlashFirmware`: load flash programmer to **RAM** |
| `Cypress FX3 USB BootProgrammer Device` | flash programmer running | queued to `ProgramBootloaderThread` → write ADI bootloader to **I2C EEPROM** |
| `Analog Devices iSensor FX3 Bootloader` | ADI second-stage bootloader | if version parsed from `Product` (text after `v`) is older than the API's expected bootloader version → `ProgramFlashFirmware`; else available for `Connect` (which RAM-loads application firmware) |
| `Analog Devices iSensor FX3 Demonstration Platform` | ADI application firmware running | listed as busy (already claimed) |

So a factory-blank board walks: ROM bootloader → flash programmer (RAM) → ADI bootloader
written to EEPROM → reboot → ADI bootloader → application firmware (RAM) on `Connect`.
The shim implements only the RAM steps; a blank board needs one pass on Windows.

No application-mode VID/PID constant exists in `FX3Api`; identification is purely by string.
Raw bootloader mode uses Cypress VID `0x04B4`.

## Assembly identities (binding requirements for shims)

| Assembly | Version | PublicKeyToken | Strong-named |
|---|---|---|---|
| `CyUSB` | 1.2.3.0 | `01f1d2b6c851ae92` | yes (Cypress) |
| `FX3USB` | 1.0.0.0 | — | no |
| `Microsoft.VisualBasic` | 10.0.0.0 | `b03f5f7f11d50a3a` | yes (Microsoft) |
| `adisInterface` | 1.1.0.1 | — | no |
| `adisApi` | 1.2.0.999 | — | no |
| `RegMapClasses` | 2.1.0.0 | — | no |

## CyUSB surface actually used by FX3Api

From `FX3Api`'s own type/member references (verified 2025-07-28 by the shim loading and
constructing `FX3Connection`):

- **Types:** `USBDeviceList`, `USBDevice`, `CyUSBDevice`, `CyFX3Device`, `CyUSBEndPoint`,
  `CyControlEndPoint`, `USBEventArgs`, `FX3_FWDWNLOAD_ERROR_CODE`, `FX3_FWDWNLOAD_MEDIA_TYPE`
- **`USBDeviceList`:** `ctor(byte)` (always `1` = `DEVICES_CYUSB`), `Count`, enumeration,
  `DeviceAttached`, `DeviceRemoved`
- **`CyFX3Device`:** `SerialNumber`, `FriendlyName`, `Product`, `ControlEndPt`, `EndPoints`,
  `DownloadFw(path, media)`, `Reset()`, `bHighSpeed`, `bSuperSpeed`
- **`CyControlEndPoint`:** `ReqCode`, `ReqType`, `Target`, `Value`, `Index`, `Direction`,
  `XferData(ref byte[], ref int)`
- **`CyUSBEndPoint`:** `Address`, `LastError`, `Reset()`
- **`DownloadFw` media used — compiled in as integers:** `1` = `RAM` (application firmware,
  flash programmer), `2` = `I2CE2PROM` (ADI bootloader). Success test is `(int)result > 0` →
  failure, so only `SUCCESS = 0` matters among the error codes.

`FX3Api` never calls `BeginDataXfer`/`WaitForXfer`/`FinishDataXfer` directly. All bulk I/O goes
through `FX3USB.USB.XferData(ref byte[], ref int, ref CyUSBEndPoint)`, which in turn uses
`OverlapSignalAllocSize`, `XferMode`, `TimeOut`, `BeginDataXfer`, `FinishDataXfer`, `Abort` and
`CyUSB.PInvoke.{CreateEvent, WaitForSingleObject, CloseHandle}` — the reason `FX3USB` is replaced
rather than emulated.

## Microsoft.VisualBasic surface used

Measured across `FX3Api` + `adisInterface` + `adisApi`:

| Member | Call sites |
|---|---:|
| `Information.IsNothing` | 56 |
| `Operators.CompareString` | 24 |
| `ProjectData.SetProjectError` | 12 |
| `ProjectData.ClearProjectError` | 8 |
| `Conversions.ToUInteger` / `ToString` / `ToInteger` | 1 each |

`Conversions.ToString(char)` is required at runtime despite the grep showing one generic site —
VB binds overloads by static type, so the shim carries the full primitive overload set.
