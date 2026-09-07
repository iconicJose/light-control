@echo off
setlocal
cd /d "%~dp0"

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" echo .NET Framework compiler not found. & exit /b 1

if not exist "src\app.ico" powershell -NoProfile -ExecutionPolicy Bypass -File "tools\make-icon.ps1" >nul

if not exist "build" mkdir "build"

"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /debug- ^
  /out:"build\LightControl.exe" ^
  /win32manifest:"src\app.manifest" ^
  /win32icon:"src\app.ico" ^
  /reference:System.dll ^
  /reference:System.Core.dll ^
  /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll ^
  /reference:System.Management.dll ^
  src\Native.cs src\Monitors.cs src\App.cs

if errorlevel 1 exit /b 1
echo Built build\LightControl.exe
