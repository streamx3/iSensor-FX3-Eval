# Legacy application behaviour

What the existing VB.NET GUI and its helper libraries actually do, extracted from the source in
this repo (MIT). This is the behaviour any replacement — Qt, Python, anything — has to match or
consciously change. Line references are against upstream `42d7b95`.

Protocol-level constants (opcodes, endpoints, enums) are in [RECOVERED_PROTOCOL.md](RECOVERED_PROTOCOL.md);
this file covers what sits *above* the USB protocol.

---

## 1. Configuration files

### 1.1 DUT personalities — `src/UserConfig/*.csv`

Loaded in this order ([TopGUI.vb:1057](../src/TopGUI.vb:1057)):

1. `custom_personality.csv` — the user's "Custom" entry; created on first run if missing
2. `dut_personalities.csv` — shipped catalogue
3. `aux_dut_personalities.csv` — optional, not shipped; extension point for extra parts

Columns are looked up **by header name**, so order is free
([DutPersonality.vb:282](../src/HelperClasses/DutPersonality.vb:282)):

| Column | Meaning | Example (ADIS16507-2) |
|---|---|---|
| `DISPLAYNAME` | key shown in the UI | `ADIS16507-2` |
| `PARENT` | family; used for the product image | `ADIS1650x` |
| `REGMAP` | file in `regmaps/` (absolute path for Custom; `,` escaped as `{comma}`) | `ADIS16507_2_RegMap.csv` |
| `SENSORTYPE` | `FX3Api.DeviceType` **by name** | `IMU` |
| `PARTTYPE` | `FX3Api.DUTType` **by name** | `IMU` |
| `SPIMODE` | 0–3; bit 0 = CPHA, bit 1 = CPOL | `3` |
| `SPIFREQ` | SCLK, Hz | `1000000` |
| `SPISTALL` | inter-word stall, µs | `16` |
| `DRDIONUMBER` | data-ready pin, DIO1–4 | `1` |
| `DRPOLARITY` | `TRUE` = rising edge | `TRUE` |
| `SUPPLY` | `FX3Api.DutVoltage` by name | `On3_3Volts` |
| `VDDIO` | informational; FX3 cannot set it | `3.3` |
| `ADDRPOS` / `DATAPOS` / `WRITEBITPOS` / `WRITEBITPOLARITY` | component-sensor SPI word layout only | `8` / `0` / `15` / `TRUE` |
| `ISLOWERFIRST` | 32-bit regs: lower 16 bits at address *n* | `TRUE` |
| `SOFTRESETBIT` / `FLASHUPDATEBIT` | bit in the command register | `7` / `3` |
| `DEFAULT_PLOT_REGS` | space-separated `pattern plotIndex` pairs; ≤3 regs matched per pattern | `_GYRO_LOW 0 _ACCL_LOW 1 DATA_CNTR 2` |

The parser adds a row even if it throws partway through — that is how older files with fewer
columns stay loadable ([DutPersonality.vb:339](../src/HelperClasses/DutPersonality.vb:339)).

Applying a personality ([DutPersonality.vb:144](../src/HelperClasses/DutPersonality.vb:144)) sets, in
order: `PartType`, `SensorType` (first — they reset other settings), SCLK, stall, CPHA/CPOL,
`DrActive = False`, DR polarity, DR pin, supply. Before that the GUI warns on 5 V supply for non-Cypress
boards and on VDDIO ≠ 3.3 ([TopGUI.vb:1335](../src/TopGUI.vb:1335)).

### 1.2 Register maps — `regmaps/*.csv` (92 files)

**There are 14 distinct header layouts.** Column order differs between files and the burst column is
spelled both `BURSTMODE` and `BurstMode`; two maps (`ADIS16228_RegMap.csv`, `ADiS1644x_Regmap.csv` —
note the irregular casing of the filename) use `BaseAddr`/`(i)`-suffixed columns instead of `Page`/`Addr`. A parser must match columns by name, case-insensitively.

Common columns:

| Column | Meaning |
|---|---|
| `Label` | register name, unique key |
| `Page`, `Addr` | page and **byte** address (addresses step by 2) |
| `NumBytes` | 1, 2 or 4. 4 = two consecutive 16-bit registers |
| `ReadLen` | bits read |
| `RegScale`, `RegOffset` | engineering value = raw × scale + offset |
| `TwosCompl` | signed |
| `ReadFlag`, `WriteFlag` | access |
| `Default` | factory default; `NONE` or empty = none |
| `ReadProtected`, `WriteProtected`, `PwPrtect`, `Hidden`, `CalReg`, `CalScale`, `Flashable`, `FLOAT` | per-family extras |
| `BURSTMODE` | integer = position in the burst; `T` = burst **trigger** register; empty = not in burst |

ADIS16507-2 burst definition, straight from its map:

| Pos | Register | Addr | Bytes |
|---:|---|---:|---:|
| 0 | `DIAG_STAT` | 0x02 | 2 |
| 1–12 | `X/Y/Z_GYRO_LOW`, `_OUT`, `X/Y/Z_ACCL_LOW`, `_OUT` (interleaved) | 0x04–0x1A | 4 / 2 |
| 13 | `TEMP_OUT` | 0x1C | 2 |
| 14 | `DATA_CNTR` | 0x22 | 2 |
| T | `GLOB_CMD` | **0x68** | 2 |

The map lists *both* 32-bit and 16-bit inertial registers; `BurstManager` picks the right set (§3).

## 2. Connection and DUT check

### 2.1 Connect ([TopGUI.vb:1380](../src/TopGUI.vb:1380))

1. `WaitForBoard(1)`, then branch on what is visible:
   - one available board → use it; several → selection dialog
   - none available but some **busy** (already running app firmware) → offer to reset *all* boards,
     wait up to 10 s, retry. Under a debugger it attaches to the busy board instead.
   - none → error
2. `Connect(sn)` — FX3Api loads application firmware to RAM (see RECOVERED_PROTOCOL device flow)
3. Apply the selected personality, **disable the FX3 watchdog**, build the DUT interface, run the DUT check

### 2.2 DUT interface selection ([TopGUI.vb:1214](../src/TopGUI.vb:1214))

| Condition | `adisInterface` class |
|---|---|
| `PartType = ADcmXL3021 / 2021 / 1021` | `AdcmInterface3Axis / 2Axis / 1Axis` |
| `PartType = LegacyIMU` | `aducInterface` |
| `SensorType = AutomotiveSpi` | `ZeusInterface` over `iSensorAutomotiveSpi` |
| `SensorType = ComponentSensor` | `ComponentInterface` over `ComponentSpi` |
| otherwise (standard IMU — **includes ADIS1650x**) | `adbfInterface` |

These classes are closed source. Their logic (paging, 32-bit word assembly, automotive-SPI CRC,
ADcmXL framing) is what a native reimplementation must reproduce for each family it supports.

### 2.3 DUT check — `TestDUT` ([TopGUI.vb:898](../src/TopGUI.vb:898))

1. Pick the scratch register: first present of `USER_SCRATCH`, `USER_SCR1`, `USER_SCR_1`,
   `USER_SCRATCH_1`, `SCRATCH_A`, `USER_SCR_2`, `APPLICATION_SPACE_7`, `USER_ID`, `XACCL_NULL`, `ALM_MAG1`
2. Force `DrActive = False`; wait for data ready to toggle (`MeasurePinFreq(DrPin, 0, 500 ms, 3)`;
   ADcmXL uses `PulseWait` on BUSY instead). Errors here are swallowed.
3. Save the original value, write a random value in 1…0x1000, read back, restore the original.
4. Pass iff the read-back matches. Restore the previous `DrActive`.

Commands ([TopGUI.vb:1003](../src/TopGUI.vb:1003)): write `1 << bit` to the first present of
`COMMAND`, `GLOB_CMD`, `USER_COMMAND`, with the bit from the personality.

## 3. Burst read — `BurstManager`

[BurstManager.vb](../src/HelperClasses/BurstManager.vb). The family is chosen by **substring match on
the personality name**, in this order — order matters, e.g. `16445` before `1644`
([:561](../src/HelperClasses/BurstManager.vb:561)):

| Match | Word size | Data select | Checksum | Setup cmd | Pad bytes | Notes |
|---|---|---|---|---|---:|---|
| `1655` | 32 fixed | inertial/delta | `BURST_CRC` 32-bit | yes | 0 | 32-bit header `BURST_RD`; upper word first |
| `1657` | 16/32 | inertial/delta | `BURST_CHECKSUM` 16 | yes | 0 | `DATA_CNT` follows word size |
| `1654` | 32 fixed | inertial/delta | `BURST_CRC` 32 | no | 10 | sync word `A5A5` inertial / `C3C3` delta |
| **`1650`** | **16/32** | **inertial/delta** | **`BURST_CHECKSUM` 16** | **no** | **2** | **ADIS16507-2** |
| `1649` | 32 fixed | inertial | `BURST_CRC` 32 | no | 8 | sync word `A5A5` |
| `1647` | 16 fixed | inertial | `BURST_CHECKSUM` 16 | no | 2 | |
| `1646` | 16 fixed | inertial | `BURST_CHECKSUM` 16 | no | 2 | |
| `16445` | 16 fixed | inertial | none | no | 2 | |
| `1644` | 16 fixed | inertial | optional 16 | no | 2 | |
| `1636` | 16 fixed | inertial | none | no | 2 | |
| `1648`, `ADcm` | — | — | — | — | — | no burst (ADcmXL has its own form) |
| anything else | | | | | | read `PROD_ID`, retry once with that number as the name |

**DUT configuration before streaming** ([:363](../src/HelperClasses/BurstManager.vb:363)), each followed
by a 10 ms sleep:

- `1650`, `1657`: `MSC_CTRL` bit 8 = delta, bit 9 = 32-bit
- `1654`: `CONFIG` bit 8 = delta (also selects the sync word)
- `1644`: `MSC_CTRL` bit 4 = checksum enable

**FX3 configuration** ([:409](../src/HelperClasses/BurstManager.vb:409)):
`StripBurstTriggerWord = False`; dummy `TriggerReg` (page 0, addr 0); MOSI command =
`[trigger addr, 0x00]` — for ADIS16507-2 `[0x68, 0x00]` — except `1655`: `[00 00 0A 00]` inertial /
`[00 00 0B 01]` delta; `BurstByteCount = payload + pad`.

**Register list** ([:453](../src/HelperClasses/BurstManager.vb:453)): header (if any) → burst-list
registers before the first `GYR`/`ACC` label → six inertial channels gyro X/Y/Z then accel X/Y/Z,
chosen by label substring (`GYR`/`ACC` or `ANG`/`VEL` for delta) and size (2 or 4) → remaining
burst-list registers → checksum. Relies on every family ordering gyro before accel.

**Packet post-processing** ([:280](../src/HelperClasses/BurstManager.vb:280)):

- families needing a setup command: one extra buffer is requested and the first is discarded
- `1655`, `1657`: packet used as-is
- `1649`, `1654`: skip to the sync word, then past any repeats of it
- all others (incl. `1650`): **drop the first 16-bit word**
- short packets are zero-padded and flagged `"Invalid burst payload length!"`

**Worked example — the setup in the screenshots** (ADIS16507-2, 16-bit, inertial):
registers `DIAG_STAT, X/Y/Z_GYRO_OUT, X/Y/Z_ACCL_OUT, TEMP_OUT, DATA_CNTR, BURST_CHECKSUM` =
20-byte payload; 22 bytes clocked; first word dropped. In 32-bit mode the six channels become the
`_LOW` registers: 32-byte payload, 34 clocked.

## 4. Logging — `StreamDataLogger.Logger`

[Logger.vb](../src_StreamDataLogger/Logger.vb). Producer/consumer: USB buffers go into a concurrent
queue; a writer thread drains it every `BuffersPerWrite` buffers (GUI uses 10 000).

- **Files:** `<FileBaseName>_<NNNN>.csv`, four-digit counter, rolling at `FileMaxDataRows`
  (GUI: 1 000 000, "to open in Excel") ([:650](../src_StreamDataLogger/Logger.vb:650)).
  Burst capture base name: `<personality>_Burst_<yyyy-MM-ddTHH-mm-ss>`.
- **Header:** register labels joined by `,`, plus `USB_TIMESTAMP_MS` if enabled.
- **Values** ([:577](../src_StreamDataLogger/Logger.vb:577)):
  1-byte regs take the **upper byte when the address is odd**, lower when even; 2-byte as-is;
  4-byte combine two words per `LowerWordFirst` (forced upper-first for `1655`). Signed per
  `TwosCompl`. Optional scaling `raw × Scale + Offset`.
- **`USB_TIMESTAMP_MS`** is host wall-clock time (`DateTime.Now`, ~15 ms resolution on Windows)
  taken when the *buffer was dequeued*, relative to stream start. It is **not** a sample timestamp —
  every row from one USB buffer gets the same value. Use the DUT's `DATA_CNTR`/`TIME_STAMP` for timing.
- **Timeout:** 10 s without a new buffer cancels the stream.

## 5. Bulk register logging timing check

Before a DR-synchronised register stream ([RegisterBulkReadGUI.vb:228](../src/RegisterBulkReadGUI.vb:228)):

```
period = (stall_µs / 1e6 + 17 / SCLK) × n16  −  stall_µs / 1e6     (n16: 4-byte regs count 2)
warn if period > 0.9 / f_DR;  DR must also measure ≤ 16 kHz
```

17 = 16 data clocks + 1 clock overhead per word.

## 6. Utilities worth keeping

| What | Where | Detail |
|---|---|---|
| ADXC1500 SPI CRC-4 | [HelperFunctions.vb:64](../src/HelperClasses/HelperFunctions.vb:64) | over the upper 28 bits, seed `0b1010`; GUI tool appends it to a 32-bit word |
| CRC-16-CCITT | [HelperFunctions.vb:225](../src/HelperClasses/HelperFunctions.vb:225) | init `0xFFFF`, poly `0x1021`, no final XOR, bytes big-endian per word; ADcmXL real-time frames |
| Register dump / restore | [TopGUI_RegisterForm.vb](../src/TopGUI_RegisterForm.vb) | CSV `Register, Page, Address, Value` (signed decimal); restore writes only writeable regs, 1 ms apart |
| Paged-part sanity check | same | after reading a page, reads page register (addr 0) and warns once per page if it differs |
| USB latency probe | [TopGUI.vb:597](../src/TopGUI.vb:597) | 1000 × `SetPin(loopback)`; ~150 µs per command expected |
| Error log | [TopGUI.vb:829](../src/TopGUI.vb:829) | `iSensor_FX3_Eval_Error_Log.csv` in CWD; second unhandled exception force-exits |
| Update check | [TopGUI.vb:704](../src/TopGUI.vb:704) | GitHub releases API; tag `vX.Y.Z.W-pub` |

## 7. Settings persisted

`My.Settings` (per-user .NET config): window geometry, last file path, status colours, last FX3 SN,
selected / last-valid personality, log-scaled / log-timestamps flags, XML-serialised `PlotterSettings`
(update rate, samples rendered, autoscale, FFT length / averages / passband / log axes).
Plot palette: `UserConfig/plot_colors.txt`, one hex colour per line.
