# QC Station FTA Proof of Concept

This document describes the Windows QC Station proof of concept for integrating with a GUSS/FTA Fruit Texture Analyzer. The WinForms station can capture FTA pressures locally and save pressure-only rows back to a selected QC sample through the dashboard API. It does not provide offline sync yet, capture USB camera photos, upload files to Google Drive, or send email.

## Purpose

The POC gives the QC Station project a safe place to test:

- FTA device initialization and status flow.
- Pressure reading capture through a clean C# abstraction.
- Mock/test mode without FTA hardware.
- Real DLL firmness-reading calls isolated behind a wrapper that can be tested on the QC computer.
- Station configuration for warehouse, DLL path, COM port, API URL, and local data path.

## Project

The original console harness lives in:

```powershell
src\CropQc.QcStation
```

Run it with:

```powershell
dotnet run --project .\src\CropQc.QcStation\CropQc.QcStation.csproj
```

Installed QC Station computers use this config path by default:

```text
C:\ProgramData\CropQc\QcStation\qcstation.settings.json
```

The app checks settings in this order:

1. Command-line settings path, if provided.
2. `C:\ProgramData\CropQc\QcStation\qcstation.settings.json`.
3. The repo development fallback `src\CropQc.QcStation\qcstation.settings.json`.

You can pass another settings file path as the first argument:

```powershell
dotnet run --project .\src\CropQc.QcStation\CropQc.QcStation.csproj -- .\path\to\qcstation.settings.json
```

The Windows Forms hardware test harness lives in:

```powershell
src\CropQc.QcStation.WinForms
```

Run it in x86 mode with:

```powershell
.\scripts\dev-run-qcstation-winforms-x86.ps1
```

Use the WinForms harness for real FTA hardware testing. The console harness remains useful for mock mode, command-line diagnostics, and non-UI development, but the FTA DLL may expect a Windows UI message pump.

Real hardware testing confirmed the WinForms x86 harness is the correct harness for this unit. The working operator flow is manual/button firmness reading: start continuous manual capture once, then press and hold the green FTA button for each physical test. Auto firmness reading did not move the probe on the current unit and should be treated as experimental.

## FTA Installer Download

Admins can use the web dashboard page `/Admin/Downloads` to open the shared Google Drive download page for the internal FTA DLL installer.

Download entry:

- Name: FTA DLL Installer.
- File: `FTADLL.exe`.
- Use: installer/runtime files needed for the GUSS FTA DLL integration on QC Station computers.
- Link: `https://drive.google.com/file/d/1iYy1v1-D8T-S4SgfHJOeuwoeJfsbcvoS/view?usp=drive_link`.

Install `FTADLL.exe` on each FTA-connected Windows computer before running QC Station RealDll mode. This installer is for internal company computers only. It is separate from the Crop QC Station setup package, which installs the WinForms app and station config. Google Drive sharing permissions should be limited to company users when possible.

After installation, configure the QC Station for the confirmed working path:

```json
{
  "FtaDllPath": "C:\\Windows\\SysWOW64",
  "FtaDllFileName": "FTA_DLL.dll",
  "FtaWorkingDirectory": "C:\\Program Files (x86)\\FTAWin"
}
```

After installing the FTA DLL runtime, run the WinForms x86 QC Station app and use continuous manual capture mode for the confirmed working FTA flow.

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

The WinForms harness also supports x86. It runs on an STA thread with a real Windows message loop. During reading waits it keeps the UI responsive and allows pending Windows messages to be processed, which may matter because the SDK says `FTAInit` installs an FTA interface and shows a tray icon. The vendor FTAwin software and vendor demos are GUI-based, and FTAwin works on the same machine where the console harness can initialize but cannot move the probe.

## Configuration

The current settings fields are:

- `StationName`
- `WarehouseCode`
- `FtaMode`: `Mock` or `RealDll`
- `FtaDllPath`
- `FtaDllFileName`
- `FtaInitializationMode`: `FTAInit` or `FTAInit2`
- `FtaConfigPath`
- `FtaReadingTimeoutSeconds`
- `FtaWorkingDirectory`
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
- `FTAInit2`
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
- `FTAInit2(char *sPath)` initializes the interface using a config path. The vendor demo declares DLL calls with `_stdcall` and calls `FTAInit2` by copying the CFG path text into a `char` buffer.
- `FTADoFirmnessReading` enables one firmness test cycle and appears to expect the operator to press the FTA front/init button or otherwise run the physical test.
- `FTADoAutoFirmnessReading` starts an auto firmness cycle. The SDK says the FTA beeps and then completes a firmness measurement without input from the Init button.
- Status bit 1 means a new firmness reading is available.
- `FTAReadMaxFirmness` returns the max firmness reading when bit 1 is true, then resets bit 1.
- `FTAReadLastFirmness` returns the last firmness reading when bit 1 is true and does not reset bit 1.
- Reading when bit 1 is not true returns `-1`.

On the real FTA computer, `FTAInit` makes the FTA beep. That confirms the DLL is communicating with hardware. With the WinForms x86 harness and `FtaWorkingDirectory` set to `C:\Program Files (x86)\FTAWin`, diagnostic status now shows interface connected, probe at top, and FTA responded.

On the current unit, `FTADoAutoFirmnessReading` does not move the probe. `FTADoFirmnessReading` works when the operator presses and holds the green FTA button during the manual/button reading workflow. After each completed test, the FTA beeps and is ready for the next physical test.

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

The vendor demo source continuously polls during idle with this pattern:

```text
iStatus = FTAStatus();
if (iStatus > 0)
    if (iStatus & 1)
        FTAReadMaxFirmness();
```

The `Demo-Style Poll Reading`, `Demo-Style Auto Reading`, and `Demo-Style Manual/Button Reading` commands mimic that pattern. They poll raw `FTAStatus` for up to `FtaReadingTimeoutSeconds` and read max firmness only when `FTAStatus > 0` and bit 1 is set.

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

The confirmed working RealDll configuration for the current FTA unit is:

```json
{
  "FtaMode": "RealDll",
  "FtaDllPath": "C:\\Windows\\SysWOW64",
  "FtaDllFileName": "FTA_DLL.dll",
  "FtaInitializationMode": "FTAInit",
  "FtaConfigPath": "C:\\Program Files\\FTADLL\\FTA_DLL.CFG",
  "FtaReadingTimeoutSeconds": 60,
  "FtaWorkingDirectory": "C:\\Program Files (x86)\\FTAWin"
}
```

`FTAInit2` remains available for diagnostics and comparison with the vendor demo. To test `FTAInit2`, set `FtaInitializationMode` to `FTAInit2` and set `FtaConfigPath` to the config path being compared.

With `FtaInitializationMode` set to `FTAInit2`, the normal `Initialize FTA` command calls `FTAInit2` automatically. The `Initialize FTA With Config Path` command also calls `FTAInit2` directly, regardless of the configured initialization mode.

When `FtaWorkingDirectory` is configured and the directory exists, the FTA reader sets the process current directory before probing/calling the DLL. This helps test whether the vendor DLL expects to run from `C:\Program Files\FTADLL` while loading supporting files.

If `FTAInit` beeps but firmness commands do not move the probe, compare behavior against the original vendor app and watch the config timestamps before and after opening `FTASetup()` or changing settings in the vendor software. A changed timestamp may reveal which config file the working vendor path actually uses.

## Firmness Reading Commands

The harness keeps the two SDK reading styles separate:

- `Start Continuous Manual Capture` calls the manual/button command, continuously polls for new readings, captures each valid firmness value, auto-fills Pressure 1 / Pressure 2, advances to the next fruit, and re-arms the FTA for the next physical test. This is the recommended working workflow.
- `Stop Continuous Capture` stops the continuous polling/re-arm loop.
- `Start Manual/Button Firmness Reading` calls `FTADoFirmnessReading()` and returns immediately after the DLL call. This is the basic manual/button diagnostic command.
- `Start And Wait Manual/Button Reading` calls `FTADoFirmnessReading()`, tells the operator to press and hold the green FTA button, then polls bit 1 for up to 60 seconds and reads `FTAReadMaxFirmness()` when available. This is the recommended working workflow.
- `Start Manual Reading and Capture` in the local two-pressure panel starts the recommended manual/button workflow, waits for a reading, and places it into the selected local pressure slot.
- `Start Auto Firmness Reading` calls `FTADoAutoFirmnessReading()`, captures diagnostics before and after the call, then polls bit 1 for up to 60 seconds and reads `FTAReadMaxFirmness()` when available. This is experimental and did not work on the current unit.
- `Demo-Style Poll Reading` only polls `FTAStatus()` and reads `FTAReadMaxFirmness()` when `FTAStatus > 0` and status bit 1 is set.
- `Demo-Style Auto Reading` calls `FTADoAutoFirmnessReading()`, then uses demo-style polling.
- `Demo-Style Manual/Button Reading` calls `FTADoFirmnessReading()`, tells the operator to press the physical FTA button, then uses demo-style polling.

If manual/button mode stops working, the next physical troubleshooting step is to confirm the saved FTA setup, COM/USB selection, working directory, and probe state.

## Basic RealDll Test Sequence

On the physical QC computer connected to the GUSS/FTA:

1. Close FTAwin so only one process is talking to the FTA.
2. Run the WinForms QC Station as x86:

   ```powershell
   .\scripts\dev-run-qcstation-winforms-x86.ps1
   ```

3. Select `Initialize FTA`.
4. Select `FTA Diagnostic Status` and confirm the DLL path, config path, process architecture, and status bits.
5. Select `Start Continuous Manual Capture`.
6. Press and hold the green FTA button until the probe completes the test.
7. Let the harness capture the value, auto-fill the current target, and re-arm for the next test.
8. Repeat the physical green-button test cycle. The harness fills Fruit 1 Pressure 1, Fruit 1 Pressure 2, Fruit 2 Pressure 1, Fruit 2 Pressure 2, and continues through Fruit 25 Pressure 2.
9. Select `Stop Continuous Capture` when done or if troubleshooting is needed.
10. Select `Quit/Disconnect FTA` before closing the station app when possible.
11. Select `FTA Diagnostic Status` again if there is no beep, no probe movement, or no new reading.

If `Get Latest Reading` says no new firmness reading is available, bit 1 was not set yet. Run the physical test cycle again or check the FTA setup/status.

If the WinForms harness works but the console harness does not, the FTA DLL likely depends on the UI message loop/tray-interface behavior created by `FTAInit` or `FTAInit2`.

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
- Initialize FTA With Config Path
- Open FTA Setup
- FTA Diagnostic Status
- Check Status
- Start Continuous Manual Capture
- Stop Continuous Capture
- Start Manual/Button Firmness Reading - Recommended
- Start Auto Firmness Reading - Experimental
- Start And Wait Manual/Button Reading - Recommended
- Demo-Style Poll Reading
- Demo-Style Auto Reading - Experimental
- Demo-Style Manual/Button Reading
- Get Latest Reading
- Cancel
- Return Probe Home
- Quit/Disconnect FTA
- Use Mock Reading
- Clear Log

The WinForms harness exposes the same hardware commands as buttons, plus status/config labels for station name, warehouse, FTA mode, DLL path, DLL file, initialization mode, config path, current working directory, process architecture, OS architecture, and last pressure reading. Its log box auto-scrolls and keeps timestamped entries visible during hardware tests.

The WinForms harness also includes a 25-fruit pressure grid. It tracks fruit number, Pressure 1, Pressure 2, average pressure, current target, last captured reading, capture target, local row status, and reading history. Continuous Manual Capture is the primary workflow: the operator starts it once, then runs each physical FTA test with the green button while the harness captures readings and advances through P1/P2 for each fruit. Captured pressures stay local until the operator selects `Save Pressures to Dashboard` or enables the optional auto-save setting.

## Dashboard API Pressure Save

The WinForms harness can connect to the MVP 1 API and save FTA pressure readings back to a selected QC sample. This is online-only for now; offline sync remains future work.

The browser pressure page does not connect directly to the physical FTA DLL. Open or create the sample in the web dashboard first, then use the local WinForms QC Station to select that sample and capture pressures.

Run the dashboard/API and WinForms station together:

1. Start the web dashboard or API project. On Render, use the dashboard URL as `ApiBaseUrl`.
2. In the dashboard, sign in as an Admin and open `Admin -> QC Stations`.
3. Create one QC Station record for each physical QC computer, then download that station's setup package immediately after creation or key rotation.
4. On the QC Station computer, extract the setup package and double-click `Install-CropQcStation.cmd`. No PowerShell command entry is required. A full package installs the WinForms app to `C:\Program Files\CropQc\QcStation`, copies `qcstation.settings.json` to `C:\ProgramData\CropQc\QcStation\qcstation.settings.json`, backs up any existing app/config first, creates a desktop shortcut when possible, and registers `cropqcstation://` links.
5. Confirm the WinForms `ApiBaseUrl` matches the dashboard/API URL, such as `https://localhost:7001` for local API testing or the Render dashboard URL for live testing.
6. Run the WinForms x86 harness:

   ```powershell
   .\scripts\dev-run-qcstation-winforms-x86.ps1
   ```

7. In the web dashboard, open `/Samples/{sampleId}` and click `Open in QC Station`.
8. The browser launches `cropqcstation://sample/{sampleId}`. The WinForms app reads the installed config, calls the dashboard API, loads that sample, and sets the pressure target to the first missing slot.
9. Use `Start Continuous Manual Capture`, then press and hold the green FTA button for each physical test.
10. Click `Save Pressures to Dashboard`.
11. Refresh the web dashboard sample page to see the saved pressure readings.

The protocol handler points to `C:\Program Files\CropQc\QcStation\CropQc.QcStation.WinForms.exe`. If the Admin QC Stations page says the app payload is missing, publish/deploy the WinForms app payload before creating or rotating station setup packages.

The QC Station pressure save endpoint is pressure-only. It updates `Pressure1Lbs`, `Pressure1Source`, `Pressure2Lbs`, and `Pressure2Source`; it does not overwrite weight, grade, starch, defects, photos, or receipt data. `Auto-save after each completed fruit` can save after Pressure 2 is captured, but it is unchecked by default.

Station API access is managed in the database, not with one shared Render environment variable. Each QC computer sends `X-QC-STATION-CODE` and `X-QC-STATION-API-KEY`; the server stores only a hash of the key. Deactivate a station to block it immediately, or rotate its key and download a fresh config if a computer is replaced.

`Admin -> Downloads` links to the Google Drive `FTADLL.exe` installer. Install it on each FTA-connected QC Station computer before running RealDll mode, then use `Admin -> QC Stations` to download that computer's station-specific setup package. This setup supports 20+ station computers without sharing one secret across all of them.

Keep setup packages private. Each package contains the raw station API key. If a package is lost or exposed, rotate that station's key in `Admin -> QC Stations` and download a new package.

## Publishing The WinForms Payload

The Render web app cannot build the Windows QC Station app during a download request. The Docker build publishes the WinForms x86 payload into the web app before publishing the dashboard. For local or manual deployment, publish the payload directly into the web app payload folder:

```powershell
.\scripts\publish-qcstation-winforms-x86.ps1
```

The script publishes `src\CropQc.QcStation.WinForms\CropQc.QcStation.WinForms.csproj` in Release mode for `win-x86` and writes the output to `src\CropQc.Web\App_Data\QcStationWinForms`, which the web project copies to publish output. The Admin QC Stations page shows whether that payload is present. If it is missing, full setup package buttons are disabled and station setup packages cannot be generated until the payload is deployed.

## Quit / Disconnect Behavior

Use `Quit/Disconnect FTA` before closing the WinForms harness when possible. The button stops continuous capture, calls `FTACancel`, then calls `FTAQuit`, marks the local station status as disconnected/not initialized, and logs each cleanup step.

Closing the WinForms app also attempts the same cleanup automatically. Shutdown cleanup errors are logged and ignored so the app can close without crashing. If the FTA interface appears stuck after a failed disconnect, power-cycle the FTA and restart the QC Station harness before initializing again.

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
- Google Drive upload.
- Actual QC Summary email sending.
- Backend database sync.
- Offline queue persistence.
- Storage inventory.
- Mexico qualification.
- Room controller imports.
- Packout imports.
- Pool closing imports.
- Long-term analytics.
