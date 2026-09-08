using System;
using System.Collections.Generic;
using System.Management;
using System.Threading;

namespace LightControl
{
    internal sealed class Display
    {
        public string Name;
        public bool Internal;

        public IntPtr Handle = IntPtr.Zero;
        public uint Min, Max;

        public string WmiInstance;

        public int Value;
        public int Pending = -1;
        public int Applied = -1;

        public bool IsWmi { get { return WmiInstance != null; } }
    }

    internal sealed class MonitorManager : IDisposable
    {
        private readonly object _gate = new object();
        private List<Display> _displays = new List<Display>();
        private readonly AutoResetEvent _signal = new AutoResetEvent(false);
        private readonly Thread _worker;
        private volatile bool _stop;

        public MonitorManager()
        {
            _worker = new Thread(WorkerLoop);
            _worker.IsBackground = true;
            _worker.Start();
        }

        public List<Display> Displays { get { lock (_gate) return _displays; } }

        public void Refresh()
        {
            List<Display> found = new List<Display>();
            Dictionary<string, TargetInfo> targets = QueryDisplayNames();

            Native.MonitorEnumProc cb = delegate(IntPtr hMon, IntPtr hdc, ref Native.RECT r, IntPtr data)
            {
                Native.MONITORINFOEX mi = new Native.MONITORINFOEX();
                mi.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.MONITORINFOEX));
                string device = Native.GetMonitorInfo(hMon, ref mi) ? mi.szDevice : null;

                TargetInfo ti = new TargetInfo();
                if (device != null) targets.TryGetValue(device, out ti);

                uint count = 0;
                if (!Native.GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, ref count) || count == 0) return true;

                Native.PHYSICAL_MONITOR[] phys = new Native.PHYSICAL_MONITOR[count];
                if (!Native.GetPhysicalMonitorsFromHMONITOR(hMon, count, phys)) return true;

                for (int i = 0; i < phys.Length; i++)
                {
                    uint min = 0, cur = 0, max = 0;
                    if (Native.GetMonitorBrightness(phys[i].hPhysicalMonitor, ref min, ref cur, ref max) && max > min)
                    {
                        Display d = new Display();
                        d.Handle = phys[i].hPhysicalMonitor;
                        d.Min = min;
                        d.Max = max;
                        d.Internal = ti.Internal;
                        d.Name = Pick(ti.Name, phys[i].szDescription, device);
                        d.Value = ToPercent(cur, min, max);
                        d.Applied = d.Value;
                        found.Add(d);
                    }
                    else
                    {
                        Native.DestroyPhysicalMonitor(phys[i].hPhysicalMonitor);
                    }
                }
                return true;
            };

            try { Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, cb, IntPtr.Zero); }
            catch { }

            List<Display> wmi = QueryWmi(targets);
            if (wmi.Count > 0)
            {
                for (int i = found.Count - 1; i >= 0; i--)
                    if (found[i].Internal) { Native.DestroyPhysicalMonitor(found[i].Handle); found.RemoveAt(i); }
                found.InsertRange(0, wmi);
            }

            List<Display> old;
            lock (_gate) { old = _displays; _displays = found; }
            ReleaseHandles(old);
        }

        private static string Pick(string a, string b, string c)
        {
            if (!string.IsNullOrEmpty(a)) return a.Trim();
            if (!string.IsNullOrEmpty(b)) return b.Trim();
            if (!string.IsNullOrEmpty(c)) return c.Replace(@"\\.\", "");
            return "Display";
        }

        private struct TargetInfo { public string Name; public bool Internal; }

        private static Dictionary<string, TargetInfo> QueryDisplayNames()
        {
            Dictionary<string, TargetInfo> map = new Dictionary<string, TargetInfo>(StringComparer.OrdinalIgnoreCase);
            try
            {
                uint numPath, numMode;
                if (Native.GetDisplayConfigBufferSizes(Native.QDC_ONLY_ACTIVE_PATHS, out numPath, out numMode) != 0) return map;

                Native.PATH_INFO[] paths = new Native.PATH_INFO[numPath];
                Native.MODE_INFO[] modes = new Native.MODE_INFO[numMode];
                if (Native.QueryDisplayConfig(Native.QDC_ONLY_ACTIVE_PATHS, ref numPath, paths, ref numMode, modes, IntPtr.Zero) != 0)
                    return map;

                for (int i = 0; i < numPath; i++)
                {
                    Native.SOURCE_DEVICE_NAME src = new Native.SOURCE_DEVICE_NAME();
                    src.header.type = Native.DEVICE_INFO_GET_SOURCE_NAME;
                    src.header.size = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.SOURCE_DEVICE_NAME));
                    src.header.adapterId = paths[i].sourceInfo.adapterId;
                    src.header.id = paths[i].sourceInfo.id;
                    if (Native.DisplayConfigGetDeviceInfo(ref src) != 0) continue;

                    Native.TARGET_DEVICE_NAME tgt = new Native.TARGET_DEVICE_NAME();
                    tgt.header.type = Native.DEVICE_INFO_GET_TARGET_NAME;
                    tgt.header.size = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.TARGET_DEVICE_NAME));
                    tgt.header.adapterId = paths[i].targetInfo.adapterId;
                    tgt.header.id = paths[i].targetInfo.id;
                    if (Native.DisplayConfigGetDeviceInfo(ref tgt) != 0) continue;

                    uint tech = tgt.outputTechnology;
                    TargetInfo info;
                    info.Internal = tech == Native.OUTPUT_TECHNOLOGY_INTERNAL
                                 || tech == Native.OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED
                                 || tech == Native.OUTPUT_TECHNOLOGY_UDI_EMBEDDED;
                    info.Name = info.Internal && string.IsNullOrEmpty(tgt.monitorFriendlyDeviceName)
                        ? "Built-in display" : tgt.monitorFriendlyDeviceName;
                    map[src.viewGdiDeviceName] = info;
                }
            }
            catch { }
            return map;
        }

        private static List<Display> QueryWmi(Dictionary<string, TargetInfo> targets)
        {
            List<Display> list = new List<Display>();
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(@"root\WMI",
                    "SELECT InstanceName, CurrentBrightness FROM WmiMonitorBrightness"))
                using (ManagementObjectCollection results = s.Get())
                {
                    foreach (ManagementObject mo in results)
                    {
                        Display d = new Display();
                        d.WmiInstance = Convert.ToString(mo["InstanceName"]);
                        d.Internal = true;
                        d.Value = Clamp(Convert.ToInt32(mo["CurrentBrightness"]));
                        d.Applied = d.Value;
                        d.Name = "Built-in display";
                        foreach (KeyValuePair<string, TargetInfo> kv in targets)
                            if (kv.Value.Internal && !string.IsNullOrEmpty(kv.Value.Name)) { d.Name = kv.Value.Name; break; }
                        list.Add(d);
                        mo.Dispose();
                    }
                }
            }
            catch { }
            return list;
        }

        public void RefreshValues()
        {
            List<Display> snapshot;
            lock (_gate) snapshot = _displays;

            Dictionary<string, int> wmi = null;
            for (int i = 0; i < snapshot.Count; i++)
            {
                Display d = snapshot[i];
                if (d.Pending >= 0 && d.Pending != d.Applied) continue;

                try
                {
                    if (d.IsWmi)
                    {
                        if (wmi == null) wmi = ReadWmiValues();
                        int v;
                        if (wmi.TryGetValue(d.WmiInstance, out v)) { d.Value = v; d.Applied = v; d.Pending = -1; }
                    }
                    else
                    {
                        uint min = 0, cur = 0, max = 0;
                        if (Native.GetMonitorBrightness(d.Handle, ref min, ref cur, ref max) && max > min)
                        {
                            d.Min = min; d.Max = max;
                            int v = ToPercent(cur, min, max);
                            d.Value = v; d.Applied = v; d.Pending = -1;
                        }
                    }
                }
                catch { }
            }
        }

        private static Dictionary<string, int> ReadWmiValues()
        {
            Dictionary<string, int> map = new Dictionary<string, int>();
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(@"root\WMI",
                    "SELECT InstanceName, CurrentBrightness FROM WmiMonitorBrightness"))
                using (ManagementObjectCollection results = s.Get())
                    foreach (ManagementObject mo in results)
                    {
                        map[Convert.ToString(mo["InstanceName"])] = Clamp(Convert.ToInt32(mo["CurrentBrightness"]));
                        mo.Dispose();
                    }
            }
            catch { }
            return map;
        }

        public void Set(Display d, int percent)
        {
            d.Value = Clamp(percent);
            Thread.VolatileWrite(ref d.Pending, d.Value);
            _signal.Set();
        }

        private void WorkerLoop()
        {
            while (!_stop)
            {
                _signal.WaitOne();
                while (!_stop)
                {
                    bool worked = false;
                    List<Display> snapshot;
                    lock (_gate) snapshot = _displays;

                    for (int i = 0; i < snapshot.Count; i++)
                    {
                        Display d = snapshot[i];
                        int want = Thread.VolatileRead(ref d.Pending);
                        if (want < 0 || want == d.Applied) continue;
                        Apply(d, want);
                        d.Applied = want;
                        worked = true;
                    }
                    if (!worked) break;
                }
            }
        }

        private static void Apply(Display d, int percent)
        {
            try
            {
                if (d.IsWmi) SetWmi(d.WmiInstance, percent);
                else Native.SetMonitorBrightness(d.Handle, (uint)(d.Min + (d.Max - d.Min) * percent / 100.0 + 0.5));
            }
            catch { }
        }

        private static void SetWmi(string instance, int percent)
        {
            string escaped = instance.Replace("\\", "\\\\").Replace("'", "\\'");
            using (ManagementObjectSearcher s = new ManagementObjectSearcher(@"root\WMI",
                "SELECT * FROM WmiMonitorBrightnessMethods WHERE InstanceName='" + escaped + "'"))
            using (ManagementObjectCollection results = s.Get())
            {
                foreach (ManagementObject mo in results)
                {
                    try { mo.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)percent }); }
                    catch { }
                    mo.Dispose();
                }
            }
        }

        private static int ToPercent(uint cur, uint min, uint max)
        {
            if (max <= min) return 0;
            return Clamp((int)Math.Round((cur - (double)min) * 100.0 / (max - min)));
        }

        internal static int Clamp(int v) { return v < 0 ? 0 : (v > 100 ? 100 : v); }

        private static void ReleaseHandles(List<Display> list)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
                if (!list[i].IsWmi && list[i].Handle != IntPtr.Zero)
                    try { Native.DestroyPhysicalMonitor(list[i].Handle); } catch { }
        }

        public void Dispose()
        {
            _stop = true;
            _signal.Set();
            List<Display> old;
            lock (_gate) { old = _displays; _displays = new List<Display>(); }
            ReleaseHandles(old);
        }
    }
}
