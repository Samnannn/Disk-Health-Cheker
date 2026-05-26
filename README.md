# Disk Health Lite

A small Windows disk health monitor inspired by Hard Disk Sentinel, focused only on the essentials:

- disk health percentage
- disk temperature
- power-on time
- lifetime writes
- wear value
- automatic refresh every 10 seconds
- internal and external drives reported by Windows

## Run

Double-click `Run-DiskHealthLite.bat`.

If the app has already been published, the launcher opens the newest published `DiskHealthLite.exe`. Otherwise it runs the .NET project.

The app asks for Administrator permission because Windows usually requires elevation for `Get-StorageReliabilityCounter`.

If your terminal still cannot find `dotnet` after installation, the launcher already tries `C:\Program Files\dotnet\dotnet.exe` directly.

## How Health Is Calculated

For drives that report a wear value:

```text
Health % = 100 - Wear
```

If Windows does not report wear but marks the disk as `Healthy`, the app shows `100%` as a basic Windows status fallback.

## Notes

Windows does not expose the same SMART details for every drive. Some USB/external adapters hide temperature, wear, power-on hours, or lifetime writes. Those fields will show `Not reported` when the drive or adapter does not provide them through Windows storage APIs.

If Smartmontools is installed and `smartctl` is available, the app also tries it as a fallback for power-on time and lifetime writes.

## PowerShell Fallback

`DiskHealthLite.ps1` is kept as a no-build fallback. The main app is the .NET WinForms project in `DiskHealthLite/`.
