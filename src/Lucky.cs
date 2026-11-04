// Lucky - a tiny Husky desktop pet that lives on top of your Windows taskbar.
// Pure WinForms + GDI+, no dependencies. Build with build.ps1 (uses the C# compiler bundled with Windows).
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LuckyPet
{
    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main()
        {
            bool created;
            using (var m = new System.Threading.Mutex(true, "LuckyPet_SingleInstance", out created))
            {
                if (!created) return;
                try { SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.Run(new PetForm());
            }
        }
    }

    enum St { Walk, Sit, Sleep, Drag, Fall, Happy, Chase, Remind }

    class PetForm : Form
    {
        // ---------- Win32 ----------
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; public POINT(int a, int b) { x = a; y = b; } }
        [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; public SIZE(int a, int b) { cx = a; cy = b; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
        [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
        [DllImport("user32.dll", CharSet = CharSet.Auto)] static extern int GetClassName(IntPtr h, System.Text.StringBuilder sb, int n);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        // ---------- state ----------
        const int CW = 130, CH = 120;          // logical canvas; ground line at y = 116
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const int RemindMinutes = 45;
        float S; int PW, PH;
        Bitmap canvas;
        Timer timer;
        Random rnd = new Random();
        St st = St.Sit;
        double x, y, vy;
        int dir = 1, stateTicks = 60, frame = 0, fsCheck = 0;
        bool dragging, moved, hiddenFs, remindOn = true;
        Point dragOff, downPt;
        NotifyIcon tray;
        DateTime nextRemind;

        // Husky palette (water-element friendly: grey-blue, white, blue eyes)
        static readonly Color Fur = Color.FromArgb(112, 128, 156), FurDark = Color.FromArgb(56, 66, 92),
            White = Color.FromArgb(242, 245, 251), EarIn = Color.FromArgb(226, 206, 222), Eye = Color.FromArgb(72, 156, 242),
            Nose = Color.FromArgb(34, 38, 50), Tongue = Color.FromArgb(242, 122, 146), Collar = Color.FromArgb(56, 196, 214),
            TagColor = Color.FromArgb(224, 232, 244);

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x80000 | 0x80 | 0x8; return cp; } // layered | toolwindow | topmost
        }
        protected override bool ShowWithoutActivation { get { return true; } }

        public PetForm()
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) S = g.DpiX / 96f;
            PW = (int)(CW * S); PH = (int)(CH * S);
            canvas = new Bitmap(PW, PH, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var wa = Screen.PrimaryScreen.WorkingArea;
            x = wa.Left + wa.Width * 0.72; y = Ground();
            Size = new Size(PW, PH); Location = new Point((int)x, (int)y);

            var menu = new ContextMenuStrip();
            menu.Items.Add("Xoa dau Lucky 0001F495", null, delegate { SetState(St.Happy, 70); });
            menu.Items.Add("Cho Lucky di ngu 0001F4A4", null, delegate { SetState(St.Sleep, 1200); });
            menu.Items.Add("Danh thuc Lucky", null, delegate { SetState(St.Walk, 150); });
            var rem = new ToolStripMenuItem("Nhac uong nuoc & vuon vai (moi 45 phut)") { Checked = true, CheckOnClick = true };
            rem.CheckedChanged += delegate { remindOn = rem.Checked; nextRemind = DateTime.Now.AddMinutes(RemindMinutes); };
            menu.Items.Add(rem);
            var boot = new ToolStripMenuItem("Khoi dong cung Windows") { Checked = StartupEnabled(), CheckOnClick = true };
            boot.CheckedChanged += delegate { SetStartup(boot.Checked); };
            menu.Items.Add(boot);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Tam biet Lucky (thoat)", null, delegate { tray.Visible = false; Application.Exit(); });
            Localize(menu);

            tray = new NotifyIcon { Icon = MakeIcon(), Text = "Lucky", Visible = true, ContextMenuStrip = menu };
            tray.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) SetState(St.Happy, 70); };
            ContextMenuStrip = menu;

            MouseDown += OnDown; MouseMove += OnMove; MouseUp += OnUp;
            nextRemind = DateTime.Now.AddMinutes(RemindMinutes);
            timer = new Timer { Interval = 33 };
            timer.Tick += Tick;
            timer.Start();
            SetState(St.Happy, 70); // say hi
        }

        // Vietnamese menu labels (unicode escapes keep the source encoding-safe)
        static void Localize(ContextMenuStrip m)
        {
            m.Items[0].Text = "Xoà đầu Lucky \U0001F495";
            m.Items[1].Text = "Cho Lucky đi ngủ 0001F4A4";
            m.Items[2].Text = "Đánh thức Lucky";
            m.Items[3].Text = "Nhắc uống nước & vươn vai (mỗi 45 phút)";
            m.Items[4].Text = "Khởi động cùng Windows";
            m.Items[6].Text = "Tạm biệt Lucky (thoát)";
        }

        double Ground() { return Screen.PrimaryScreen.WorkingArea.Bottom - PH + 4 * S; }

        void SetState(St s, int ticks)
        {
            st = s; stateTicks = ticks;
            if (s == St.Happy && y >= Ground() - 1) vy = -7 * S;
            if (s == St.Fall) vy = 0;
            timer.Interval = (s == St.Sleep || s == St.Sit) ? 50 : 33;
        }

        void NextRandomState()
        {
            if (st == St.Sleep) { SetState(St.Sit, 60); return; }
            int r = rnd.Next(100);
            if (r < 55) { dir = rnd.Next(2) == 0 ? -1 : 1; SetState(St.Walk, rnd.Next(90, 300)); }
            else if (r < 87) SetState(St.Sit, rnd.Next(60, 160));
            else SetState(St.Sleep, rnd.Next(300, 900));
        }

        // ---------- input ----------
        void OnDown(object s, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            dragging = true; moved = false; downPt = Cursor.Position;
            dragOff = new Point(Cursor.Position.X - (int)x, Cursor.Position.Y - (int)y);
        }
        void OnMove(object s, MouseEventArgs e)
        {
            if (!dragging) return;
            var c = Cursor.Position;
            if (!moved && (Math.Abs(c.X - downPt.X) > 4 || Math.Abs(c.Y - downPt.Y) > 4)) { moved = true; SetState(St.Drag, 1); }
            if (moved) { x = c.X - dragOff.X; y = c.Y - dragOff.Y; }
        }
        void OnUp(object s, MouseEventArgs e)
        {
            if (!dragging) return;
            dragging = false;
            if (moved) SetState(St.Fall, 1); else SetState(St.Happy, 70);
        }

        // ---------- main loop ----------
        void Tick(object sender, EventArgs e)
        {
            frame++;
            if (++fsCheck >= 30)
            {
                fsCheck = 0;
                bool fs = IsFullscreenApp();
                if (fs && !hiddenFs) { hiddenFs = true; Hide(); }
                else if (!fs && hiddenFs) { hiddenFs = false; Show(); }
            }
            if (hiddenFs) return;

            if (remindOn && DateTime.Now >= nextRemind && st != St.Drag)
            {
                nextRemind = DateTime.Now.AddMinutes(RemindMinutes);
                SetState(St.Remind, 260);
                tray.ShowBalloonTip(7000, "Lucky nhắc nè 0001F4A7",
                    "Uống một ngụm nước và đứng dậy vươn vai 1 phút nhé!", ToolTipIcon.None);
            }

            var wa = Screen.PrimaryScreen.WorkingArea;
            double g = Ground();
            var cur = Cursor.Position;
            switch (st)
            {
                case St.Walk:
                    x += dir * 1.7 * S;
                    if (Math.Abs(cur.X - (x + PW / 2.0)) < 230 * S && cur.Y > g - 120 * S && rnd.Next(120) == 0) SetState(St.Chase, 220);
                    break;
                case St.Chase:
                    {
                        double d = (cur.X - PW / 2.0) - x;
                        dir = d > 0 ? 1 : -1;
                        if (Math.Abs(d) > 14 * S) x += dir * 3.6 * S; else SetState(St.Happy, 60);
                        break;
                    }
                case St.Fall:
                    vy += 0.9 * S; y += vy;
                    if (y >= g)
                    {
                        y = g;
                        if (vy > 9 * S) vy = -vy * 0.35;
                        else { vy = 0; SetState(St.Sit, 80); }
                    }
                    break;
                case St.Happy:
                    if (y < g || vy < 0) { vy += 0.9 * S; y += vy; if (y >= g) { y = g; vy = 0; } }
                    break;
            }
            if (st != St.Drag && st != St.Fall && st != St.Happy) y = g;
            if (x < wa.Left) { x = wa.Left; dir = 1; }
            if (x > wa.Right - PW) { x = wa.Right - PW; dir = -1; }
            if (st != St.Drag && --stateTicks <= 0) NextRandomState();
            Render();
        }

        bool IsFullscreenApp()
        {
            IntPtr h = GetForegroundWindow();
            if (h == IntPtr.Zero || h == Handle || h == GetShellWindow()) return false;
            var sb = new System.Text.StringBuilder(256); GetClassName(h, sb, 256);
            string c = sb.ToString();
            if (c == "WorkerW" || c == "Progman" || c == "Shell_TrayWnd") return false;
            RECT r; GetWindowRect(h, out r);
            var b = Screen.FromHandle(h).Bounds;
            return r.L <= b.Left && r.T <= b.Top && r.R >= b.Right && r.B >= b.Bottom;
        }

        // ---------- rendering ----------
        void Render()
        {
            using (var g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.ScaleTransform(S, S);
                var state = g.Save();
                if (dir < 0) { g.TranslateTransform(CW, 0); g.ScaleTransform(-1, 1); }
                if (st == St.Sleep) DrawSleep(g);
                else if (st == St.Sit || st == St.Remind) DrawSit(g);
                else DrawStand(g);
                g.Restore(state);
                DrawOverlay(g);
            }
            Push();
        }

        void Push()
        {
            IntPtr screenDc = GetDC(IntPtr.Zero), memDc = CreateCompatibleDC(screenDc);
            IntPtr hb = canvas.GetHbitmap(Color.FromArgb(0)), old = SelectObject(memDc, hb);
            var size = new SIZE(PW, PH); var src = new POINT(0, 0); var dst = new POINT((int)x, (int)y);
            var bf = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref bf, 2);
            SelectObject(memDc, old); DeleteObject(hb); DeleteDC(memDc); ReleaseDC(IntPtr.Zero, screenDc);
        }

        static void Oval(Graphics g, Color fill, float x, float y, float w, float h, bool outline = true)
        {
            using (var b = new SolidBrush(fill)) g.FillEllipse(b, x, y, w, h);
            if (outline) using (var p = new Pen(FurDark, 2f)) g.DrawEllipse(p, x, y, w, h);
        }
        static void Poly(Graphics g, Color fill, bool outline, params PointF[] pts)
        {
            using (var br = new SolidBrush(fill)) g.FillPolygon(br, pts);
            if (outline) using (var p = new Pen(FurDark, 2f) { LineJoin = LineJoin.Round }) g.DrawPolygon(p, pts);
        }
        static PointF P(float x, float y) { return new PointF(x, y); }

        // Fluffy curled husky tail with a white tip
        static void Tail(Graphics g, PointF a, PointF b, PointF c, PointF d, float w)
        {
            using (var p = new Pen(FurDark, w + 3) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawBezier(p, a, b, c, d);
            using (var p = new Pen(Fur, w) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawBezier(p, a, b, c, d);
            using (var b2 = new SolidBrush(White)) g.FillEllipse(b2, d.X - w * 0.55f, d.Y - w * 0.55f, w * 1.1f, w * 1.1f);
        }

        static void Leg(Graphics g, float x, float top, float h)
        {
            using (var path = new GraphicsPath())
            {
                float w = 10, r = 4.5f;
                path.AddArc(x, top, w, r * 2, 180, 180);
                path.AddLine(x + w, top + r, x + w, top + h - r);
                path.AddArc(x, top + h - r * 2, w, r * 2, 0, 180);
                path.CloseFigure();
                using (var b = new SolidBrush(White)) g.FillPath(b, path);
                using (var p = new Pen(FurDark, 2f)) g.DrawPath(p, path);
            }
            using (var b = new SolidBrush(Fur)) g.FillRectangle(b, x + 1.2f, top + 2, 7.6f, h * 0.35f);
        }

        // Husky head, facing right; snout points to +x
        void Head(Graphics g, float cx, float cy, float r, bool closed)
        {
            bool happy = st == St.Happy || st == St.Remind;
            Poly(g, Fur, true, P(cx - r * 0.85f, cy - r * 0.4f), P(cx - r * 0.6f, cy - r * 1.5f), P(cx - r * 0.05f, cy - r * 0.8f));
            Poly(g, Fur, true, P(cx + r * 0.05f, cy - r * 0.85f), P(cx + r * 0.5f, cy - r * 1.55f), P(cx + r * 0.8f, cy - r * 0.5f));
            Poly(g, EarIn, false, P(cx - r * 0.66f, cy - r * 0.6f), P(cx - r * 0.56f, cy - r * 1.2f), P(cx - r * 0.25f, cy - r * 0.8f));
            Poly(g, EarIn, false, P(cx + r * 0.22f, cy - r * 0.85f), P(cx + r * 0.48f, cy - r * 1.25f), P(cx + r * 0.62f, cy - r * 0.6f));
            Oval(g, Fur, cx - r, cy - r * 0.95f, r * 2, r * 1.9f);
            // white husky mask + eyebrow dots
            using (var b = new SolidBrush(White))
            {
                g.FillEllipse(b, cx - r * 0.8f, cy - r * 0.05f, r * 1.6f, r * 0.95f);
                g.FillEllipse(b, cx - r * 0.12f, cy - r * 0.75f, r * 0.24f, r * 0.55f);   // forehead stripe
                g.FillEllipse(b, cx - r * 0.5f, cy - r * 0.42f, 5, 3.5f);
                g.FillEllipse(b, cx + r * 0.2f, cy - r * 0.42f, 5, 3.5f);
            }
            // snout
            Oval(g, White, cx + r * 0.3f, cy + r * 0.02f, r * 1.0f, r * 0.68f);
            Oval(g, Nose, cx + r * 1.08f, cy + r * 0.04f, 7.5f, 5.5f, false);
            using (var b = new SolidBrush(Color.FromArgb(160, 255, 255, 255))) g.FillEllipse(b, cx + r * 1.1f + 1.5f, cy + r * 0.06f, 2.5f, 1.6f);
            using (var mp = new Pen(Color.FromArgb(60, 64, 84), 1.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                if (happy || st == St.Chase)
                {
                    Oval(g, Tongue, cx + r * 0.62f, cy + r * 0.48f, 8, 11, false);
                    g.DrawLine(mp, cx + r * 0.62f + 4, cy + r * 0.52f, cx + r * 0.62f + 4, cy + r * 0.52f + 6);
                    g.DrawArc(mp, cx + r * 0.42f, cy + r * 0.25f, r * 0.8f, r * 0.35f, 10, 160);
                }
                else g.DrawArc(mp, cx + r * 0.5f, cy + r * 0.3f, r * 0.6f, r * 0.2f, 20, 140);
            }
            // eyes
            float ey = cy - r * 0.18f;
            bool blink = (frame % 150) < 4;
            using (var p = new Pen(Color.FromArgb(38, 42, 58), 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                foreach (float px in new[] { cx - r * 0.35f, cx + r * 0.3f })
                {
                    if (closed) g.DrawArc(p, px - 4, ey - 4, 8, 7, 10, 160);
                    else if (happy) g.DrawArc(p, px - 4, ey - 1, 8, 8, 200, 140);
                    else if (blink) g.DrawLine(p, px - 4, ey + 1, px + 4, ey + 1);
                    else
                    {
                        Oval(g, Eye, px - 4, ey - 4, 8, 8, false);
                        using (var pen = new Pen(Color.FromArgb(38, 42, 58), 1.4f)) g.DrawEllipse(pen, px - 4, ey - 4, 8, 8);
                        float pw = st == St.Chase ? 5 : 3.6f;
                        using (var b = new SolidBrush(Color.FromArgb(22, 26, 38))) g.FillEllipse(b, px - pw / 2 + 0.6f, ey - pw / 2, pw, pw);
                        using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, px - 2.4f, ey - 3f, 2.6f, 2.6f);
                    }
                }
            }
            using (var b = new SolidBrush(Color.FromArgb(80, 255, 160, 185)))
                g.FillEllipse(b, cx - r * 0.75f, cy + r * 0.12f, 8, 4.5f);
        }

        void CollarTag(Graphics g, float cx, float cy, float w)
        {
            using (var p = new Pen(Collar, 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(p, cx - w / 2, cy - 7, w, 12, 25, 130);
            Oval(g, TagColor, cx - 3.5f, cy + 3.5f, 7, 7, false);
            using (var p = new Pen(Color.FromArgb(110, 125, 150), 1.2f)) g.DrawEllipse(p, cx - 3.5f, cy + 3.5f, 7, 7);
        }

        void DrawStand(Graphics g)
        {
            bool walking = st == St.Walk || st == St.Chase;
            float speed = st == St.Chase ? 0.6f : 0.35f;
            float bob = walking ? (float)Math.Abs(Math.Sin(frame * speed)) * 2 : 0;
            float wag = st == St.Happy || st == St.Chase ? (float)Math.Sin(frame * 0.8) * 9 : (float)Math.Sin(frame * 0.25) * 4;
            float ph = walking ? (float)Math.Sin(frame * speed) * 4 : 0;
            Tail(g, P(34, 86 - bob), P(16, 80), P(12 + wag * 0.4f, 52 + wag), P(30 + wag * 0.3f, 52 + wag * 0.5f), 10);
            Leg(g, 36 - ph, 92 - bob, 23 + bob); Leg(g, 49 + ph, 92 - bob, 23 + bob);
            Oval(g, Fur, 28, 74 - bob, 66, 33);
            using (var b = new SolidBrush(White)) g.FillEllipse(b, 42, 93 - bob, 40, 13);
            Leg(g, 70 + ph, 92 - bob, 23 + bob); Leg(g, 82 - ph, 92 - bob, 23 + bob);
            CollarTag(g, 86, 80 - bob, 24);
            Head(g, 90, 60 - bob, 21, false);
        }

        void DrawSit(Graphics g)
        {
            float wag = st == St.Remind ? (float)Math.Sin(frame * 0.6) * 5 : (float)Math.Sin(frame * 0.12) * 3;
            Tail(g, P(46, 108), P(26, 114), P(16 + wag, 100), P(26 + wag, 88), 10);
            Oval(g, Fur, 38, 95, 28, 20);
            Oval(g, Fur, 42, 70, 46, 46);
            using (var b = new SolidBrush(White)) g.FillEllipse(b, 55, 80, 24, 32);
            Leg(g, 56, 95, 20); Leg(g, 69, 95, 20);
            CollarTag(g, 66, 70, 28);
            Head(g, 66, 50, 22, false);
        }

        void DrawSleep(Graphics g)
        {
            float br = (float)Math.Sin(frame * 0.08) * 1.3f;
            Oval(g, Fur, 28, 89 - br, 68, 27 + br);
            using (var b = new SolidBrush(White)) g.FillEllipse(b, 42, 104 - br, 38, 10);
            Tail(g, P(32, 104), P(14, 118), P(56, 122), P(86, 114), 10);
            CollarTag(g, 80, 86, 22);
            Head(g, 80, 92, 18, true);
        }

        float HeadX(float hx) { return dir > 0 ? hx : CW - hx; }

        void DrawOverlay(Graphics g)
        {
            if (st == St.Sleep)
            {
                float hx = HeadX(84);
                using (var f = new Font("Segoe UI", 9f, FontStyle.Bold))
                    for (int k = 0; k < 3; k++)
                    {
                        double ph = (frame * 0.012 + k / 3.0) % 1.0;
                        int a = (int)(255 * (1 - ph));
                        using (var b = new SolidBrush(Color.FromArgb(a, 205, 225, 255)))
                        {
                            var s0 = g.Save();
                            g.TranslateTransform(hx + (float)(ph * 18 * (dir > 0 ? 1 : -1)), 64 - (float)(ph * 46));
                            g.ScaleTransform(0.7f + (float)ph * 0.6f, 0.7f + (float)ph * 0.6f);
                            g.DrawString("z", f, b, 0, 0);
                            g.Restore(s0);
                        }
                    }
            }
            if (st == St.Happy)
            {
                float hx = HeadX(90);
                for (int k = 0; k < 3; k++)
                {
                    double ph = (frame * 0.03 + k / 3.0) % 1.0;
                    float hxk = hx - 16 + k * 16 + (float)Math.Sin(frame * 0.2 + k) * 2;
                    Heart(g, hxk, 30 - (float)ph * 26, 4.5f, (int)(255 * (1 - ph)));
                }
            }
            if (st == St.Remind)
            {
                float hx = HeadX(66);
                float bx = Math.Max(2, Math.Min(CW - 42, hx - 6)), by = 2 + (float)Math.Sin(frame * 0.15) * 1.5f;
                using (var path = Rounded(bx, by, 38, 26, 9))
                {
                    using (var b = new SolidBrush(Color.FromArgb(235, 244, 249, 255))) g.FillPath(b, path);
                    using (var p = new Pen(Color.FromArgb(120, 160, 210), 1.5f)) g.DrawPath(p, path);
                }
                Poly(g, Color.FromArgb(235, 244, 249, 255), false, P(hx - 2, by + 25), P(hx + 6, by + 25), P(hx, by + 32));
                float dx = bx + 19, dy = by + 4;
                using (var b = new SolidBrush(Color.FromArgb(70, 160, 240)))
                {
                    g.FillEllipse(b, dx - 7, dy + 6, 14, 14);
                    g.FillPolygon(b, new[] { P(dx, dy - 1), P(dx - 6.3f, dy + 10), P(dx + 6.3f, dy + 10) });
                }
                using (var b = new SolidBrush(Color.FromArgb(220, 255, 255, 255))) g.FillEllipse(b, dx - 4, dy + 9, 3.5f, 5);
            }
        }

        static GraphicsPath Rounded(float x, float y, float w, float h, float r)
        {
            var p = new GraphicsPath();
            p.AddArc(x, y, r * 2, r * 2, 180, 90); p.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
            p.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90); p.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
            p.CloseFigure(); return p;
        }

        static void Heart(Graphics g, float x, float y, float s, int a)
        {
            using (var b = new SolidBrush(Color.FromArgb(Math.Max(0, Math.Min(255, a)), 255, 120, 165)))
            {
                g.FillEllipse(b, x - s, y - s / 2, s * 1.1f, s * 1.1f);
                g.FillEllipse(b, x - s * 0.1f, y - s / 2, s * 1.1f, s * 1.1f);
                g.FillPolygon(b, new[] { P(x - s * 0.95f, y + s * 0.2f), P(x + s * 0.95f, y + s * 0.2f), P(x, y + s * 1.4f) });
            }
        }

        Icon MakeIcon()
        {
            using (var bmp = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var b = new SolidBrush(Fur))
                    {
                        g.FillPolygon(b, new[] { P(4, 14), P(7, 0), P(14, 8) });
                        g.FillPolygon(b, new[] { P(18, 8), P(25, 0), P(28, 14) });
                        g.FillEllipse(b, 3, 6, 26, 24);
                    }
                    using (var b = new SolidBrush(White)) { g.FillEllipse(b, 6, 17, 20, 13); g.FillEllipse(b, 14, 7, 4, 9); }
                    using (var b = new SolidBrush(Eye)) { g.FillEllipse(b, 9, 13, 5, 5); g.FillEllipse(b, 18, 13, 5, 5); }
                    using (var b = new SolidBrush(Nose)) g.FillEllipse(b, 13, 21, 6, 4);
                }
                IntPtr h = bmp.GetHicon();
                var ic = (Icon)Icon.FromHandle(h).Clone();
                DestroyIcon(h);
                return ic;
            }
        }

        static bool StartupEnabled()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue("LuckyPet") != null;
        }
        static void SetStartup(bool on)
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (k == null) return;
                if (on) k.SetValue("LuckyPet", "\"" + Application.ExecutablePath + "\"");
                else k.DeleteValue("LuckyPet", false);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e) { tray.Visible = false; base.OnFormClosed(e); }
    }
}
