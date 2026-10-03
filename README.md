# DiskSpaceAnalyzer

A lightweight Windows desktop application for exploring disk usage, reviewing installed applications, and identifying cleanup candidates. The user interface is currently in Turkish.

## Features

- Scan a selected drive and list the largest folders.
- Group folder results using heuristic system, hardware, game, application, and user-data labels.
- Review installed applications, their detected drive, and available size estimates.
- Open an application's installation folder or the Windows Installed apps settings page.
- Review cleanup suggestions and explicitly confirm before deleting suggested temporary-file contents.
- Keep completed scan results locally per drive between application launches.

## Requirements

- Windows
- .NET 10 SDK

## Run

From the repository directory:

```powershell
dotnet run --project .\DiskAlanAnaliz.csproj
```

Folder tags and application sizes are best-effort estimates based on available file-system and Windows uninstall registry information. Access restrictions may result in partial scan results.

Scan history is stored locally at `%LOCALAPPDATA%\DiskAlanAnaliz\scan-cache.json`.
