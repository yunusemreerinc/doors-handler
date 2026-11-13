# doors-handler

A Windows desktop app (WPF, .NET 8) that connects to the IBM DOORS (Classic, 9.x) client and **lists the modules** in its database.

## Features

- Connects to the DOORS client running on the same machine through COM, and shows the user, database and DOORS version.
- Lists **formal, link and descriptive** modules, starting from a chosen folder or project (or `/` for the whole database), optionally including subfolders.
- Filter by type (all modules / formal / link / descriptive / folders) and search by name, path, description or unique ID as you type.
- Double-click a folder or project to open it, or a module to copy its full path; `↑` goes up one folder.
- **Export the contents of selected modules as JSON**: select one or more formal modules with Ctrl/Shift; each module is opened read-only in DOORS and all its objects (heading, text, level, parent, created/modified info and user-defined attributes) are written to a single JSON file. See below.
- Export the visible list as CSV (UTF-8 with BOM so Excel shows non-ASCII characters correctly; the column separator follows the Windows list separator, so Excel splits the columns).
- Folders you have no access to don't stop the scan; they are reported as warnings.
- If connecting or listing fails, an error banner explains **what happened**, **why** and **what to do**, with *Retry*, *Copy details* (to send to your support team) and technical details (HRESULT, privileges, architecture).
- If DOORS doesn't respond within two minutes (for example because its login window is hidden), the connection times out; you can also click *Cancel* while it waits.
- **Light / dark theme**: the *Theme* list at the top right offers *System* (follows the Windows light/dark mode setting and updates immediately when it changes), *Light* and *Dark*; the window title bar follows the theme too. The choice is saved in `%LOCALAPPDATA%\DoorsHandler\settings.json`.
- **Demo mode**: try the app with sample data on a machine without DOORS.

## Requirements

- Windows 10/11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (the .NET 8 SDK for development)
- For development in Visual Studio: **Visual Studio 2022 version 17.8 or later** with the *.NET desktop development* workload. Visual Studio 2019 cannot build .NET 8 projects (error `NETSDK1045`).
- A licensed **IBM DOORS 9.x client** installed on the same machine (not needed for demo mode)

## Running

```bash
dotnet run --project src/DoorsHandler.Desktop
```

With demo data:

```bash
dotnet run --project src/DoorsHandler.Desktop -- --demo
```

To build a folder you can distribute:

```bash
dotnet publish src/DoorsHandler.Desktop -c Release -r win-x64 --self-contained false -o publish
```

Tests (run on Windows, Linux or macOS):

```bash
dotnet test
```

In Visual Studio, open `DoorsHandler.sln` and set **DoorsHandler.Desktop** as the startup project.

## Usage

1. With *IBM DOORS (local client)* selected under **Connection**, click **Connect**.
   If DOORS isn't running, Windows starts it and DOORS shows its own login window; the app connects once you have logged in.
2. Once connected, the path in **Root folder** (default `/`) is listed automatically.
3. To list somewhere else, type a path and press Enter (e.g. `/Vehicle Project/Requirements`), or double-click a folder in the list.
4. Scanning the whole tree of a large database can take a while; untick *Include subfolders* to list only the contents of that folder.

## JSON export

1. Select one or more **formal** modules in the list (Ctrl for individual rows, Shift for a range, Ctrl+A for all).
2. Click **Export selected modules to JSON…** (also in the right-click menu) and choose a file name.
3. Modules are read one by one; the status bar shows which module is being read, and *Cancel* stops the export (no file is written if you cancel).

Behaviour:

- Modules are opened **read-only and hidden** in DOORS and closed when reading is done. A module you already have open is left alone and stays open.
- View filters are ignored; **all objects** except soft-deleted ones are written in document order.
- Link and descriptive modules are skipped (they have no object content); the status bar says how many items were skipped.
- A module that can't be opened (deleted, no permission, locked) doesn't stop the export: the JSON gets an `error` field for that module, and it is listed under warnings. If no module can be read, no file is written and the error banner explains why.
- If the connection to DOORS is lost, the export stops and no file is written.

File format (UTF-8, non-ASCII characters written as-is):

```json
{
  "formatVersion": 1,
  "exportedAt": "2026-11-12T22:40:00+03:00",
  "database": "ProdDB",
  "exportedBy": "jdoe",
  "doorsVersion": "9.7.2.3",
  "modules": [
    {
      "name": "Software Requirements",
      "fullName": "/Vehicle Project/01 Requirements/Software Requirements",
      "uniqueId": "00001a2b",
      "description": "SRS",
      "attributeDefinitions": [ { "name": "Priority", "type": "Priority" } ],
      "objectCount": 2,
      "objects": [
        {
          "absoluteNumber": 1, "identifier": "SRS-1", "level": 1,
          "heading": "1 Introduction", "text": "",
          "createdBy": "jdoe", "createdOn": "01.10.2026",
          "lastModifiedBy": "jdoe", "lastModifiedOn": "05.11.2026",
          "attributes": { "Priority": "" }
        },
        {
          "absoluteNumber": 2, "identifier": "SRS-2", "level": 2, "parentAbsoluteNumber": 1,
          "heading": "", "text": "The system shall be ready within 2 seconds of start-up.",
          "createdBy": "jdoe", "createdOn": "01.10.2026",
          "lastModifiedBy": "amy", "lastModifiedOn": "07.11.2026",
          "attributes": { "Priority": "High" }
        }
      ]
    },
    { "name": "Deleted", "fullName": "/Vehicle Project/Deleted", "uniqueId": "…", "error": "Module '/Vehicle Project/Deleted' could not be opened …" }
  ]
}
```

- `level` and `parentAbsoluteNumber` let you rebuild the module tree (top-level objects have no `parentAbsoluteNumber`).
- `attributes` holds the module's **user-defined** object attributes under their DOORS names; system attributes appear only as the fields above.
- `text` is plain text (rich-text formatting and OLE objects are not included). Dates are in the format DOORS displays.

## How it works

The DOORS client provides a COM automation server called `DOORS.Application`. The app attaches to it with late binding, so building it needs no DOORS type library or interop DLL:

1. `runStr(dxl)` runs a DXL script inside DOORS.
2. The script returns its result with `oleSetResult(...)`, which the app reads from the `result` property.
3. The result is a simple tab-separated line protocol (`OK`, `ERR`, `S`, `I`, `W`, … lines); field values are escaped so tabs or line breaks in descriptions can't break the protocol.

COM calls run on a separate STA thread, so the window doesn't freeze while DOORS performs a long scan.

```
src/
  DoorsHandler.Core/        Platform-independent logic (no Windows needed, unit tested)
    Dxl/DxlScripts.cs         DXL scripts sent to DOORS
    Dxl/DxlResultParser.cs    Parsing of the DXL output
    DxlDoorsClient.cs         IDoorsClient: runs the scripts through an IDxlExecutor
    DemoDoorsClient.cs        Sample data for trying the app without DOORS
    CsvExporter.cs            CSV export
    ModuleJsonExporter.cs     Reads module contents and writes JSON
  DoorsHandler.Desktop/     WPF app
    Doors/ComDxlExecutor.cs   DOORS COM bridge (DOORS.Application)
    Doors/StaWorker.cs        Separate STA thread for COM calls
    ViewModels/MainViewModel.cs
    MainWindow.xaml
    Themes/                   Light.xaml / Dark.xaml colour palettes, Controls.xaml control styles
    Theming/ThemeManager.cs   Theme switching, following the Windows setting, dark title bar
tests/
  DoorsHandler.Core.Tests/  xUnit tests
```

To add a new DOORS operation, add the script to `DxlScripts`, its parsing to `DxlResultParser` and a method to `IDoorsClient`; the COM layer doesn't need to change.

## Troubleshooting

When something fails, the app classifies the cause itself and shows it in the error banner. Possible causes:

| Reason in the banner | When it happens | What to do |
| --- | --- | --- |
| DOORS client not found | The `DOORS.Application` COM server is not registered (`REGDB_E_CLASSNOTREG`) | Install or repair DOORS. To try the app, choose *Demo data*. |
| DOORS could not be started | Windows couldn't start DOORS or attach to the running DOORS (`CO_E_SERVER_EXEC_FAILURE`) | Start DOORS yourself and log in, then *Retry*. |
| Access denied | One program runs as administrator and the other doesn't (`E_ACCESSDENIED`) | Run DOORS and this app with the same privileges. |
| DOORS busy | A dialog is open in DOORS, or another DXL script is running (`RPC_E_CALL_REJECTED`) | Close the open windows in DOORS, then *Retry*. |
| Timeout | DOORS didn't respond within 2 minutes; usually its login window is hidden behind other windows | Find the DOORS window and log in, then *Retry*. |
| Connection lost | DOORS was closed or crashed after connecting | Restart DOORS, then *Retry*. |
| DOORS could not run the command | The DXL script failed or returned an empty response; a DXL error window may have opened in DOORS | Note the error text and send it to your support team with *Copy details*. |
| Folder not found | The root folder path is wrong, or you can't see that folder | Check the path (format `/Project/Folder`), or enter `/`. |
| Module could not be opened | During JSON export: the module was deleted, you have no read access, or it is locked exclusively | Refresh the list and try again, or check your access in DOORS. |

The *Technical details* section shows the time, the error kind, the HRESULT, whether the app is 32- or 64-bit, and whether it runs as administrator.

## Out of scope

DOORS Next (DNG, Jazz/ELM) uses OSLC/REST APIs instead of DXL and COM, so it isn't supported in this version. The `IDoorsClient` interface is designed so that an OSLC-based client could be added behind it later.
