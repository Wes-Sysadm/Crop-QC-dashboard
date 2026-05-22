# QC Station FTA Proof of Concept

This document describes the first Windows QC Station proof of concept for integrating with a GUSS/FTA Fruit Texture Analyzer. This is a local harness only. It does not sync readings to Azure SQL, write readings into the QC sample grid, capture USB camera photos, upload files to SharePoint, or send email.

## Purpose

The POC gives the QC Station project a safe place to test:

- FTA device initialization and status flow.
- Pressure reading capture through a clean C# abstraction.
- Mock/test mode without FTA hardware.
- A real DLL wrapper placeholder that can be completed on the QC computer later.
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

## Configuration

The current settings fields are:

- `StationName`
- `WarehouseCode`
- `FtaMode`: `Mock` or `RealDll`
- `FtaDllPath`
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

Real DLL mode is isolated in `FtaDllPressureReader`. This is the only class that should later load or call the vendor FTA DLL.

Expected DLL files later:

```text
FTA_DLL.dll
borlndmm.dll
```

Place both files in the configured `FtaDllPath`. If either file is missing, the harness reports a clear error and does not crash.

The vendor function declarations and calls are intentionally TODOs until the actual DLL can be tested on the QC computer connected to the GUSS/FTA.

## Test Screen Commands

The local harness displays:

- Station name
- Warehouse
- FTA mode
- DLL path
- COM port
- API base URL placeholder
- Local data path placeholder
- Last pressure reading
- Timestamped log

Available commands:

- Initialize FTA
- Check Status
- Start Pressure Reading
- Get Latest Reading
- Cancel
- Use Mock Reading
- Return Probe Home
- Clear Log

## Hardware Testing Still Required

Real hardware testing must happen on the QC computer connected to the GUSS/FTA.

Before production use, test and confirm:

- Correct `FTA_DLL.dll` function names and calling conventions.
- Whether `borlndmm.dll` must be loaded before `FTA_DLL.dll`.
- Required bitness, such as x86 versus x64.
- COM port or other device connection requirements.
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
