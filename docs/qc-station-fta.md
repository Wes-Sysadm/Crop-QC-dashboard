# QC Station FTA Proof of Concept

This document describes the first Windows QC Station proof of concept for integrating with a GUSS/FTA Fruit Texture Analyzer. This is a local harness only. It does not sync readings to Azure SQL, write readings into the QC sample grid, capture USB camera photos, upload files to SharePoint, or send email.

## Purpose

The POC gives the QC Station project a safe place to test:

- FTA device initialization and status flow.
- Pressure reading capture through a clean C# abstraction.
- Mock/test mode without FTA hardware.
- Real DLL firmness-reading calls isolated behind a wrapper that can be tested on the QC computer.
- Station configuration for warehouse, DLL path, COM port, API URL, and local data path.

## Project

The harness lives in:

```powershell
src\CropQc.QcStation
```

Run it with:

```powershell
dotnet run --project .\src\CropQc.QcStation\CropQc.QcStation.csproj
```

The app uses `src\CropQc.QcStation\qcstation.settings.json` by default. You can pass another settings file path as the first argument:

```powershell
dotnet run --project .\src\CropQc.QcStation\CropQc.QcStation.csproj -- .\path\to\qcstation.settings.json
```

## Running RealDll Mode as x86

The FTA-connected computer reported this load error:

```text
An attempt was made to load a program with an incorrect format. (0x8007000B)
```

That usually means a 32-bit/64-bit mismatch. The current `FTA_dll.dll` in `C:\Program Files\FTADLL` is likely 32-bit, while a normal .NET run may start the QC Station as a 64-bit process.

For RealDll testing on the QC computer, run the station explicitly as x86:

```powershell
.\scripts\dev-run-qcstation-x86.ps1
```

The script runs:

```powershell
dotnet run --project .\src\CropQc.QcStation\CropQc.QcStation.csproj --configuration Debug --property:Platform=x86 -- .\src\CropQc.QcStation\qcstation.settings.json
```

The QC Station project supports `AnyCPU`, `x86`, and `x64` platforms. Normal solution builds remain unchanged; RealDll hardware testing should use x86 until the vendor DLL bitness is confirmed.

## Configuration

The current settings fields are:

- `StationName`
- `WarehouseCode`
- `FtaMode`: `Mock` or `RealDll`
- `FtaDllPath`
- `FtaDllFileName`
- `ComPort`
- `ApiBaseUrl`
- `LocalDataPath`

`ApiBaseUrl` and `LocalDataPath` are placeholders for later sync/offline work. No cloud or database sync is implemented in this POC.

## Mock Mode

Mock mode is the default and works without hardware or vendor DLLs.

Mock mode can:

- Initialize and report connected status.
- Generate a test pressure reading.
- Accept a manual mock pressure value from the menu.
- Keep a timestamped local log in the running process.

Use mock mode for development machines and demos where the FTA is not connected.

## Real DLL Mode

Real DLL mode is isolated in `FtaDllPressureReader`. This is the only class that loads and calls the vendor FTA DLL.

Expected DLL files later:

```text
FTA_dll.dll
FTA_DLL.dll
borlndmm.dll
```

The current FTA computer has the main DLL at:

```text
C:\Program Files\FTADLL\FTA_dll.dll
```

Place the main FTA DLL in the configured `FtaDllPath`. `FtaDllFileName` is checked first and defaults to `FTA_dll.dll`. If that configured file is not found, the harness also checks the alternate `FTA_DLL.dll` name.

`FTA_dll.dll` or `FTA_DLL.dll` is required. `borlndmm.dll` may be called by the FTA DLL according to the SDK, but it is warning-only in this harness because the current FTA computer may not have it in `C:\Program Files\FTADLL`. If the main DLL exists, the harness attempts a safe load check. If the load fails because of a missing dependency or bitness mismatch, the harness reports the actual loader error instead of crashing.

Real DLL status messages show:

- DLL folder used
- main DLL file used
- whether the main DLL was found
- whether the main DLL load check passed
- whether `borlndmm.dll` was found
- whether RealDll mode is ready for actual function calls

The harness binds the documented FTA SDK 11.0.4 function names:

- `FTAInit`
- `FTASetup`
- `FTAStatus`
- `FTABitStatus`
- `FTADoFirmnessReading`
- `FTADoAutoFirmnessReading`
- `FTAReadMaxFirmness`
- `FTAReadLastFirmness`
- `FTACancel`
- `FTABack`
- `FTAQuit`

The SDK behavior used by the harness is:

- `FTAInit` initializes the interface and establishes the serial/USB link.
- `FTADoFirmnessReading` enables one firmness test cycle and appears to expect the operator to press the FTA front/init button or otherwise run the physical test.
- `FTADoAutoFirmnessReading` starts an auto firmness cycle. The SDK says the FTA beeps and then completes a firmness measurement without input from the Init button.
- Status bit 1 means a new firmness reading is available.
- `FTAReadMaxFirmness` returns the max firmness reading when bit 1 is true, then resets bit 1.
- `FTAReadLastFirmness` returns the last firmness reading when bit 1 is true and does not reset bit 1.
- Reading when bit 1 is not true returns `-1`.

On the real FTA computer, `FTAInit` makes the FTA beep. That confirms the DLL is communicating with hardware. If `FTADoFirmnessReading` returns without crashing but there is no beep and no reading, try `Start Auto Firmness Reading`.

The FTA has also been observed in Windows as a HID USB Input Device:

```text
USB\VID_6017&PID_3430
```

The uploaded `FTA_DLL.CFG` appears to contain `COM1`, and Windows has also been observed listing only `COM1`. That can be confusing because the FTA device itself appears as HID USB. The original vendor software may use additional configuration beyond `FTA_DLL.CFG`, or it may bridge through a vendor driver while still leaving legacy COM settings in the config file.

The harness checks these status bits:

- bit 1: new firmness reading available
- bit 2: new size reading available
- bit 3: interface connected
- bit 5: probe at top
- bit 6: probe at bottom
- bit 7: FTA responded
- bit 8: new mass reading
- bit 9: scale attached/can measure mass

Use the `FTA Diagnostic Status` command before and after a reading attempt when troubleshooting the physical station. It displays the raw `FTAStatus` value and raw `FTABitStatus` values for bits 1, 2, 3, 5, 6, 7, 8, and 9, plus a Yes/No interpretation for each direct bit check.

The diagnostic command also reports:

- `FTA_DLL.CFG` path.
- Whether `FTA_DLL.CFG` exists.
- `FTA_DLL.CFG` last write time.
- `FTA_DLL.CFG` file length.
- Visible COM strings found in `FTA_DLL.CFG`, such as `COM1`.
- Windows COM ports visible to the QC Station process.
- Windows HID devices matching `VID_6017`.
- A warning if `FTA_DLL.CFG` says `COM1`, Windows only reports `COM1`, and the FTA appears as HID USB instead of a COM port.

If `FTAStatus` returns `-1`, the harness labels the value as negative/suspicious and does not decode the raw status word as if every bit is valid. It still shows the direct `FTABitStatus` calls separately.

The `Start Manual/Button Firmness Reading` command captures diagnostic status before and after `FTADoFirmnessReading`. If the call returns but bit 1 is still false, the harness logs:

```text
FTADoFirmnessReading call returned, but no new reading detected yet. Confirm FTA setup COM port and probe state.
```

The actual calling convention and pressure units still need to be confirmed on the physical FTA computer. If the vendor DLL rejects the first binding/call test, check the SDK headers and update only `FtaDllPressureReader`.

## FTA Setup Dialog

Use the `Open FTA Setup` command in the QC Station harness to call `FTASetup()`. The SDK says this opens the setup dialog where the serial port is selected and settings are saved to:

```text
C:\Program Files\FTADLL\FTA_DLL.CFG
```

If the FTA is not responding, run the setup dialog on the QC computer, confirm the COM/USB setting, then initialize and check status again.

If `FTAInit` beeps but firmness commands do not move the probe, compare behavior against the original vendor app and watch the config timestamps before and after opening `FTASetup()` or changing settings in the vendor software. A changed timestamp may reveal which config file the working vendor path actually uses.

## Firmness Reading Commands

The harness keeps the two SDK reading styles separate:

- `Start Manual/Button Firmness Reading` calls `FTADoFirmnessReading()` and returns immediately after the DLL call. Use this to confirm the basic SDK command can be invoked.
- `Start And Wait Manual/Button Reading` calls `FTADoFirmnessReading()`, tells the operator to press the FTA front/init button or run the physical test, then polls bit 1 for up to 60 seconds and reads `FTAReadMaxFirmness()` when available.
- `Start Auto Firmness Reading` calls `FTADoAutoFirmnessReading()`, captures diagnostics before and after the call, then polls bit 1 for up to 60 seconds and reads `FTAReadMaxFirmness()` when available.

If auto mode beeps and manual/button mode does not, the next physical troubleshooting step is to confirm the saved FTA setup, COM/USB selection, and probe state.

## Basic RealDll Test Sequence

On the physical QC computer connected to the GUSS/FTA:

1. Run the QC Station as x86:

   ```powershell
   .\scripts\dev-run-qcstation-x86.ps1
   ```

2. Select `Initialize FTA`.
3. Select `Open FTA Setup` if the serial/USB settings need to be selected or confirmed.
4. Select `Check Status` and confirm bit 3 and/or bit 7 show that the FTA is connected/responding.
5. Select `FTA Diagnostic Status` to capture the baseline raw status and bit values.
6. Select `Start Auto Firmness Reading`.
7. If auto mode does not produce a reading, select `Start And Wait Manual/Button Reading`, then press the FTA front/init button or run the physical firmness test.
8. Select `FTA Diagnostic Status` again if there is no beep, no probe movement, or no new reading.
9. Select `Get Latest Reading` if a separate latest-read check is needed.

If `Get Latest Reading` says no new firmness reading is available, bit 1 was not set yet. Run the physical test cycle again or check the FTA setup/status.

## Test Screen Commands

The local harness displays:

- Station name
- Warehouse
- FTA mode
- DLL path
- DLL file
- COM port
- API base URL placeholder
- Local data path placeholder
- Last pressure reading
- Timestamped log

Available commands:

- Initialize FTA
- Open FTA Setup
- FTA Diagnostic Status
- Check Status
- Start Manual/Button Firmness Reading
- Start Auto Firmness Reading
- Start And Wait Manual/Button Reading
- Get Latest Reading
- Cancel
- Return Probe Home
- Quit/Disconnect FTA
- Use Mock Reading
- Clear Log

## Hardware Testing Still Required

Real hardware testing must happen on the QC computer connected to the GUSS/FTA.

Before production use, test and confirm:

- Correct `FTA_DLL.dll` function names and calling conventions.
- Whether `FTA_dll.dll` or `FTA_DLL.dll` is the final deployed file name.
- Whether `borlndmm.dll` must be loaded before the main FTA DLL.
- Required bitness, such as x86 versus x64.
- COM port or other device connection requirements.
- Whether the vendor software updates `FTA_DLL.CFG` or uses additional config files/registry settings.
- Whether HID `USB\VID_6017&PID_3430` is expected for the working vendor app path despite `FTA_DLL.CFG` containing `COM1`.
- Initialize/status/read/cancel/home command behavior.
- Pressure unit returned by the DLL and whether conversion to pounds is required.
- Error codes and recoverable fault behavior.

## What This POC Does Not Do

This POC does not implement:

- Real USB camera capture.
- SharePoint/OneDrive upload.
- Actual QC Summary email sending.
- Azure SQL sync.
- Offline queue persistence.
- Storage inventory.
- Mexico qualification.
- Room controller imports.
- Packout imports.
- Pool closing imports.
- Long-term analytics.
