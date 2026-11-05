// F7 – Lucky never steals clicks: click-through by default, deliberate activation (Alt or 800 ms hover),
// cursor dodging, staying away from caption buttons / the text caret, fading while you move or scroll.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LuckyPet
{
    partial class PetForm
    {
        [StructLayout(LayoutKind.Sequential)]
        struct GUITHREADINFO
        {
            public int cbSize, flags;
            public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
            public RECT rcCaret;
        }
        [StructLayout(LayoutKind.Sequential)] struct PT { public int X, Y; }
        [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint thread, ref GUITHREADINFO info);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref PT p);

        bool interactActive;
        int hoverStart = -1, lastTouch = Environment.TickCount, lastDodge = -100000, f7Slow, fadeUntil;
        Point hoverPt, lastCur; int lastCurT;
        double curSpeed;

        void F7Init()
        {
            ClickThrough = cfg.F7;
            UpdateTrayIcon();
        }

        void F7Off()
        {
            // legacy behaviour: Lucky is a normal clickable window
            if (ClickThrough) ClickThrough = false;
            ballWin.ClickThrough = false; boneWin.ClickThrough = false;
            interactActive = true; PushAlpha = 255;
        }

        bool HitPet(Point c)
        {
            int lx = c.X - (int)x, ly = c.Y - (int)y;
            if (lx < 0 || ly < 0 || lx >= PW || ly >= PH || !Visible) return false;
            try { return canvas.GetPixel(lx, ly).A > 40; } catch { return false; }
        }

        void SetActive(bool on)
        {
            if (interactActive == on) return;
            interactActive = on;
            ClickThrough = !on;
            Cursor = on ? Cursors.Hand : Cursors.Default;
            hoverStart = -1;
            if (on) lastTouch = Environment.TickCount;
        }

        void SetInteract(bool on, bool auto)
        {
            cfg.Interact = on; cfg.Save();
            if (interactItem != null) interactItem.Checked = on;
            SetActive(false);
            Request("interact", P_ACT_EVT - 1, on ? "interact_on" : "interact_off", true);
            Say(on ? "Đã bật tương tác" : (auto ? "Đã tự tắt tương tác" : "Đã tắt tương tác"), 2000, true);
            lastTouch = Environment.TickCount;
            UpdateTrayIcon();
        }

        void F7Tick()
        {
            int now = Environment.TickCount;
            var c = Cursor.Position;
            if (lastCurT != 0 && now != lastCurT)
            {
                double dx = c.X - lastCur.X, dy = c.Y - lastCur.Y;
                curSpeed = Math.Sqrt(dx * dx + dy * dy) * 1000.0 / Math.Max(1, now - lastCurT);
            }
            var prev = lastCur; lastCur = c; lastCurT = now;

            ballWin.ClickThrough = !cfg.Interact;
            boneWin.ClickThrough = true;
            bool over = HitPet(c);

            if (!cfg.Interact) { SetActive(false); hoverStart = -1; }
            else if (!dragging)
            {
                if (over)
                {
                    lastTouch = interactActive ? now : lastTouch;
                    bool alt = (GetAsyncKeyState(0x12) & 0x8000) != 0;
                    if (!interactActive && !cfg.LockPet)
                    {
                        if (alt) SetActive(true);
                        else if (hoverStart < 0 || Math.Abs(c.X - hoverPt.X) + Math.Abs(c.Y - hoverPt.Y) > 4 * dpiScale) { hoverStart = now; hoverPt = c; }
                        else if (now - hoverStart >= 800) SetActive(true);
                    }
                }
                else { hoverStart = -1; if (interactActive) SetActive(false); }

                // dodge a cursor rushing at Lucky (not while showing an alert with buttons)
                if (cfg.Dodge && !interactActive && !over && !AlertActive() && !cfg.LockPet && now - lastDodge > 1500 && curSpeed > 1500 * dpiScale)
                {
                    double cx = x + PW / 2.0, cy = y + PH * 0.6;
                    double d0 = Math.Sqrt((prev.X - cx) * (prev.X - cx) + (prev.Y - cy) * (prev.Y - cy));
                    double d1 = Math.Sqrt((c.X - cx) * (c.X - cx) + (c.Y - cy) * (c.Y - cy));
                    if (d1 < 200 * dpiScale && d1 < d0)
                    {
                        lastDodge = now;
                        int away = c.X < cx ? 1 : -1;
                        var wa = Screen.PrimaryScreen.WorkingArea;
                        x = Math.Max(wa.Left, Math.Min(wa.Right - PW, x + away * 80 * dpiScale));
                        dir = away;
                        Request("dodge", P_ACT_EVT, "dodge", true);
                    }
                }
                if (cfg.AutoOffMin > 0 && now - lastTouch > cfg.AutoOffMin * 60000) SetInteract(false, true);
            }

            // fade to 30 % while a window is being dragged or a page is scrolled
            if (now - lastWheel < 600) fadeUntil = now + 400;
            if (now - f7Slow >= 250)
            {
                f7Slow = now;
                IntPtr fg = GetForegroundWindow();
                uint pid; uint tid = fg == IntPtr.Zero ? 0 : GetWindowThreadProcessId(fg, out pid);
                var gi = new GUITHREADINFO { cbSize = Marshal.SizeOf(typeof(GUITHREADINFO)) };
                if (tid != 0 && GetGUIThreadInfo(tid, ref gi))
                {
                    if ((gi.flags & 0x2) != 0) fadeUntil = now + 400;   // GUI_INMOVESIZE
                    if (!cfg.LockPet && st != St.Drag && now % 1000 < 260) AvoidWorkArea(fg, gi);
                }
            }
            PushAlpha = (byte)(now < fadeUntil ? 77 : 255);
        }

        // keep clear of the active window's caption buttons and of the text caret
        void AvoidWorkArea(IntPtr fg, GUITHREADINFO gi)
        {
            var pet = new Rectangle((int)x, (int)y, PW, PH);
            RECT r;
            if (fg != Handle && GetWindowRect(fg, out r))
            {
                var caption = new Rectangle(r.R - (int)(150 * dpiScale), r.T, (int)(150 * dpiScale), (int)(40 * dpiScale));
                if (caption.IntersectsWith(pet)) MoveAside(caption);
            }
            if (gi.hwndCaret != IntPtr.Zero)
            {
                var p = new PT { X = gi.rcCaret.L, Y = gi.rcCaret.T };
                if (ClientToScreen(gi.hwndCaret, ref p))
                {
                    var caret = new Rectangle(p.X - 4, p.Y - 4, 8 + gi.rcCaret.R - gi.rcCaret.L, 8 + gi.rcCaret.B - gi.rcCaret.T);
                    if (caret.IntersectsWith(pet)) MoveAside(caret);
                }
            }
        }

        void MoveAside(Rectangle obstacle)
        {
            var wa = Screen.PrimaryScreen.WorkingArea;
            double left = obstacle.Left - PW - 10 * dpiScale, right = obstacle.Right + 10 * dpiScale;
            x = left >= wa.Left ? left : Math.Min(wa.Right - PW, right);
        }

        void F7Overlay(Graphics g)
        {
            if (!cfg.F7 || !cfg.Interact || interactActive || hoverStart < 0) return;
            float p = Math.Min(1f, (Environment.TickCount - hoverStart) / 800f);
            if (p < 0.08f) return;
            float cx = 65, cy = 78, r = 46;
            using (var pen = new Pen(Color.FromArgb(90, 255, 255, 255), 4f)) g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
            using (var pen = new Pen(Color.FromArgb(230, 40, 200, 220), 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawArc(pen, cx - r, cy - r, r * 2, r * 2, -90, 360 * p);
        }
    }
}
