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
        public float Size = 1.4f;
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
                        case "size": m.Size = Math.Max(0.8f, Math.Min(2.5f, float.Parse(v, CI))); break;
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
            sb.AppendLine("size=" + Size.ToString("0.0", CI));
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
        Bitmap canvas, petLayer;
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
        static readonly Color FurTop = Color.FromArgb(82, 96, 128), FurMid = Color.FromArgb(124, 140, 170), FurLight = Color.FromArgb(166, 182, 208),
            White = Color.FromArgb(250, 251, 254), WhiteShade = Color.FromArgb(210, 219, 236), Line = Color.FromArgb(160, 50, 60, 90),
            EarIn1 = Color.FromArgb(253, 222, 232), EarIn2 = Color.FromArgb(230, 178, 202),
            Iris1 = Color.FromArgb(160, 218, 255), Iris2 = Color.FromArgb(34, 104, 210),
            NoseC = Color.FromArgb(36, 40, 54), Tongue1 = Color.FromArgb(255, 160, 180), Tongue2 = Color.FromArgb(232, 96, 128),
            Collar1 = Color.FromArgb(120, 236, 236), Collar2 = Color.FromArgb(26, 150, 190), TagColor = Color.FromArgb(188, 200, 222);

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
            canvas = new Bitmap(PW, PH, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            petLayer = new Bitmap(PW, PH, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
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
            foreach (var opt in new[] { new KeyValuePair<string, float>("Nhỏ", 1.0f), new KeyValuePair<string, float>("Vừa", 1.4f), new KeyValuePair<string, float>("Lớn", 1.8f) })
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
            timer.Interval = (s == St.Sleep || s == St.Sit) ? 50 : 33;
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
                g.ScaleTransform(S, S);
                if (dir < 0) { g.TranslateTransform(CW, 0); g.ScaleTransform(-1, 1); }
                if (st == St.Sleep) DrawSleep(g);
                else if (st == St.Sit || st == St.Remind) DrawSit(g);
                else DrawStand(g);
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
                float o = 1.7f * S;
                for (int k = 0; k < 12; k++)
                {
                    double a = k * Math.PI / 6;
                    int dx = (int)Math.Round(Math.Cos(a) * o), dy = (int)Math.Round(Math.Sin(a) * o);
                    g.DrawImage(petLayer, new Rectangle(dx, dy, PW, PH), 0, 0, PW, PH, GraphicsUnit.Pixel, whiteAttr);
                }
                g.DrawImageUnscaled(petLayer, 0, 0);
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
        static void Blob(Graphics g, float x, float y, float w, float h, Color a, Color b, bool line)
        {
            var r = new RectangleF(x, y, w, h);
            using (var br = Grad(r, a, b)) g.FillEllipse(br, r);
            if (line) using (var p = new Pen(Line, 1.6f)) g.DrawEllipse(p, r);
        }
        static void Solid(Graphics g, Color c, float x, float y, float w, float h)
        {
            using (var b = new SolidBrush(c)) g.FillEllipse(b, x, y, w, h);
        }
        static void Curve(Graphics g, Color a, Color b, bool line, float tension, params PointF[] pts)
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var p in pts) { x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); }
            using (var br = Grad(new RectangleF(x0, y0, x1 - x0, y1 - y0), a, b)) g.FillClosedCurve(br, pts, FillMode.Alternate, tension);
            if (line) using (var pen = new Pen(Line, 1.6f) { LineJoin = LineJoin.Round }) g.DrawClosedCurve(pen, pts, tension, FillMode.Alternate);
        }

        void GroundShadow(Graphics g, float cx, float w)
        {
            if ((Ground() - y) / S > 3) return;
            using (var b = new SolidBrush(Color.FromArgb(55, 10, 16, 36))) g.FillEllipse(b, cx - w / 2, 111, w, 7);
        }

        // Fluffy curled husky tail with a white tip
        static void Tail(Graphics g, PointF a, PointF b, PointF c, PointF d, float w)
        {
            using (var p = new Pen(Line, w + 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawBezier(p, a, b, c, d);
            using (var p = new Pen(FurMid, w) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawBezier(p, a, b, c, d);
            using (var p = new Pen(Color.FromArgb(140, FurLight), w * 0.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawBezier(p, P(a.X - 1, a.Y - 1.5f), P(b.X - 1, b.Y - 1.5f), P(c.X - 1, c.Y - 1.5f), P(d.X - 1, d.Y - 1.5f));
            Blob(g, d.X - w * 0.72f, d.Y - w * 0.72f, w * 1.44f, w * 1.44f, White, WhiteShade, true);
        }

        static void Leg(Graphics g, float x, float top, float h)
        {
            float w = 11, r = 5;
            using (var path = new GraphicsPath())
            {
                path.AddArc(x, top, w, r * 2, 180, 180);
                path.AddLine(x + w, top + r, x + w, top + h - r);
                path.AddArc(x, top + h - r * 2, w, r * 2, 0, 180);
                path.CloseFigure();
                var rect = new RectangleF(x, top, w, h);
                using (var b = Grad(rect, White, WhiteShade)) g.FillPath(b, path);
                g.SetClip(path, CombineMode.Replace);
                using (var b = Grad(new RectangleF(x, top, w, h * 0.45f), FurMid, FurLight)) g.FillRectangle(b, x, top, w, h * 0.45f);
                g.ResetClip();
                using (var p = new Pen(Line, 1.6f)) g.DrawPath(p, path);
            }
            Blob(g, x - 1.2f, top + h - 6.5f, w + 2.4f, 7.5f, White, WhiteShade, true);
            using (var p = new Pen(Color.FromArgb(120, 70, 80, 110), 1f))
            {
                g.DrawLine(p, x + 3.6f, top + h - 3, x + 3.6f, top + h);
                g.DrawLine(p, x + 7.4f, top + h - 3, x + 7.4f, top + h);
            }
        }

        // Chibi husky head, facing right; snout points to +x
        void Head(Graphics g, float cx, float cy, float r, bool closed)
        {
            bool happy = st == St.Happy || st == St.Remind;
            // ears
            Curve(g, FurTop, FurMid, true, 0.3f, P(cx - r * 0.95f, cy - r * 0.2f), P(cx - r * 0.66f, cy - r * 1.45f), P(cx - r * 0.05f, cy - r * 0.8f));
            Curve(g, FurTop, FurMid, true, 0.3f, P(cx - r * 0.02f, cy - r * 0.85f), P(cx + r * 0.52f, cy - r * 1.5f), P(cx + r * 0.9f, cy - r * 0.32f));
            Curve(g, EarIn1, EarIn2, false, 0.35f, P(cx - r * 0.76f, cy - r * 0.45f), P(cx - r * 0.62f, cy - r * 1.15f), P(cx - r * 0.28f, cy - r * 0.78f));
            Curve(g, EarIn1, EarIn2, false, 0.35f, P(cx + r * 0.2f, cy - r * 0.82f), P(cx + r * 0.5f, cy - r * 1.2f), P(cx + r * 0.7f, cy - r * 0.48f));
            // cheek fluff
            Curve(g, White, WhiteShade, true, 0.25f, P(cx - r * 0.8f, cy + r * 0.0f), P(cx - r * 1.14f, cy + r * 0.5f), P(cx - r * 0.55f, cy + r * 0.74f));
            // head
            var hr = new RectangleF(cx - r * 1.02f, cy - r * 0.95f, r * 2.04f, r * 1.86f);
            using (var br = Grad(hr, FurTop, FurLight)) g.FillEllipse(br, hr);
            using (var hp = new GraphicsPath())
            {
                hp.AddEllipse(hr);
                g.SetClip(hp, CombineMode.Replace);
                Blob(g, cx - r * 0.95f, cy - r * 0.02f, r * 1.9f, r * 1.05f, White, WhiteShade, false);        // white mask
                Curve(g, White, White, false, 0.45f, P(cx - r * 0.2f, cy + r * 0.05f), P(cx - r * 0.02f, cy - r * 0.8f), P(cx + r * 0.16f, cy + r * 0.05f)); // forehead stripe
                Solid(g, White, cx - r * 0.56f, cy - r * 0.52f, r * 0.28f, r * 0.17f);                         // eyebrow dots
                Solid(g, White, cx + r * 0.16f, cy - r * 0.54f, r * 0.26f, r * 0.16f);
                Solid(g, Color.FromArgb(50, 255, 255, 255), cx - r * 0.62f, cy - r * 0.88f, r * 0.8f, r * 0.42f); // top sheen
                g.ResetClip();
            }
            using (var p = new Pen(Line, 1.6f)) g.DrawEllipse(p, hr);
            // blush
            Solid(g, Color.FromArgb(115, 255, 150, 182), cx - r * 0.74f, cy + r * 0.16f, r * 0.38f, r * 0.21f);
            Solid(g, Color.FromArgb(100, 255, 150, 182), cx + r * 0.1f, cy + r * 0.2f, r * 0.3f, r * 0.18f);
            // snout + nose
            Blob(g, cx + r * 0.3f, cy + r * 0.08f, r * 0.92f, r * 0.62f, White, WhiteShade, true);
            Blob(g, cx + r * 0.98f, cy + r * 0.07f, r * 0.34f, r * 0.26f, Color.FromArgb(78, 84, 104), NoseC, false);
            Solid(g, Color.FromArgb(210, 255, 255, 255), cx + r * 1.04f, cy + r * 0.1f, r * 0.11f, r * 0.06f);
            using (var mp = new Pen(Color.FromArgb(70, 74, 96), 1.3f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                if (happy || st == St.Chase)
                {
                    Solid(g, Color.FromArgb(120, 50, 70), cx + r * 0.62f, cy + r * 0.4f, r * 0.44f, r * 0.3f);
                    Blob(g, cx + r * 0.68f, cy + r * 0.5f, r * 0.32f, r * 0.44f, Tongue1, Tongue2, false);
                    g.DrawLine(mp, cx + r * 0.84f, cy + r * 0.56f, cx + r * 0.84f, cy + r * 0.78f);
                }
                else
                {
                    g.DrawArc(mp, cx + r * 0.86f, cy + r * 0.32f, r * 0.2f, r * 0.17f, 0, 180);
                    g.DrawArc(mp, cx + r * 1.05f, cy + r * 0.32f, r * 0.2f, r * 0.17f, 0, 180);
                }
            }
            // eyes
            float e = r * 0.42f;
            bool blink = (frame % 150) < 4;
            using (var p = new Pen(Color.FromArgb(40, 46, 66), 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                foreach (var c in new[] { P(cx - r * 0.4f, cy - r * 0.14f), P(cx + r * 0.32f, cy - r * 0.16f) })
                {
                    float px = c.X, py = c.Y;
                    if (closed) g.DrawArc(p, px - e / 2, py - e / 2, e, e * 0.8f, 15, 150);
                    else if (happy) g.DrawArc(p, px - e / 2, py - e * 0.2f, e, e, 200, 140);
                    else if (blink) g.DrawLine(p, px - e / 2, py + 1, px + e / 2, py + 1);
                    else
                    {
                        var er = new RectangleF(px - e / 2, py - e * 0.54f, e, e * 1.08f);
                        using (var ep = new GraphicsPath())
                        {
                            ep.AddEllipse(er);
                            using (var pb = new PathGradientBrush(ep) { CenterColor = Iris1, SurroundColors = new[] { Iris2 }, CenterPoint = P(px, py + e * 0.18f) })
                                g.FillEllipse(pb, er);
                        }
                        float pw = e * (st == St.Chase ? 0.62f : 0.5f);
                        Solid(g, Color.FromArgb(18, 22, 34), px - pw / 2, py - pw / 2 + e * 0.04f, pw, pw);
                        using (var op = new Pen(Color.FromArgb(40, 46, 66), 1.3f)) g.DrawEllipse(op, er);
                        Solid(g, Color.White, px - e * 0.38f, py - e * 0.44f, e * 0.38f, e * 0.38f);
                        Solid(g, Color.FromArgb(220, 255, 255, 255), px + e * 0.1f, py + e * 0.12f, e * 0.17f, e * 0.17f);
                    }
                }
            }
        }

        void Collar(Graphics g, float cx, float cy, float w)
        {
            using (var p = new Pen(Collar2, 5f) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(p, cx - w / 2, cy - 7, w, 12, 25, 130);
            using (var p = new Pen(Collar1, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(p, cx - w / 2, cy - 8, w, 12, 35, 110);
            Blob(g, cx - 4, cy + 3.5f, 8, 8, White, TagColor, true);
            Solid(g, Color.White, cx - 2.4f, cy + 4.8f, 2.6f, 2.6f);
        }

        void DrawStand(Graphics g)
        {
            bool walking = st == St.Walk || st == St.Chase;
            float speed = st == St.Chase ? 0.6f : 0.35f;
            float bob = walking ? (float)Math.Abs(Math.Sin(frame * speed)) * 2 : 0;
            float wag = st == St.Happy || st == St.Chase ? (float)Math.Sin(frame * 0.8) * 9 : (float)Math.Sin(frame * 0.25) * 4;
            float ph = walking ? (float)Math.Sin(frame * speed) * 4 : 0;
            Tail(g, P(34, 86 - bob), P(14, 80), P(10 + wag * 0.4f, 50 + wag), P(30 + wag * 0.3f, 50 + wag * 0.5f), 12);
            Leg(g, 36 - ph, 93 - bob, 22 + bob); Leg(g, 49 + ph, 93 - bob, 22 + bob);
            Blob(g, 28, 72 - bob, 68, 36, FurTop, FurLight, true);
            Blob(g, 42, 94 - bob, 42, 12, White, WhiteShade, false);
            Leg(g, 70 + ph, 93 - bob, 22 + bob); Leg(g, 82 - ph, 93 - bob, 22 + bob);
            Curve(g, White, WhiteShade, true, 0.45f, P(76, 78 - bob), P(88, 74 - bob), P(98, 82 - bob), P(95, 94 - bob),
                  P(88, 100 - bob), P(82, 95 - bob), P(76, 100 - bob), P(73, 89 - bob));
            Collar(g, 88, 78 - bob, 26);
            Head(g, 92, 56 - bob, 24, false);
        }

        void DrawSit(Graphics g)
        {
            float wag = st == St.Remind ? (float)Math.Sin(frame * 0.6) * 5 : (float)Math.Sin(frame * 0.12) * 3;
            Tail(g, P(46, 108), P(24, 116), P(14 + wag, 100), P(24 + wag, 86), 12);
            Blob(g, 36, 94, 30, 21, FurTop, FurLight, true);
            Blob(g, 42, 68, 46, 48, FurTop, FurLight, true);
            Curve(g, White, WhiteShade, true, 0.45f, P(52, 76), P(66, 70), P(80, 76), P(80, 92), P(74, 108), P(66, 102), P(58, 108), P(52, 92));
            Leg(g, 55, 95, 21); Leg(g, 69, 95, 21);
            Collar(g, 66, 70, 30);
            Head(g, 66, 47, 25, false);
        }

        void DrawSleep(Graphics g)
        {
            float br = (float)Math.Sin(frame * 0.08) * 1.3f;
            Blob(g, 26, 88 - br, 72, 28 + br, FurTop, FurLight, true);
            Blob(g, 40, 104 - br, 40, 10, White, WhiteShade, false);
            Blob(g, 86, 107, 18, 9, White, WhiteShade, true);
            Tail(g, P(30, 104), P(12, 118), P(56, 123), P(86, 116), 12);
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
