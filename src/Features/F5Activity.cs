// F5 – reacts to the rhythm of keyboard / mouse use. Only counts events: key contents are never read.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace LuckyPet
{
    partial class PetForm
    {
        delegate IntPtr LLProc(int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, LLProc fn, IntPtr hMod, uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int n, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);

        static LLProc kbProc, msProc;          // kept alive for the lifetime of the hooks
        IntPtr kbHook, msHook;
        readonly Queue<int> keyTimes = new Queue<int>(), clickTimes = new Queue<int>();
        int lastWheel = -100000, typingFastSince = -1, lastStartle = -100000, lastSaved = -100000, f5Last, lastFastBurst = -100000, comboKeys;
        bool ctrlS, idleSleeping, yawned;

        static uint IdleMs()
        {
            var li = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
            if (!GetLastInputInfo(ref li)) return 0;
            return unchecked((uint)Environment.TickCount - li.dwTime);
        }

        void F5Install()
        {
            if (cfg == null || (!cfg.F5 && !cfg.F7) || kbHook != IntPtr.Zero) return;
            kbProc = KbHook; msProc = MsHook;
            IntPtr hm = GetModuleHandle(null);
            kbHook = SetWindowsHookEx(13, kbProc, hm, 0);   // WH_KEYBOARD_LL
            msHook = SetWindowsHookEx(14, msProc, hm, 0);   // WH_MOUSE_LL
        }

        void F5Uninstall()
        {
            if (kbHook != IntPtr.Zero) { UnhookWindowsHookEx(kbHook); kbHook = IntPtr.Zero; }
            if (msHook != IntPtr.Zero) { UnhookWindowsHookEx(msHook); msHook = IntPtr.Zero; }
        }

        IntPtr KbHook(int n, IntPtr w, IntPtr l)
        {
            if (n >= 0)
            {
                int msg = w.ToInt32();
                if (msg == 0x100 || msg == 0x104)
                {
                    keyTimes.Enqueue(Environment.TickCount);
                    comboKeys++;
                    int vk = Marshal.ReadInt32(l);
                    if (vk == 0x53 && (GetAsyncKeyState(0x11) & 0x8000) != 0) ctrlS = true;   // Ctrl+S (only the fact it happened)
                }
            }
            return CallNextHookEx(kbHook, n, w, l);
        }

        IntPtr MsHook(int n, IntPtr w, IntPtr l)
        {
            if (n >= 0)
            {
                int msg = w.ToInt32();
                if (msg == 0x201 || msg == 0x204) clickTimes.Enqueue(Environment.TickCount);
                else if (msg == 0x20A || msg == 0x20E) lastWheel = Environment.TickCount;
            }
            return CallNextHookEx(msHook, n, w, l);
        }

        void F5Tick()
        {
            int now = Environment.TickCount;
            while (keyTimes.Count > 0 && now - keyTimes.Peek() > 30000) keyTimes.Dequeue();
            while (clickTimes.Count > 0 && now - clickTimes.Peek() > 2000) clickTimes.Dequeue();
            if (!cfg.F5) { ClearMode("activity"); return; }
            if (now - f5Last < 300) return;
            f5Last = now;

            if (clickTimes.Count >= 8 && now - lastStartle > 10000) { lastStartle = now; clickTimes.Clear(); Request("react", P_REACT, "activity_startle", true); }
            if (ctrlS) { ctrlS = false; if (now - lastSaved > 30000) { lastSaved = now; Request("react", P_REACT, "activity_saved", true); Say("Đã lưu ✓", 1500, false); } }
            // idle-game style typing feedback: +1 XP every 100 keystrokes
            if (comboKeys >= 100) { comboKeys -= 100; Reward(1, 0); floaters.Add(new Floater { Text = "⌨ x100", X = HeadX(70) - 10, Y = 52, T0 = now + 300, C = System.Drawing.Color.FromArgb(255, 226, 120) }); }
            // cursor comes close -> turn the head and look at it (any calm pose)
            var cp = System.Windows.Forms.Cursor.Position;
            double hx = x + (dir > 0 ? 66 : CW - 66) * S, hy = y + 47 * S;
            double cd = Math.Sqrt((cp.X - hx) * (cp.X - hx) + (cp.Y - hy) * (cp.Y - hy));
            if (cd < 220 * dpiScale && !interactActive && st != St.Drag) SetLoop("look", P_REACT + 1, "activity_look");
            else ClearMode("look");

            uint idle = IdleMs();
            if (idle >= 10 * 60000)
            {
                if (!idleSleeping) { idleSleeping = true; SetLoop("activity", P_ACTIVITY, "sleep"); }
                return;
            }
            if (idleSleeping)
            {
                idleSleeping = false; ClearMode("activity");
                Request("react", P_REACT, "activity_wake", true);
                Say("Bạn về rồi! Chào nha", 3000, false);
            }
            if (idle >= 3 * 60000)
            {
                ClearMode("activity");
                if (!yawned) { yawned = true; Request("react", P_REACT, "activity_idle_yawn", true); }
                return;
            }
            yawned = false;

            double kpm = keyTimes.Count * 2.0;
            if (kpm >= cfg.FastKpm) { if (typingFastSince < 0) typingFastSince = now; }
            else typingFastSince = -1;
            bool fast = typingFastSince >= 0 && now - typingFastSince >= cfg.FastSec * 1000;
            if (fast && now - lastFastBurst > 45000)
            {
                lastFastBurst = now;
                Request("react", P_REACT, "activity_typing_fast", false, double.NaN, null, 2500);   // short cheer, even over the work context
                Say(new[] { "Gõ nhanh quá! ✦", "Đỉnh thật sự!", "Tay bạn bốc lửa luôn!" }[rnd.Next(3)], 2500, false);
            }
            if (fast) SetLoop("activity", P_ACTIVITY, "activity_typing_fast");
            else if (keyTimes.Count >= 10) SetLoop("activity", P_ACTIVITY, "activity_working");
            else ClearMode("activity");
        }
    }
}
