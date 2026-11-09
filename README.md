# lighting control for pc v1

A Windows system-tray application for adjusting the brightness of supported displays.

- Shows brightness controls for detected displays.
- Uses Windows monitor controls and WMI for supported built-in displays.
- Refreshes display information when displays change or the computer resumes.
- Offers an optional start-with-Windows setting.

The application is written in C# with Windows Forms. Display support depends on the monitor and its drivers.

## Build

On Windows, run `build.cmd` with the .NET Framework compiler available. The executable is written to `build/LightControl.exe`.
