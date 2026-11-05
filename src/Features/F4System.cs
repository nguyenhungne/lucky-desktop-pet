// F4 – low battery and machine overload alerts.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace LuckyPet
{
    partial class PetForm
    {
        [StructLayout(LayoutKind.Sequential)] struct FILETIME_ { public uint Lo, Hi; public ulong V { get { return ((ulong)Hi << 32) | Lo; } } }
        [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out FILETIME_ idle, out FILETIME_ kernel, out FILETIME_ user);
        [StructLayout(LayoutKind.Sequential)]
        class MEMORYSTATUSEX
        {
            public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            public uint dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX m);

        int f4Last, overloadSince = -1, overloadCalmSince = -1, overloadCooldownUntil, lastCriticalNag = -600000;
        int batteryState;   // 0 ok, 1 low, 2 critical
        ulong pIdle, pKernel, pUser;
        int cpuPct, ramPct, battPct = 100;

        void F4Tick()
        {
            int now = Environment.TickCount;
            if (now - f4Last < 2000) return;
            f4Last = now;

            // CPU / RAM
            FILETIME_ i, k, u;
            if (GetSystemTimes(out i, out k, out u))
            {
                ulong di = i.V - pIdle, dk = k.V - pKernel, du = u.V - pUser;
                if (pKernel != 0 && dk + du > 0) cpuPct = (int)(100 - di * 100 / (dk + du));
                pIdle = i.V; pKernel = k.V; pUser = u.V;
            }
            var ms = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(ms)) ramPct = (int)ms.dwMemoryLoad;

            // battery
            var ps = SystemInformation.PowerStatus;
            bool hasBatt = (ps.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) == 0 && ps.BatteryLifePercent <= 1f;
            bool plugged = ps.PowerLineStatus == PowerLineStatus.Online;
            battPct = (int)Math.Round(ps.BatteryLifePercent * 100);
            bool lp = hasBatt && !plugged && battPct < 20;
            if (lp != lowPower) { lowPower = lp; timer.Interval = lp ? timer.Interval * 2 : Math.Max(40, timer.Interval / 2); }

            if (!cfg.F4 || cfg.Dnd) { ClearMode("battery"); ClearMode("overload"); batteryState = 0; return; }

            if (hasBatt && !plugged && battPct <= cfg.BatteryCritical)
            {
                if (batteryState != 2 || now - lastCriticalNag > 5 * 60000)
                {
                    batteryState = 2; lastCriticalNag = now;
                    SetLoop("battery", P_ALERT, "battery_critical");
                    Say("Pin còn " + battPct + "%! Cắm sạc gấp nha", 6000, true);
                }
            }
            else if (hasBatt && !plugged && battPct <= cfg.BatteryLow)
            {
                if (batteryState == 0)
                {
                    batteryState = 1;
                    SetLoop("battery", P_ALERT, "battery_hungry");
                    Say("Lucky đói pin quá… cắm sạc giúp nha (" + battPct + "%)", 6000, true);
                }
                else if (batteryState == 2) { batteryState = 1; SetLoop("battery", P_ALERT, "battery_hungry"); }
            }
            else if (batteryState != 0 && (plugged || !hasBatt))
            {
                batteryState = 0;
                ClearMode("battery");
                Request("charged", P_ALERT, "battery_charged", true);
                Say("Có điện rồi! Cảm ơn bạn ⚡", 3500, true);
            }

            // overload
            bool over = cpuPct > cfg.OverloadPct || ramPct > cfg.OverloadPct;
            if (over)
            {
                overloadCalmSince = -1;
                if (overloadSince < 0) overloadSince = now;
                if (now - overloadSince >= cfg.OverloadSec * 1000 && now - overloadCooldownUntil > 0 && !HasMode("overload"))
                {
                    SetLoop("overload", P_ALERT + 1, "overload_sweat");
                    Ask("Máy đang quá tải (CPU " + cpuPct + "%, RAM " + ramPct + "%)", 60000,
                        Btn("Xem tiến trình", ShowTopProcesses),
                        Btn("Ẩn", () => { overloadCooldownUntil = Environment.TickCount + cfg.AlertCooldownMin * 60000; ClearMode("overload"); }));
                }
            }
            else
            {
                overloadSince = -1;
                if (HasMode("overload"))
                {
                    if (overloadCalmSince < 0) overloadCalmSince = now;
                    if (now - overloadCalmSince > 30000) { ClearMode("overload"); overloadCooldownUntil = now + cfg.AlertCooldownMin * 60000; }
                }
            }
        }

        // Shows (never kills) the three heaviest processes, sampled over one second off the UI thread
        void ShowTopProcesses()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                var first = new Dictionary<int, TimeSpan>();
                foreach (var p in Process.GetProcesses()) { try { first[p.Id] = p.TotalProcessorTime; } catch { } }
                Thread.Sleep(1000);
                var rows = new List<KeyValuePair<double, string>>();
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        TimeSpan t0;
                        double cpu = first.TryGetValue(p.Id, out t0) ? (p.TotalProcessorTime - t0).TotalMilliseconds / 10.0 / Environment.ProcessorCount : 0;
                        double mb = p.WorkingSet64 / 1048576.0;
                        rows.Add(new KeyValuePair<double, string>(cpu * 50 + mb, p.ProcessName + "  ·  CPU " + cpu.ToString("0") + "%  ·  RAM " + mb.ToString("0") + " MB"));
                    }
                    catch { }
                }
                rows.Sort((a, b) => b.Key.CompareTo(a.Key));
                var sb = new StringBuilder("Top 3 tiến trình đang tốn tài nguyên nhất:\n\n");
                for (int n = 0; n < Math.Min(3, rows.Count); n++) sb.Append(n + 1).Append(". ").Append(rows[n].Value).Append('\n');
                sb.Append("\nLucky chỉ hiển thị, không tự đóng tiến trình nào.");
                BeginInvoke(new Action(() => MessageBox.Show(sb.ToString(), "Lucky – máy đang quá tải", MessageBoxButtons.OK, MessageBoxIcon.Information)));
            });
        }
    }
}
