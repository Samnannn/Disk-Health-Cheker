# Disk Health Lite

Lightweight Windows disk health monitoring utility inspired by Hard Disk Sentinel — focused on clean design, essential SMART information, and zero unnecessary bloat.

---

## Features

- Real-time disk health monitoring
- SSD/HDD/NVMe support
- Health percentage calculation
- Temperature monitoring
- Power-on hours tracking
- Lifetime writes / total host writes
- SSD wear-level information
- Automatic refresh every 10 seconds
- Detects both internal and external drives
- Portable release (no installation required)

---

## Download

Download the latest portable build from the GitHub Releases page:

➡️ **Releases → Download `DiskHealthLite-portable.zip`**

After downloading:

1. Extract the ZIP file
2. Open the extracted folder
3. Run:

```text
Run-DiskHealthLite.bat
```

The launcher automatically starts the newest published version of `DiskHealthLite.exe`.

---

## Screenshots

_Add screenshots here later_

---

## Requirements

- Windows 10 / Windows 11
- Administrator permission recommended
- .NET Runtime (only required for source build)

The app requests Administrator access because Windows storage APIs usually require elevation for SMART and reliability data.

---

## How Health Is Calculated

For SSDs/drives that expose wear information:

```text
Health % = 100 - Wear
```

Example:

```text
Wear = 7
Health = 93%
```

If wear data is unavailable but Windows reports the drive as healthy, the application falls back to:

```text
Health = 100%
```

---

## SMART & Data Notes

Windows does not expose identical SMART information for every drive.

Some USB adapters and external enclosures may hide:

- temperature
- wear values
- power-on hours
- lifetime writes
- reliability counters

When unavailable, the app displays:

```text
Not reported
```

---

## smartctl Fallback Support

If `smartctl.exe` from Smartmontools is installed, Disk Health Lite automatically attempts fallback detection for:

- power-on hours
- lifetime writes
- additional SMART information

The app searches for:

```text
smartctl.exe
```

in:

- System PATH
- `C:\Program Files\smartmontools\bin\`
- bundled portable directories

---

## Building From Source

Clone the repository:

```bash
git clone https://github.com/Samnannn/Disk-Health-Checker.git
```

Open the project in Visual Studio and build:

```text
DiskHealthLite.sln
```

Or run directly using:

```bash
dotnet run
```

---

## Project Structure

```text
DiskHealthLite/        → Main WinForms application
release/               → Portable release ZIPs
Run-DiskHealthLite.bat → Smart launcher
DiskHealthLite.ps1     → PowerShell fallback version
```

---

## Why This Project Exists

Most disk monitoring tools are either:

- too heavy
- outdated
- filled with ads
- or overly complex

Disk Health Lite was built to provide:

- fast startup
- clean UI
- essential health metrics only
- portable usage
- lightweight resource consumption

---

## License

MIT License

---

## Author

Developed by Samnan
```
