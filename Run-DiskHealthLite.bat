@echo off
setlocal
if exist "%~dp0release\DiskHealthLite\DiskHealthLite.exe" (
  start "" "%~dp0release\DiskHealthLite\DiskHealthLite.exe"
  exit /b
)

if exist "%~dp0publish-v6\DiskHealthLite.exe" (
  start "" "%~dp0publish-v6\DiskHealthLite.exe"
  exit /b
)

if exist "%~dp0publish-v5\DiskHealthLite.exe" (
  start "" "%~dp0publish-v5\DiskHealthLite.exe"
  exit /b
)

if exist "%~dp0publish-v4\DiskHealthLite.exe" (
  start "" "%~dp0publish-v4\DiskHealthLite.exe"
  exit /b
)

if exist "%~dp0publish-v3\DiskHealthLite.exe" (
  start "" "%~dp0publish-v3\DiskHealthLite.exe"
  exit /b
)

if exist "%~dp0publish-v2\DiskHealthLite.exe" (
  start "" "%~dp0publish-v2\DiskHealthLite.exe"
  exit /b
)

if exist "%~dp0publish-latest\DiskHealthLite.exe" (
  start "" "%~dp0publish-latest\DiskHealthLite.exe"
  exit /b
)

if exist "%~dp0publish\DiskHealthLite.exe" (
  start "" "%~dp0publish\DiskHealthLite.exe"
  exit /b
)

set "DOTNET=C:\Program Files\dotnet\dotnet.exe"
if not exist "%DOTNET%" set "DOTNET=dotnet"
"%DOTNET%" run --project "%~dp0DiskHealthLite\DiskHealthLite.csproj"
