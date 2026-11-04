// Lucky - a tiny Husky desktop pet that lives on top of your Windows taskbar.
// Pure WinForms + GDI+, no dependencies. Build with build.ps1 (uses the C# compiler bundled with Windows).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LuckyPet
{
    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--snapshot")
            {
                try { SetProcessDPIAware(); } catch { }
                var f = new PetForm(true);
                f.Snapshot(args[1]);
                return;
            }
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

    // ---------- persistent memory (%APPDATA%\LuckyPet\memory.txt, key=value) ----------
    class Memory
    {
        public DateTime FirstMet = DateTime.Now, LastSeen = DateTime.Now;
        public int Pets, Chases, Reminders, Naps, Drops, DayMilestone, PetMilestone;
        public double Affection = 60, PosX = -1;
        public float Size = 0.85f;
        public int Facing = 1;
        public bool RemindOn = true;

        public static string Folder { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LuckyPet"); } }
        public static string FilePath { get { return Path.Combine(Folder, "memory.txt"); } }
        static readonly CultureInfo CI = CultureInfo.InvariantCulture;

        public int DaysTogether { get { return (DateTime.Now.Date - FirstMet.Date).Days + 1; } }

        public static Memory Load(out bool isNew)
        {
            var m = new Memory();
            isNew = !File.Exists(FilePath);
            if (isNew) return m;
            string[] lines;
            try { lines = File.ReadAllLines(FilePath, Encoding.UTF8); } catch { return m; }
            foreach (var line in lines)
            {
                int i = line.IndexOf('=');
                if (i <= 0 || line.StartsWith("#")) continue;
                string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                try
                {
                    switch (k)
                    {
                        case "first_met": m.FirstMet = DateTime.Parse(v, CI, DateTimeStyles.RoundtripKind); break;
                        case "last_seen": m.LastSeen = DateTime.Parse(v, CI, DateTimeStyles.RoundtripKind); break;
                        case "pets": m.Pets = int.Parse(v, CI); break;
                        case "chases": m.Chases = int.Parse(v, CI); break;
                        case "reminders": m.Reminders = int.Parse(v, CI); break;
                        case "naps": m.Naps = int.Parse(v, CI); break;
                        case "drops": m.Drops = int.Parse(v, CI); break;
                        case "day_milestone": m.DayMilestone = int.Parse(v, CI); break;
                        case "pet_milestone": m.PetMilestone = int.Parse(v, CI); break;
                        case "affection": m.Affection = Math.Max(0, Math.Min(100, double.Parse(v, CI))); break;
                        case "pos_x": m.PosX = double.Parse(v, CI); break;
                        case "facing": m.Facing = int.Parse(v, CI) < 0 ? -1 : 1; break;
                        case "remind_on": m.RemindOn = v == "true"; break;
                        case "size": m.Size = Math.Max(0.6f, Math.Min(2.5f, float.Parse(v, CI))); break;
                    }
                }
                catch { /* ignore a bad line, keep the rest */ }
            }
            return m;
        }

        public void Save()
        {
            Directory.CreateDirectory(Folder);
            var sb = new StringBuilder();
            sb.AppendLine("# Lucky's memory 🐾  (plain text, safe to read; edit while Lucky is closed)");
            sb.AppendLine("first_met=" + FirstMet.ToString("o", CI));
            sb.AppendLine("last_seen=" + LastSeen.ToString("o", CI));
            sb.AppendLine("affection=" + Affection.ToString("0.00", CI));
            sb.AppendLine("pets=" + Pets.ToString(CI));
            sb.AppendLine("chases=" + Chases.ToString(CI));
            sb.AppendLine("naps=" + Naps.ToString(CI));
            sb.AppendLine("drops=" + Drops.ToString(CI));
            sb.AppendLine("reminders=" + Reminders.ToString(CI));
            sb.AppendLine("day_milestone=" + DayMilestone.ToString(CI));
            sb.AppendLine("pet_milestone=" + PetMilestone.ToString(CI));
            sb.AppendLine("pos_x=" + PosX.ToString("0", CI));
            sb.AppendLine("facing=" + Facing.ToString(CI));
            sb.AppendLine("remind_on=" + (RemindOn ? "true" : "false"));
            sb.AppendLine("size=" + Size.ToString("0.00", CI));
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null); else File.Move(tmp, FilePath);
        }
    }

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
        static readonly int[] DayMilestones = { 3, 7, 30, 100, 365 };
        static readonly int[] PetMilestones = { 10, 50, 100, 500, 1000 };
        float S, dpiScale = 1; int PW, PH;
        Bitmap canvas, petLayer, petSmall;
        const int SS = 2; // supersampling factor
        System.Drawing.Imaging.ImageAttributes whiteAttr;
        Timer timer;
        Random rnd = new Random();
        St st = St.Sit;
        double x, y, vy;
        int dir = 1, stateTicks = 60, frame = 0, fsCheck = 0;
        bool dragging, moved, hiddenFs, remindOn = true, dirty;
        Point dragOff, downPt;
        NotifyIcon tray;
        DateTime nextRemind, lastSave = DateTime.Now, lastDecay = DateTime.Now;
        Memory mem;
        string pendingTitle, pendingText;

        // Husky palette (water-element friendly: grey-blue, white, blue eyes)
        static readonly Color FurDark = Color.FromArgb(62, 74, 106), FurMid = Color.FromArgb(116, 132, 166), FurLight = Color.FromArgb(182, 196, 222),
            White = Color.FromArgb(253, 254, 255), WhiteShade = Color.FromArgb(204, 214, 234), Line = Color.FromArgb(175, 42, 50, 80),
            Rim = Color.FromArgb(150, 205, 232, 255),
            EarIn1 = Color.FromArgb(255, 212, 226), EarIn2 = Color.FromArgb(224, 156, 188),
            IrisTop = Color.FromArgb(22, 58, 138), Iris2 = Color.FromArgb(52, 136, 232), Iris1 = Color.FromArgb(156, 222, 255),
            NoseC = Color.FromArgb(32, 36, 50), Tongue1 = Color.FromArgb(255, 164, 184), Tongue2 = Color.FromArgb(226, 88, 122),
            Collar1 = Color.FromArgb(150, 244, 240), Collar2 = Color.FromArgb(28, 158, 196), TagColor = Color.FromArgb(170, 184, 210);

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x80000 | 0x80 | 0x8; return cp; } // layered | toolwindow | topmost
        }
        protected override bool ShowWithoutActivation { get { return true; } }

        // Offline renderer for README screenshots / visual checks (no tray, no memory writes).
        public PetForm(bool snapshotOnly)
        {
            mem = new Memory(); mem.Affection = 90;
            timer = new Timer();
            S = 2; Alloc();
        }

        public void Snapshot(string path)
        {
            var poses = new[] { St.Walk, St.Sit, St.Happy, St.Chase, St.Sleep, St.Remind };
            using (var sheet = new Bitmap(PW * poses.Length, PH))
            using (var g = Graphics.FromImage(sheet))
            {
                g.Clear(Color.FromArgb(16, 30, 66));
                y = Ground();
                for (int i = 0; i < poses.Length; i++)
                {
                    st = poses[i]; frame = 20; dir = 1;
                    RenderFrame();
                    g.DrawImageUnscaled(canvas, i * PW, 0);
                }
                sheet.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        void Alloc()
        {
            PW = (int)(CW * S); PH = (int)(CH * S);
            if (canvas != null) canvas.Dispose();
            if (petLayer != null) petLayer.Dispose();
            if (petSmall != null) petSmall.Dispose();
            canvas = new Bitmap(PW, PH, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            petLayer = new Bitmap(PW * SS, PH * SS, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            petSmall = new Bitmap(PW, PH, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            if (whiteAttr == null)
            {
                // Paint every pixel near-white while keeping its alpha -> used for the sticker outline
                var cm = new System.Drawing.Imaging.ColorMatrix(new float[][] {
                    new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 0, 0 },
                    new float[] { 0, 0, 0, 1, 0 }, new float[] { 0.95f, 0.97f, 1, 0, 1 } });
                whiteAttr = new System.Drawing.Imaging.ImageAttributes();
                whiteAttr.SetColorMatrix(cm);
            }
        }

        void SetSize(float z)
        {
            mem.Size = z; dirty = true;
            S = dpiScale * z; Alloc();
            var wa = Screen.PrimaryScreen.WorkingArea;
            x = Math.Max(wa.Left, Math.Min(wa.Right - PW, x));
            y = Ground();
            Render();
        }

        public PetForm()
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) dpiScale = g.DpiX / 96f;
            bool isNew;
            mem = Memory.Load(out isNew);
            S = dpiScale * mem.Size;
            Alloc();
            remindOn = mem.RemindOn;
            dir = mem.Facing;
            var wa = Screen.PrimaryScreen.WorkingArea;
            x = mem.PosX >= 0 ? Math.Max(wa.Left, Math.Min(wa.Right - PW, mem.PosX)) : wa.Left + wa.Width * 0.72;
            y = Ground();
            Size = new Size(PW, PH); Location = new Point((int)x, (int)y);
            Greet(isNew);

            var menu = new ContextMenuStrip();
            menu.Items.Add("Xoa đầu Lucky 💕", null, delegate { Pet(); });
            menu.Items.Add("Kỷ niệm với Lucky 📖", null, delegate { ShowMemories(); });
            menu.Items.Add("Cho Lucky đi ngủ 💤", null, delegate { SetState(St.Sleep, 1200); });
            menu.Items.Add("Đánh thức Lucky", null, delegate { SetState(St.Walk, 150); });
            var sizeMenu = new ToolStripMenuItem("Kích thước");
            foreach (var opt in new[] { new KeyValuePair<string, float>("Nhỏ", 0.85f), new KeyValuePair<string, float>("Vừa", 1.1f), new KeyValuePair<string, float>("Lớn", 1.4f) })
            {
                var it = new ToolStripMenuItem(opt.Key) { Checked = Math.Abs(mem.Size - opt.Value) < 0.05f, Tag = opt.Value };
                it.Click += delegate(object s, EventArgs e)
                {
                    foreach (ToolStripMenuItem o in sizeMenu.DropDownItems) o.Checked = o == s;
                    SetSize((float)((ToolStripMenuItem)s).Tag);
                };
                sizeMenu.DropDownItems.Add(it);
            }
            menu.Items.Add(sizeMenu);
            var rem = new ToolStripMenuItem("Nhắc uống nước & vươn vai (mỗi 45 phút)") { Checked = remindOn, CheckOnClick = true };
            rem.CheckedChanged += delegate { remindOn = rem.Checked; nextRemind = DateTime.Now.AddMinutes(RemindMinutes); dirty = true; };
            menu.Items.Add(rem);
            var boot = new ToolStripMenuItem("Khởi động cùng Windows") { Checked = StartupEnabled(), CheckOnClick = true };
            boot.CheckedChanged += delegate { SetStartup(boot.Checked); };
            menu.Items.Add(boot);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Tạm biệt Lucky (thoát)", null, delegate { SaveMem(); tray.Visible = false; Application.Exit(); });

            tray = new NotifyIcon { Icon = MakeIcon(), Text = "Lucky", Visible = true, ContextMenuStrip = menu };
            tray.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) Pet(); };
            ContextMenuStrip = menu;
            UpdateTooltip();

            MouseDown += OnDown; MouseMove += OnMove; MouseUp += OnUp;
            SystemEvents.SessionEnding += delegate { SaveMem(); };
            nextRemind = DateTime.Now.AddMinutes(RemindMinutes);
            timer = new Timer { Interval = 33 };
            timer.Tick += Tick;
            timer.Start();
            SetState(St.Happy, 70); // say hi
        }

        // ---------- memory-driven behaviour ----------
        void Greet(bool isNew)
        {
            if (isNew)
            {
                pendingTitle = "Gâu! Mình là Lucky 🐶";
                pendingText = "Từ hôm nay mình sẽ ở trên taskbar với bạn. Click để xoa đầu mình nhé!";
                dirty = true;
                return;
            }
            TimeSpan away = DateTime.Now - mem.LastSeen;
            if (away.TotalHours >= 12)
            {
                mem.Affection = Math.Max(10, mem.Affection - Math.Min(30, away.TotalDays * 5));
                string t = away.TotalDays >= 1 ? ((int)away.TotalDays) + " ngày" : ((int)away.TotalHours) + " tiếng";
                pendingTitle = "Lucky nhớ bạn quá! 🥺";
                pendingText = "Đã " + t + " rồi mới gặp lại. Xoa đầu Lucky một cái nha!";
            }
            int days = mem.DaysTogether;
            foreach (int m in DayMilestones)
                if (days >= m && mem.DayMilestone < m)
                {
                    mem.DayMilestone = m;
                    pendingTitle = "Kỷ niệm " + m + " ngày bên nhau 🎉";
                    pendingText = "Hôm nay là ngày thứ " + days + " Lucky ở bên bạn. Cảm ơn bạn đã chăm Lucky!";
                }
            dirty = true;
        }

        void Pet()
        {
            mem.Pets++;
            mem.Affection = Math.Min(100, mem.Affection + 3);
            foreach (int m in PetMilestones)
                if (mem.Pets >= m && mem.PetMilestone < m)
                {
                    mem.PetMilestone = m;
                    tray.ShowBalloonTip(5000, "Lucky vui quá 🥰", "Bạn đã xoa đầu Lucky " + m + " lần rồi đó!", ToolTipIcon.None);
                }
            dirty = true;
            UpdateTooltip();
            SetState(St.Happy, 70);
        }

        void ShowMemories()
        {
            int aff = (int)Math.Round(mem.Affection);
            string bar = new string('♥', aff / 20) + new string('♡', 5 - aff / 20);
            string text =
                "🐶  Lucky – chú Husky của bạn\n\n" +
                "📅  Gặp nhau từ: " + mem.FirstMet.ToString("dd/MM/yyyy") + "  (" + mem.DaysTogether + " ngày)\n" +
                "💞  Độ thân thiết: " + bar + "  " + aff + "%\n\n" +
                "🤚  Được xoa đầu: " + mem.Pets + " lần\n" +
                "🏃  Đuổi theo chuột: " + mem.Chases + " lần\n" +
                "💤  Giấc ngủ: " + mem.Naps + " lần\n" +
                "🪂  Bị thả rơi: " + mem.Drops + " lần\n" +
                "💧  Nhắc uống nước: " + mem.Reminders + " lần\n\n" +
                "Trí nhớ được lưu ở:\n" + Memory.FilePath;
            MessageBox.Show(text, "Kỷ niệm với Lucky", MessageBoxButtons.OK, MessageBoxIcon.None);
        }

        void UpdateTooltip()
        {
            if (tray == null) return;
            string t = "Lucky 🐾 ♥ " + (int)Math.Round(mem.Affection) + "% · bên nhau " + mem.DaysTogether + " ngày";
            tray.Text = t.Length > 63 ? t.Substring(0, 63) : t;
        }

        void SaveMem()
        {
            mem.PosX = x; mem.Facing = dir; mem.RemindOn = remindOn; mem.LastSeen = DateTime.Now;
            try { mem.Save(); } catch { }
            dirty = false; lastSave = DateTime.Now;
        }

        double Ground() { return Screen.PrimaryScreen.WorkingArea.Bottom - PH + 4 * S; }

        void SetState(St s, int ticks)
        {
            if (s == St.Sleep && st != St.Sleep) { mem.Naps++; dirty = true; }
            if (s == St.Chase) { mem.Chases++; mem.Affection = Math.Min(100, mem.Affection + 0.5); dirty = true; }
            st = s; stateTicks = ticks;
            if (s == St.Happy && y >= Ground() - 1) vy = -7 * S;
            if (s == St.Fall) vy = 0;
            timer.Interval = (s == St.Sleep || s == St.Sit) ? 70 : 40;
        }

        void NextRandomState()
        {
            if (st == St.Sleep) { SetState(St.Sit, 60); return; }
            int r = rnd.Next(100);
            bool lonely = mem.Affection < 30;
            if (r < (lonely ? 40 : 55)) { dir = rnd.Next(2) == 0 ? -1 : 1; SetState(St.Walk, rnd.Next(90, 300)); }
            else if (r < (lonely ? 70 : 87)) SetState(St.Sit, rnd.Next(60, 160));
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
            if (moved) { mem.Drops++; dirty = true; SetState(St.Fall, 1); } else Pet();
        }

        // ---------- main loop ----------
        void Tick(object sender, EventArgs e)
        {
            frame++;
            if (frame == 25 && pendingTitle != null) { tray.ShowBalloonTip(7000, pendingTitle, pendingText, ToolTipIcon.None); pendingTitle = null; }
            if ((DateTime.Now - lastDecay).TotalMinutes >= 1)
            {
                lastDecay = DateTime.Now;
                mem.Affection = Math.Max(0, mem.Affection - 0.02);
                UpdateTooltip();
            }
            if ((dirty && (DateTime.Now - lastSave).TotalSeconds > 30) || (DateTime.Now - lastSave).TotalMinutes > 5) SaveMem();

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
                mem.Reminders++; dirty = true;
                SetState(St.Remind, 260);
                tray.ShowBalloonTip(7000, "Lucky nhắc nè 💧", "Uống một ngụm nước và đứng dậy vươn vai 1 phút nhé!", ToolTipIcon.None);
            }

            var wa = Screen.PrimaryScreen.WorkingArea;
            double g = Ground();
            var cur = Cursor.Position;
            int chaseOdds = mem.Affection > 70 ? 70 : mem.Affection > 40 ? 120 : 260;
            switch (st)
            {
                case St.Walk:
                    x += dir * 1.7 * S;
                    if (Math.Abs(cur.X - (x + PW / 2.0)) < 230 * S && cur.Y > g - 120 * S && rnd.Next(chaseOdds) == 0) SetState(St.Chase, 220);
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
        void Render() { RenderFrame(); Push(); }

        void RenderFrame()
        {
            // 1) the pet itself on its own layer
            using (var g = Graphics.FromImage(petLayer))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.ScaleTransform(S * SS, S * SS);
                if (dir < 0) { g.TranslateTransform(CW, 0); g.ScaleTransform(-1, 1); }
                if (st == St.Sleep) DrawSleep(g);
                else if (st == St.Sit || st == St.Remind) DrawSit(g);
                else DrawStand(g);
            }
            using (var g = Graphics.FromImage(petSmall))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.DrawImage(petLayer, new Rectangle(0, 0, PW, PH));
            }
            // 2) ground shadow + white sticker outline + pet + overlays
            using (var g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                var s0 = g.Save();
                g.ScaleTransform(S, S);
                if (dir < 0) { g.TranslateTransform(CW, 0); g.ScaleTransform(-1, 1); }
                if (st == St.Sleep) GroundShadow(g, 62, 80);
                else if (st == St.Sit || st == St.Remind) GroundShadow(g, 64, 64);
                else GroundShadow(g, 62, 76);
                g.Restore(s0);
                float o = 1.25f * S;
                for (int k = 0; k < 12; k++)
                {
                    double a = k * Math.PI / 6;
                    int dx = (int)Math.Round(Math.Cos(a) * o), dy = (int)Math.Round(Math.Sin(a) * o);
                    g.DrawImage(petSmall, new Rectangle(dx, dy, PW, PH), 0, 0, PW, PH, GraphicsUnit.Pixel, whiteAttr);
                }
                g.DrawImageUnscaled(petSmall, 0, 0);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                g.ScaleTransform(S, S);
                DrawOverlay(g);
            }
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

        // --- drawing helpers ---
        static PointF P(float x, float y) { return new PointF(x, y); }

        static Brush Grad(RectangleF r, Color a, Color b)
        {
            return new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, Math.Max(r.Width, 1), r.Height + 2), a, b, LinearGradientMode.Vertical);
        }
        // Spherical shading: light from the upper-left
        static Brush Ball(GraphicsPath path, Color light, Color dark)
        {
            var bb = path.GetBounds();
            var b = new PathGradientBrush(path);
            b.CenterColor = light;
            b.SurroundColors = new[] { dark };
            b.CenterPoint = P(bb.X + bb.Width * 0.36f, bb.Y + bb.Height * 0.3f);
            b.FocusScales = P(0.38f, 0.38f);
            return b;
        }
        static GraphicsPath EllipsePath(RectangleF r) { var p = new GraphicsPath(); p.AddEllipse(r); return p; }
        // Ellipse with a soft fluffy (scalloped) edge
        static GraphicsPath Fluffy(RectangleF r, int n, float amp)
        {
            var pts = new PointF[n * 2];
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            for (int i = 0; i < n * 2; i++)
            {
                double a = i * Math.PI / n;
                float k = (i % 2 == 0) ? 1 + amp : 1 - amp * 0.35f;
                pts[i] = P(cx + (float)Math.Cos(a) * r.Width / 2 * k, cy + (float)Math.Sin(a) * r.Height / 2 * k);
            }
            var p = new GraphicsPath(); p.AddClosedCurve(pts, 0.55f); return p;
        }
        static void Fill(Graphics g, GraphicsPath path, Color light, Color dark, bool line)
        {
            using (var b = Ball(path, light, dark)) g.FillPath(b, path);
            if (line) using (var pen = new Pen(Line, 1.25f) { LineJoin = LineJoin.Round }) g.DrawPath(pen, path);
        }
        static void FillEl(Graphics g, RectangleF r, Color light, Color dark, bool line)
        {
            using (var p = EllipsePath(r)) Fill(g, p, light, dark, line);
        }
        static void Solid(Graphics g, Color c, float x, float y, float w, float h)
        {
            using (var b = new SolidBrush(c)) g.FillEllipse(b, x, y, w, h);
        }
        static void Curve(Graphics g, Color a, Color b, bool line, float tension, params PointF[] pts)
        {
            using (var path = new GraphicsPath())
            {
                path.AddClosedCurve(pts, tension);
                Fill(g, path, a, b, line);
            }
        }
        // Darken the top band of a shape (husky saddle / cap)
        static void TopShade(Graphics g, GraphicsPath clip, RectangleF band, Color c)
        {
            var old = g.Clip;
            g.SetClip(clip, CombineMode.Intersect);
            using (var b = new LinearGradientBrush(new RectangleF(band.X, band.Y - 1, band.Width, band.Height + 2), c, Color.FromArgb(0, c), LinearGradientMode.Vertical))
                g.FillRectangle(b, band);
            g.Clip = old;
        }
        static void RimLight(Graphics g, RectangleF r, float from, float sweep)
        {
            r.Inflate(-1.4f, -1.4f);
            using (var p = new Pen(Rim, 1.3f) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(p, r, from, sweep);
        }

        void GroundShadow(Graphics g, float cx, float w)
        {
            if ((Ground() - y) / S > 3) return;
            using (var p = EllipsePath(new RectangleF(cx - w / 2, 110.5f, w, 8)))
            using (var b = new PathGradientBrush(p) { CenterColor = Color.FromArgb(70, 8, 14, 34), SurroundColors = new[] { Color.FromArgb(0, 8, 14, 34) } })
                g.FillPath(b, p);
        }

        // Plush tail built from overlapping fluffy puffs, white tip
        static void Tail(Graphics g, PointF a, PointF b, PointF c, PointF d, float w)
        {
            const int n = 15;
            var pts = new PointF[n]; var rad = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1), u = 1 - t;
                pts[i] = P(u * u * u * a.X + 3 * u * u * t * b.X + 3 * u * t * t * c.X + t * t * t * d.X,
                           u * u * u * a.Y + 3 * u * u * t * b.Y + 3 * u * t * t * c.Y + t * t * t * d.Y);
                rad[i] = w * 0.5f * (0.72f + 0.5f * (float)Math.Sin(Math.PI * Math.Min(1.0, t * 1.05)));
                if (t > 0.85f) rad[i] = Math.Max(rad[i], w * 0.62f);
            }
            using (var lb = new SolidBrush(Line))
                for (int i = 0; i < n; i++) g.FillEllipse(lb, pts[i].X - rad[i] - 1.1f, pts[i].Y - rad[i] - 1.1f, (rad[i] + 1.1f) * 2, (rad[i] + 1.1f) * 2);
            using (var fb = new SolidBrush(FurMid))
            using (var wb = new SolidBrush(White))
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)(n - 1);
                    using (var p = Fluffy(new RectangleF(pts[i].X - rad[i], pts[i].Y - rad[i], rad[i] * 2, rad[i] * 2), 6, 0.07f))
                        g.FillPath(t < 0.74f ? fb : wb, p);
                }
            using (var p = new Pen(Color.FromArgb(70, FurDark), w * 0.3f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawBezier(p, P(a.X + 1.5f, a.Y + 2.5f), P(b.X + 1.5f, b.Y + 2.5f), P(c.X + 1.5f, c.Y + 2.5f), P(d.X + 1.5f, d.Y + 2.5f));
            using (var p = new Pen(Color.FromArgb(150, FurLight), w * 0.3f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawBezier(p, P(a.X - 1, a.Y - 2), P(b.X - 1, b.Y - 2), P(c.X - 1, c.Y - 2), P(d.X - 1, d.Y - 2));
            var tip = new RectangleF(pts[n - 1].X - rad[n - 1], pts[n - 1].Y - rad[n - 1], rad[n - 1] * 2, rad[n - 1] * 2);
            using (var p = Fluffy(tip, 7, 0.08f)) Fill(g, p, White, WhiteShade, false);
        }

        static void Leg(Graphics g, float x, float top, float h, bool back)
        {
            float w = 11, r = 5;
            using (var path = new GraphicsPath())
            {
                path.AddArc(x, top, w, r * 2, 180, 180);
                path.AddLine(x + w, top + r, x + w, top + h - r);
                path.AddArc(x, top + h - r * 2, w, r * 2, 0, 180);
                path.CloseFigure();
                Fill(g, path, back ? WhiteShade : White, back ? Color.FromArgb(178, 190, 214) : WhiteShade, false);
                var old = g.Clip;
                g.SetClip(path, CombineMode.Intersect);
                using (var fp = EllipsePath(new RectangleF(x - 4, top - 10, w + 8, h * 0.45f + 12)))
                    Fill(g, fp, back ? FurMid : FurLight, back ? FurDark : FurMid, false);
                g.Clip = old;
                using (var pen = new Pen(Line, 1.25f)) g.DrawPath(pen, path);
            }
            FillEl(g, new RectangleF(x - 1.4f, top + h - 6.5f, w + 2.8f, 7.5f), White, WhiteShade, true);
            using (var p = new Pen(Color.FromArgb(130, 80, 92, 124), 0.9f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(p, x + 3.4f, top + h - 3.2f, x + 3.4f, top + h - 0.6f);
                g.DrawLine(p, x + 7.6f, top + h - 3.2f, x + 7.6f, top + h - 0.6f);
            }
        }

        void Eye(Graphics g, float px, float py, float e, bool closed, bool happy, bool blink)
        {
            using (var lid = new Pen(Color.FromArgb(32, 36, 54), e * 0.17f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                if (closed) { g.DrawArc(lid, px - e / 2, py - e * 0.45f, e, e * 0.75f, 20, 140); return; }
                if (happy) { g.DrawArc(lid, px - e / 2, py - e * 0.15f, e, e * 0.9f, 205, 130); return; }
                if (blink) { g.DrawLine(lid, px - e / 2, py + e * 0.1f, px + e / 2, py + e * 0.1f); return; }
                var er = new RectangleF(px - e / 2, py - e * 0.56f, e, e * 1.12f);
                using (var b = new LinearGradientBrush(er, IrisTop, Iris1, LinearGradientMode.Vertical))
                {
                    var cb = new ColorBlend(3);
                    cb.Colors = new[] { IrisTop, Iris2, Iris1 };
                    cb.Positions = new[] { 0f, 0.5f, 1f };
                    b.InterpolationColors = cb;
                    g.FillEllipse(b, er);
                }
                float pw = e * (st == St.Chase ? 0.64f : 0.52f);
                Solid(g, Color.FromArgb(14, 18, 32), px - pw / 2, py - pw / 2 + e * 0.06f, pw, pw * 1.05f);
                using (var ring = new Pen(Color.FromArgb(150, 18, 46, 110), 0.8f)) g.DrawEllipse(ring, er);
                using (var shine = new Pen(Color.FromArgb(170, 170, 255, 245), e * 0.09f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(shine, er.X + e * 0.14f, er.Y + e * 0.2f, e * 0.72f, e * 0.8f, 40, 100);
                Solid(g, Color.White, px - e * 0.4f, py - e * 0.46f, e * 0.42f, e * 0.42f);
                Solid(g, Color.FromArgb(235, 255, 255, 255), px + e * 0.1f, py + e * 0.1f, e * 0.17f, e * 0.17f);
                g.DrawArc(lid, er.X - e * 0.04f, er.Y - e * 0.02f, e * 1.08f, e * 1.1f, 200, 140);
                g.DrawLine(lid, px + e * 0.48f, py - e * 0.3f, px + e * 0.66f, py - e * 0.44f); // lash flick
            }
        }

        // Chibi husky head, facing right (3/4 view); snout points to +x
        void Head(Graphics g, float cx, float cy, float r, bool closed)
        {
            bool happy = st == St.Happy || st == St.Remind;
            // ears (far ear first)
            Curve(g, FurMid, FurDark, true, 0.32f, P(cx - r * 0.95f, cy - r * 0.18f), P(cx - r * 0.68f, cy - r * 1.46f), P(cx - r * 0.04f, cy - r * 0.8f));
            Curve(g, FurMid, FurDark, true, 0.32f, P(cx - r * 0.02f, cy - r * 0.86f), P(cx + r * 0.52f, cy - r * 1.52f), P(cx + r * 0.92f, cy - r * 0.3f));
            Curve(g, EarIn1, EarIn2, false, 0.36f, P(cx - r * 0.77f, cy - r * 0.45f), P(cx - r * 0.64f, cy - r * 1.16f), P(cx - r * 0.27f, cy - r * 0.78f));
            Curve(g, EarIn1, EarIn2, false, 0.36f, P(cx + r * 0.2f, cy - r * 0.83f), P(cx + r * 0.5f, cy - r * 1.22f), P(cx + r * 0.72f, cy - r * 0.46f));
            using (var tuft = new Pen(Color.FromArgb(230, 255, 255, 255), 1f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                for (int k = 0; k < 3; k++)
                {
                    g.DrawLine(tuft, cx - r * (0.62f - k * 0.1f), cy - r * 0.55f, cx - r * (0.58f - k * 0.08f), cy - r * (0.82f + k * 0.04f));
                    g.DrawLine(tuft, cx + r * (0.36f + k * 0.1f), cy - r * 0.62f, cx + r * (0.42f + k * 0.07f), cy - r * (0.9f - k * 0.02f));
                }
            }
            // fluffy cheeks
            using (var ck = Fluffy(new RectangleF(cx - r * 1.16f, cy + r * 0.0f, r * 0.74f, r * 0.66f), 6, 0.16f)) Fill(g, ck, White, WhiteShade, true);
            using (var ck = Fluffy(new RectangleF(cx + r * 0.46f, cy + r * 0.12f, r * 0.66f, r * 0.58f), 6, 0.16f)) Fill(g, ck, White, WhiteShade, true);
            // head
            var hr = new RectangleF(cx - r * 1.02f, cy - r * 0.96f, r * 2.04f, r * 1.88f);
            using (var hp = EllipsePath(hr))
            {
                Fill(g, hp, FurMid, FurDark, false);
                TopShade(g, hp, new RectangleF(hr.X, hr.Y, hr.Width, r * 0.85f), Color.FromArgb(150, FurDark));
                var old = g.Clip;
                g.SetClip(hp, CombineMode.Intersect);
                FillEl(g, new RectangleF(cx - r * 0.98f, cy + r * 0.1f, r * 1.96f, r * 1.0f), White, WhiteShade, false);    // lower mask
                FillEl(g, new RectangleF(cx - r * 0.68f, cy - r * 0.3f, r * 0.56f, r * 0.54f), White, WhiteShade, false);   // goggle L
                FillEl(g, new RectangleF(cx + r * 0.07f, cy - r * 0.32f, r * 0.54f, r * 0.52f), White, WhiteShade, false);  // goggle R
                Curve(g, White, White, false, 0.45f, P(cx - r * 0.13f, cy + r * 0.12f), P(cx - r * 0.02f, cy - r * 0.78f), P(cx + r * 0.1f, cy + r * 0.12f));
                Solid(g, Color.FromArgb(55, 255, 255, 255), cx - r * 0.66f, cy - r * 0.9f, r * 0.8f, r * 0.4f);
                g.Clip = old;
                RimLight(g, hr, 195, 75);
                using (var pen = new Pen(Line, 1.25f)) g.DrawPath(pen, hp);
            }
            // blush
            Solid(g, Color.FromArgb(120, 255, 150, 185), cx - r * 0.78f, cy + r * 0.2f, r * 0.4f, r * 0.2f);
            Solid(g, Color.FromArgb(100, 255, 150, 185), cx + r * 0.08f, cy + r * 0.24f, r * 0.32f, r * 0.17f);
            // muzzle
            var mr = new RectangleF(cx + r * 0.32f, cy + r * 0.1f, r * 0.92f, r * 0.62f);
            FillEl(g, mr, White, WhiteShade, true);
            Solid(g, Color.FromArgb(40, 60, 70, 100), mr.X + r * 0.12f, mr.Bottom - r * 0.18f, r * 0.6f, r * 0.14f);
            using (var dot = new SolidBrush(Color.FromArgb(120, 120, 134, 166)))
                for (int k = 0; k < 3; k++) g.FillEllipse(dot, cx + r * (0.62f + k * 0.11f), cy + r * (0.42f - k * 0.03f), 1.3f, 1.3f);
            // nose
            FillEl(g, new RectangleF(cx + r * 1.0f, cy + r * 0.08f, r * 0.33f, r * 0.24f), Color.FromArgb(120, 126, 150), NoseC, false);
            Solid(g, Color.FromArgb(225, 255, 255, 255), cx + r * 1.06f, cy + r * 0.11f, r * 0.12f, r * 0.07f);
            using (var mp = new Pen(Color.FromArgb(76, 80, 104), 1.15f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(mp, cx + r * 1.16f, cy + r * 0.32f, cx + r * 1.14f, cy + r * 0.4f);
                if (happy || st == St.Chase)
                {
                    using (var mouth = EllipsePath(new RectangleF(cx + r * 0.8f, cy + r * 0.38f, r * 0.44f, r * 0.32f)))
                    using (var b = new SolidBrush(Color.FromArgb(118, 46, 68))) g.FillPath(b, mouth);
                    FillEl(g, new RectangleF(cx + r * 0.86f, cy + r * 0.5f, r * 0.32f, r * 0.44f), Tongue1, Tongue2, false);
                    g.DrawLine(mp, cx + r * 1.02f, cy + r * 0.56f, cx + r * 1.02f, cy + r * 0.8f);
                    Solid(g, Color.FromArgb(150, 255, 255, 255), cx + r * 0.9f, cy + r * 0.56f, r * 0.08f, r * 0.12f);
                }
                else
                {
                    g.DrawArc(mp, cx + r * 0.94f, cy + r * 0.32f, r * 0.2f, r * 0.16f, 0, 180);
                    g.DrawArc(mp, cx + r * 1.13f, cy + r * 0.32f, r * 0.2f, r * 0.16f, 0, 180);
                }
            }
            // eyes
            bool blink = (frame % 150) < 4;
            Eye(g, cx - r * 0.4f, cy - r * 0.1f, r * 0.46f, closed, happy, blink);
            Eye(g, cx + r * 0.33f, cy - r * 0.12f, r * 0.42f, closed, happy, blink);
        }

        void Collar(Graphics g, float cx, float cy, float w)
        {
            using (var p = new Pen(Collar2, 5f) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(p, cx - w / 2, cy - 7, w, 12, 25, 130);
            using (var p = new Pen(Collar1, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(p, cx - w / 2, cy - 8, w, 12, 38, 104);
            var tr = new RectangleF(cx - 4.2f, cy + 3.4f, 8.4f, 8.4f);
            FillEl(g, tr, White, TagColor, true);
            using (var b = new SolidBrush(Color.FromArgb(150, 120, 134, 166)))
            {
                g.FillEllipse(b, cx - 1.6f, cy + 7.6f, 3.2f, 2.6f);
                g.FillEllipse(b, cx - 2.4f, cy + 5.6f, 1.3f, 1.3f); g.FillEllipse(b, cx - 0.65f, cy + 5.0f, 1.3f, 1.3f); g.FillEllipse(b, cx + 1.1f, cy + 5.6f, 1.3f, 1.3f);
            }
            Solid(g, Color.White, tr.X + 1.6f, tr.Y + 1.4f, 2.2f, 2.2f);
        }

        void Body(Graphics g, RectangleF r, float saddleH)
        {
            using (var bp = Fluffy(r, 14, 0.035f))
            {
                Fill(g, bp, FurMid, FurDark, true);
                TopShade(g, bp, new RectangleF(r.X, r.Y - 2, r.Width, saddleH), Color.FromArgb(215, FurDark));
                var old = g.Clip;
                g.SetClip(bp, CombineMode.Intersect);
                FillEl(g, new RectangleF(r.X + r.Width * 0.18f, r.Bottom - r.Height * 0.42f, r.Width * 0.72f, r.Height * 0.62f), White, WhiteShade, false);
                g.Clip = old;
                RimLight(g, r, 200, 80);
            }
        }

        void DrawStand(Graphics g)
        {
            bool walking = st == St.Walk || st == St.Chase;
            float speed = st == St.Chase ? 0.6f : 0.35f;
            float bob = walking ? (float)Math.Abs(Math.Sin(frame * speed)) * 2 : 0;
            float wag = st == St.Happy || st == St.Chase ? (float)Math.Sin(frame * 0.8) * 9 : (float)Math.Sin(frame * 0.25) * 4;
            float ph = walking ? (float)Math.Sin(frame * speed) * 4 : 0;
            Tail(g, P(34, 86 - bob), P(14, 80), P(10 + wag * 0.4f, 50 + wag), P(30 + wag * 0.3f, 50 + wag * 0.5f), 13);
            Leg(g, 36 - ph, 93 - bob, 22 + bob, true); Leg(g, 49 + ph, 93 - bob, 22 + bob, true);
            Body(g, new RectangleF(28, 72 - bob, 68, 36), 14);
            Solid(g, Color.FromArgb(45, 20, 28, 60), 70, 74 - bob, 28, 14); // occlusion under the head
            Leg(g, 70 + ph, 93 - bob, 22 + bob, false); Leg(g, 82 - ph, 93 - bob, 22 + bob, false);
            using (var ruff = Fluffy(new RectangleF(72, 72 - bob, 28, 30), 7, 0.13f)) Fill(g, ruff, White, WhiteShade, true);
            Collar(g, 88, 78 - bob, 26);
            Head(g, 92, 56 - bob, 24, false);
        }

        void DrawSit(Graphics g)
        {
            float wag = st == St.Remind ? (float)Math.Sin(frame * 0.6) * 5 : (float)Math.Sin(frame * 0.12) * 3;
            Tail(g, P(46, 108), P(24, 116), P(14 + wag, 100), P(24 + wag, 86), 13);
            using (var hp = Fluffy(new RectangleF(36, 93, 32, 22), 8, 0.04f)) Fill(g, hp, FurMid, FurDark, true);
            Body(g, new RectangleF(42, 66, 46, 50), 18);
            using (var chest = Fluffy(new RectangleF(52, 70, 30, 40), 8, 0.1f)) Fill(g, chest, White, WhiteShade, true);
            Leg(g, 55, 95, 21, false); Leg(g, 69, 95, 21, false);
            Collar(g, 66, 70, 30);
            Head(g, 66, 46, 25, false);
        }

        void DrawSleep(Graphics g)
        {
            float br = (float)Math.Sin(frame * 0.08) * 1.3f;
            Body(g, new RectangleF(26, 86 - br, 72, 30 + br), 12);
            FillEl(g, new RectangleF(84, 106, 18, 9), White, WhiteShade, true);
            FillEl(g, new RectangleF(92, 108.5f, 16, 8), White, WhiteShade, true);
            Tail(g, P(30, 104), P(12, 118), P(56, 124), P(88, 115), 13);
            Collar(g, 80, 86, 24);
            Head(g, 82, 92, 21, true);
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
                            g.TranslateTransform(hx + (float)(ph * 18 * (dir > 0 ? 1 : -1)), 60 - (float)(ph * 46));
                            g.ScaleTransform(0.7f + (float)ph * 0.6f, 0.7f + (float)ph * 0.6f);
                            g.DrawString("z", f, b, 0, 0);
                            g.Restore(s0);
                        }
                    }
            }
            if (st == St.Happy)
            {
                float hx = HeadX(92);
                int n = mem.Affection > 80 ? 5 : 3;
                for (int k = 0; k < n; k++)
                {
                    double ph = (frame * 0.03 + k / (double)n) % 1.0;
                    float hxk = hx - 20 + k * (40f / (n - 1)) + (float)Math.Sin(frame * 0.2 + k) * 2;
                    Heart(g, hxk, 26 - (float)ph * 24, 4.5f, (int)(255 * (1 - ph)));
                }
            }
            if (st == St.Remind)
            {
                float hx = HeadX(66);
                float bx = dir > 0 ? hx + 12 : hx - 50;
                bx = Math.Max(2, Math.Min(CW - 40, bx));
                float by = 2 + (float)Math.Sin(frame * 0.15) * 1.5f;
                using (var path = Rounded(bx, by, 38, 26, 9))
                {
                    using (var b = new SolidBrush(Color.FromArgb(238, 244, 249, 255))) g.FillPath(b, path);
                    using (var p = new Pen(Color.FromArgb(120, 160, 210), 1.5f)) g.DrawPath(p, path);
                }
                float dx = bx + 19, dy = by + 4;
                using (var b = new SolidBrush(Color.FromArgb(70, 160, 240)))
                {
                    g.FillEllipse(b, dx - 7, dy + 6, 14, 14);
                    g.FillPolygon(b, new[] { P(dx, dy - 1), P(dx - 6.3f, dy + 10), P(dx + 6.3f, dy + 10) });
                }
                Solid(g, Color.FromArgb(220, 255, 255, 255), dx - 4, dy + 9, 3.5f, 5);
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
                    using (var b = new SolidBrush(FurMid))
                    {
                        g.FillPolygon(b, new[] { P(4, 14), P(7, 0), P(14, 8) });
                        g.FillPolygon(b, new[] { P(18, 8), P(25, 0), P(28, 14) });
                        g.FillEllipse(b, 3, 6, 26, 24);
                    }
                    using (var b = new SolidBrush(White)) { g.FillEllipse(b, 6, 17, 20, 13); g.FillEllipse(b, 14, 7, 4, 9); }
                    using (var b = new SolidBrush(Iris2)) { g.FillEllipse(b, 9, 13, 5, 6); g.FillEllipse(b, 18, 13, 5, 6); }
                    using (var b = new SolidBrush(NoseC)) g.FillEllipse(b, 13, 21, 6, 4);
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

        protected override void OnFormClosed(FormClosedEventArgs e) { SaveMem(); tray.Visible = false; base.OnFormClosed(e); }
    }
}
