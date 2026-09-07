using System;
using System.Runtime.InteropServices;

namespace LightControl
{
    internal static class Native
    {
        // ---------- monitor enumeration ----------

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
        }

        internal delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

        [DllImport("user32.dll")]
        internal static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
        internal static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);

        // ---------- DDC/CI ----------

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct PHYSICAL_MONITOR
        {
            public IntPtr hPhysicalMonitor;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szDescription;
        }

        [DllImport("dxva2.dll", SetLastError = true)]
        internal static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, ref uint count);

        [DllImport("dxva2.dll", SetLastError = true)]
        internal static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint count, [Out] PHYSICAL_MONITOR[] monitors);

        [DllImport("dxva2.dll", SetLastError = true)]
        internal static extern bool GetMonitorBrightness(IntPtr hMonitor, ref uint min, ref uint current, ref uint max);

        [DllImport("dxva2.dll", SetLastError = true)]
        internal static extern bool SetMonitorBrightness(IntPtr hMonitor, uint value);

        [DllImport("dxva2.dll", SetLastError = true)]
        internal static extern bool DestroyPhysicalMonitor(IntPtr hMonitor);

        // ---------- display config (friendly names + internal panel detection) ----------

        internal const uint QDC_ONLY_ACTIVE_PATHS = 2;
        internal const uint DEVICE_INFO_GET_SOURCE_NAME = 1;
        internal const uint DEVICE_INFO_GET_TARGET_NAME = 2;
        internal const uint OUTPUT_TECHNOLOGY_INTERNAL = 0x80000000;
        internal const uint OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED = 11;
        internal const uint OUTPUT_TECHNOLOGY_UDI_EMBEDDED = 13;

        [StructLayout(LayoutKind.Sequential)]
        internal struct LUID { public uint Low; public int High; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RATIONAL { public uint Numerator; public uint Denominator; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PATH_SOURCE_INFO { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PATH_TARGET_INFO
        {
            public LUID adapterId;
            public uint id;
            public uint modeInfoIdx;
            public uint outputTechnology;
            public uint rotation;
            public uint scaling;
            public RATIONAL refreshRate;
            public uint scanLineOrdering;
            public int targetAvailable;
            public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PATH_INFO { public PATH_SOURCE_INFO sourceInfo; public PATH_TARGET_INFO targetInfo; public uint flags; }

        [StructLayout(LayoutKind.Sequential, Size = 64)]
        internal struct MODE_INFO { public uint infoType; public uint id; public LUID adapterId; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct DEVICE_INFO_HEADER { public uint type; public uint size; public LUID adapterId; public uint id; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct SOURCE_DEVICE_NAME
        {
            public DEVICE_INFO_HEADER header;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct TARGET_DEVICE_NAME
        {
            public DEVICE_INFO_HEADER header;
            public uint flags;
            public uint outputTechnology;
            public ushort edidManufactureId;
            public ushort edidProductCodeId;
            public uint connectorInstance;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
        }

        [DllImport("user32.dll")]
        internal static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPath, out uint numMode);

        [DllImport("user32.dll")]
        internal static extern int QueryDisplayConfig(uint flags, ref uint numPath, [Out] PATH_INFO[] paths,
            ref uint numMode, [Out] MODE_INFO[] modes, IntPtr topologyId);

        [DllImport("user32.dll")]
        internal static extern int DisplayConfigGetDeviceInfo(ref SOURCE_DEVICE_NAME info);

        [DllImport("user32.dll")]
        internal static extern int DisplayConfigGetDeviceInfo(ref TARGET_DEVICE_NAME info);

        // ---------- window polish ----------

        [DllImport("dwmapi.dll")]
        internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        internal const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        internal const int DWMWCP_ROUND = 2;

        [DllImport("user32.dll")]
        internal static extern bool DestroyIcon(IntPtr handle);
    }
}
