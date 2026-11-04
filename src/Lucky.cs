// Lucky - a Husky desktop pet that lives on top of your Windows taskbar.
// Sprites are pre-rendered by art/render_sprites.py and embedded into the exe by build.ps1.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
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

    enum St { Walk, Sit, Sleep, Drag, Fall, Happy, Chase, Remind, Fetch, Carry, GoEat, Eat, Focus }

    // ================================================================ memory
    class Memory
    {
        public DateTime FirstMet = DateTime.Now, LastSeen = DateTime.Now, LastFed = DateTime.Now;
        public int Pets, Chases, Reminders, Naps, Drops, Balls, Treats, FocusSessions, DayMilestone, PetMilestone;
        public double Affection = 60, PosX = -1;
        public int Facing = 1;
        public bool RemindOn = true;
        public float Size = 0.85f;
        public string LastMorning = "", LastNight = "";

        public static string Folder { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LuckyPet"); } }
        public static string FilePath { get { return Path.Combine(Folder, "memory.txt"); } }
        static readonly CultureInfo CI = CultureInfo.InvariantCulture;
        public int DaysTogether { get { return (DateTime.Now.Date - FirstMet.Date).Days + 1; } }
        public bool Hungry { get { return (DateTime.Now - LastFed).TotalHours >= 6; } }

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
                        case "last_fed": m.LastFed = DateTime.Parse(v, CI, DateTimeStyles.RoundtripKind); break;
                        case "pets": m.Pets = int.Parse(v, CI); break;
                        case "chases": m.Chases = int.Parse(v, CI); break;
                        case "reminders": m.Reminders = int.Parse(v, CI); break;
                        case "naps": m.Naps = int.Parse(v, CI); break;
                        case "drops": m.Drops = int.Parse(v, CI); break;
                        case "balls": m.Balls = int.Parse(v, CI); break;
                        case "treats": m.Treats = int.Parse(v, CI); break;
                        case "focus_sessions": m.FocusSessions = int.Parse(v, CI); break;
                        case "day_milestone": m.DayMilestone = int.Parse(v, CI); break;
                        case "pet_milestone": m.PetMilestone = int.Parse(v, CI); break;
                        case "affection": m.Affection = Math.Max(0, Math.Min(100, double.Parse(v, CI))); break;
                        case "pos_x": m.PosX = double.Parse(v, CI); break;
                        case "facing": m.Facing = int.Parse(v, CI) < 0 ? -1 : 1; break;
                        case "remind_on": m.RemindOn = v == "true"; break;
                        case "size": m.Size = Math.Max(0.6f, Math.Min(2.5f, float.Parse(v, CI))); break;
                        case "last_morning": m.LastMorning = v; break;
                        case "last_night": m.LastNight = v; break;
                    }
                }
                catch { }
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
            sb.AppendLine("last_fed=" + LastFed.ToString("o", CI));
            sb.AppendLine("affection=" + Affection.ToString("0.00", CI));
            sb.AppendLine("pets=" + Pets.ToString(CI));
            sb.AppendLine("chases=" + Chases.ToString(CI));
            sb.AppendLine("balls=" + Balls.ToString(CI));
            sb.AppendLine("treats=" + Treats.ToString(CI));
            sb.AppendLine("naps=" + Naps.ToString(CI));
            sb.AppendLine("drops=" + Drops.ToString(CI));
            sb.AppendLine("reminders=" + Reminders.ToString(CI));
            sb.AppendLine("focus_sessions=" + FocusSessions.ToString(CI));
            sb.AppendLine("day_milestone=" + DayMilestone.ToString(CI));
            sb.AppendLine("pet_milestone=" + PetMilestone.ToString(CI));
            sb.AppendLine("pos_x=" + PosX.ToString("0", CI));
            sb.AppendLine("facing=" + Facing.ToString(CI));
            sb.AppendLine("remind_on=" + (RemindOn ? "true" : "false"));
            sb.AppendLine("size=" + Size.ToString("0.00", CI));
            sb.AppendLine("last_morning=" + LastMorning);
            sb.AppendLine("last_night=" + LastNight);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null); else File.Move(tmp, FilePath);
        }
    }

    // ================================================================ per-pixel-alpha window
    class LayerWin : Form
    {
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; public POINT(int a, int b) { x = a; y = b; } }
        [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; public SIZE(int a, int b) { cx = a; cy = b; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
        [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);

        public LayerWin() { FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; }
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x80000 | 0x80 | 0x8; return cp; } // layered | toolwindow | topmost
        }
        protected override bool ShowWithoutActivation { get { return true; } }

        public void Push(Bitmap bmp, int x, int y)
        {
            IntPtr screenDc = GetDC(IntPtr.Zero), memDc = CreateCompatibleDC(screenDc);
            IntPtr hb = bmp.GetHbitmap(Color.FromArgb(0)), old = SelectObject(memDc, hb);
            var size = new SIZE(bmp.Width, bmp.Height); var src = new POINT(0, 0); var dst = new POINT(x, y);
            var bf = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref bf, 2);
            SelectObject(memDc, old); DeleteObject(hb); DeleteDC(memDc); ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    // ================================================================ the pet
    class PetForm : LayerWin
    {
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
        [DllImport("user32.dll", CharSet = CharSet.Auto)] static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        const int CW = 130, CH = 120, SPRITE_W = 260;      // logical canvas; sprites stored at 2x
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const int RemindMinutes = 45, FocusMinutes = 25;
        static readonly string[] AnimNames = { "walk", "run", "happy", "sit", "sitblink", "sithappy", "sleep", "eat" };
        static readonly int[] DayMilestones = { 3, 7, 30, 100, 365 };
        static readonly int[] PetMilestones = { 10, 50, 100, 500, 1000 };
        static readonly string[] Cheers = {
            "Gâu! Hôm nay bạn sẽ gặp may đó ✦", "Bạn đang làm rất tốt luôn!", "Nghỉ mắt 20 giây, nhìn ra xa nha",
            "Lucky thương bạn nhiều ♥", "Uống ngụm nước đi nè", "Cố lên, sắp xong rồi!",
            "Thẳng lưng lên nào, ngồi đúng tư thế nha", "Việc khó cỡ nào cũng có Lucky ở đây",
            "Hít thở sâu 3 lần nào…", "Ném bóng cho Lucky chơi hông?" };
        static readonly string[] PetLines = { "Gâu gâu! ♥", "Thêm cái nữa đi!", "Hì hì, nhột quá", "Lucky vui lắm!", "Bạn là nhất ♥" };

        float S, dpiScale = 1; int PW, PH;
        Bitmap canvas;
        Dictionary<string, Bitmap[]> sprR = new Dictionary<string, Bitmap[]>(), sprL = new Dictionary<string, Bitmap[]>();
        Timer timer;
        Random rnd = new Random();
        St st = St.Sit;
        double x, y, vy;
        int dir = 1, stateTicks = 60, frame = 0, fsCheck = 0, animStart = Environment.TickCount, blinkUntil = 0;
        bool dragging, moved, hiddenFs, remindOn = true, dirty;
        Point dragOff, downPt;
        NotifyIcon tray;
        ToolStripMenuItem focusItem;
        DateTime nextRemind, nextChat, lastSave = DateTime.Now, lastDecay = DateTime.Now, lastClock = DateTime.MinValue, focusEnd;
        Memory mem;
        string pendingTitle, pendingText;

        // toys & speech
        LayerWin ballWin, boneWin, bubbleWin;
        bool ballActive, ballCarried; double bx, by, bvx, bvy, bRot; int ballIdleSince;
        bool boneActive; double boneX, boneY, boneVy; int boneBites;
        int eatStart;
        string bubbleText; int bubbleUntil;

        public PetForm()
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) dpiScale = g.DpiX / 96f;
            bool isNew;
            mem = Memory.Load(out isNew);
            remindOn = mem.RemindOn;
            dir = mem.Facing;
            S = dpiScale * mem.Size;
            Alloc();
            var wa = Screen.PrimaryScreen.WorkingArea;
            x = mem.PosX >= 0 ? Math.Max(wa.Left, Math.Min(wa.Right - PW, mem.PosX)) : wa.Left + wa.Width * 0.72;
            y = Ground();
            Size = new Size(PW, PH); Location = new Point((int)x, (int)y);
            Greet(isNew);

            ballWin = new LayerWin(); boneWin = new LayerWin(); bubbleWin = new LayerWin();
            ballWin.MouseDown += delegate { KickBall(); };
            bubbleWin.MouseDown += delegate { bubbleUntil = 0; };

            var menu = new ContextMenuStrip();
            menu.Items.Add("Xoa đầu Lucky 💕", null, delegate { Pet(); });
            menu.Items.Add("Ném bóng 🎾", null, delegate { ThrowBall(true); });
            menu.Items.Add("Cho ăn xương 🦴", null, delegate { DropBone(); });
            focusItem = new ToolStripMenuItem("Tập trung 25 phút 🎯");
            focusItem.Click += delegate { ToggleFocus(); };
            menu.Items.Add(focusItem);
            menu.Items.Add("Kỷ niệm với Lucky 📖", null, delegate { ShowMemories(); });
            menu.Items.Add(new ToolStripSeparator());
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
            DoubleClick += delegate { ThrowBall(false); };
            SystemEvents.SessionEnding += delegate { SaveMem(); };
            nextRemind = DateTime.Now.AddMinutes(RemindMinutes);
            nextChat = DateTime.Now.AddMinutes(rnd.Next(12, 25));
            timer = new Timer { Interval = 40 };
            timer.Tick += Tick;
            timer.Start();
            SetState(St.Happy, 60);
        }

        // ---------------------------------------------------------- sprites
        void Alloc()
        {
            PW = (int)(CW * S); PH = (int)(CH * S);
            if (canvas != null) canvas.Dispose();
            canvas = new Bitmap(PW, PH, PixelFormat.Format32bppArgb);
            foreach (var arr in sprR.Values) foreach (var b in arr) b.Dispose();
            foreach (var arr in sprL.Values) foreach (var b in arr) b.Dispose();
            sprR.Clear(); sprL.Clear();
            var asm = Assembly.GetExecutingAssembly();
            using (var ia = new ImageAttributes())
            {
                ia.SetWrapMode(WrapMode.TileFlipXY);
                foreach (var n in AnimNames)
                {
                    using (var s = asm.GetManifestResourceStream(n + ".png"))
                    using (var sheet = new Bitmap(s))
                    {
                        int cnt = sheet.Width / SPRITE_W;
                        var r = new Bitmap[cnt]; var l = new Bitmap[cnt];
                        for (int i = 0; i < cnt; i++)
                        {
                            var b = new Bitmap(PW, PH, PixelFormat.Format32bppPArgb);
                            using (var g = Graphics.FromImage(b))
                            {
                                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                                g.CompositingQuality = CompositingQuality.HighQuality;
                                g.DrawImage(sheet, new Rectangle(0, 0, PW, PH), i * SPRITE_W, 0, SPRITE_W, sheet.Height, GraphicsUnit.Pixel, ia);
                            }
                            r[i] = b;
                            var f = (Bitmap)b.Clone(); f.RotateFlip(RotateFlipType.RotateNoneFlipX); l[i] = f;
                        }
                        sprR[n] = r; sprL[n] = l;
                    }
                }
            }
            GC.Collect();
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

        string Anim(out int fps)
        {
            switch (st)
            {
                case St.Walk: fps = 10; return "walk";
                case St.Chase: case St.Fetch: case St.Carry: case St.GoEat: fps = 14; return "run";
                case St.Happy: case St.Drag: case St.Fall: fps = 12; return "happy";
                case St.Remind: fps = 8; return "sithappy";
                case St.Sleep: case St.Focus: fps = 4; return "sleep";
                case St.Eat: fps = 6; return "eat";
                default: fps = 6; return Environment.TickCount < blinkUntil ? "sitblink" : "sit";
            }
        }

        // ---------------------------------------------------------- memory-driven behaviour
        void Greet(bool isNew)
        {
            if (isNew)
            {
                pendingTitle = "Gâu! Mình là Lucky 🐶";
                pendingText = "Từ hôm nay mình sẽ ở trên taskbar với bạn. Click để xoa đầu, nhấp đúp để ném bóng nhé!";
                dirty = true; return;
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
            if (st == St.Focus) return;
            if (rnd.Next(4) == 0) Say(PetLines[rnd.Next(PetLines.Length)], 2500);
            if (st != St.Carry && st != St.Eat && st != St.GoEat && st != St.Fetch) SetState(St.Happy, 60);
        }

        void ShowMemories()
        {
            int aff = (int)Math.Round(mem.Affection);
            string bar = new string('♥', aff / 20) + new string('♡', 5 - aff / 20);
            string fed = mem.Hungry ? "đang đói 🥺" : "no bụng 😋";
            string text =
                "🐶  Lucky – chú Husky của bạn\n\n" +
                "📅  Gặp nhau từ: " + mem.FirstMet.ToString("dd/MM/yyyy") + "  (" + mem.DaysTogether + " ngày)\n" +
                "💞  Độ thân thiết: " + bar + "  " + aff + "%\n" +
                "🍖  Bụng: " + fed + "\n\n" +
                "🤚  Được xoa đầu: " + mem.Pets + " lần\n" +
                "🎾  Nhặt bóng: " + mem.Balls + " lần\n" +
                "🦴  Ăn xương: " + mem.Treats + " cái\n" +
                "🏃  Đuổi theo chuột: " + mem.Chases + " lần\n" +
                "🎯  Phiên tập trung: " + mem.FocusSessions + "\n" +
                "💤  Giấc ngủ: " + mem.Naps + " lần\n" +
                "💧  Nhắc uống nước: " + mem.Reminders + " lần\n\n" +
                "Trí nhớ được lưu ở:\n" + Memory.FilePath;
            MessageBox.Show(text, "Kỷ niệm với Lucky", MessageBoxButtons.OK, MessageBoxIcon.None);
        }

        void UpdateTooltip()
        {
            if (tray == null) return;
            string t;
            if (st == St.Focus) t = "Lucky 🎯 tập trung · còn " + Math.Max(0, (int)Math.Ceiling((focusEnd - DateTime.Now).TotalMinutes)) + " phút";
            else t = "Lucky ♥ " + (int)Math.Round(mem.Affection) + "% · " + mem.DaysTogether + " ngày" + (mem.Hungry ? " · đói bụng" : "");
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
            if (st == St.Focus && s != St.Focus && DateTime.Now < focusEnd) return; // stay calm while focusing
            if (s == St.Sleep && st != St.Sleep) { mem.Naps++; dirty = true; }
            if (s == St.Chase) { mem.Chases++; mem.Affection = Math.Min(100, mem.Affection + 0.5); dirty = true; }
            if (s != st) animStart = Environment.TickCount;
            st = s; stateTicks = ticks;
            if (s == St.Happy && y >= Ground() - 1) vy = -6 * S;
            timer.Interval = (s == St.Sleep || s == St.Sit || s == St.Focus || s == St.Eat) ? 80 : 40;
        }

        void NextRandomState()
        {
            if (st == St.Sleep) { SetState(St.Sit, 60); return; }
            if (ballActive && !ballCarried) { SetState(St.Fetch, 400); return; }
            if (boneActive) { SetState(St.GoEat, 400); return; }
            int r = rnd.Next(100);
            bool lonely = mem.Affection < 30;
            if (r < (lonely ? 40 : 55)) { dir = rnd.Next(2) == 0 ? -1 : 1; SetState(St.Walk, rnd.Next(70, 220)); }
            else if (r < (lonely ? 70 : 87))
            {
                SetState(St.Sit, rnd.Next(50, 130));
                if (mem.Hungry && rnd.Next(3) == 0) Say("Lucky đói bụng quá… cho Lucky cục xương nha", 4000);
            }
            else SetState(St.Sleep, rnd.Next(250, 700));
        }

        // ---------------------------------------------------------- toys
        double MouthX(double mouthLogical) { return x + (dir > 0 ? mouthLogical : CW - mouthLogical) * S; }
        float BallR { get { return 5.5f * S; } }

        void ThrowBall(bool fromCursor)
        {
            if (st == St.Focus) { Say("Đang tập trung mà, lát chơi nha ♥", 2500); return; }
            var wa = Screen.PrimaryScreen.WorkingArea;
            var c = Cursor.Position;
            if (fromCursor && c.Y < wa.Bottom - 10) { bx = c.X; by = Math.Min(c.Y, wa.Bottom - BallR); }
            else { bx = MouthX(115); by = y + 60 * S; }
            double speed = (6 + rnd.NextDouble() * 7) * S;
            bvx = fromCursor ? (rnd.Next(2) == 0 ? -speed : speed) : -dir * speed;
            bvy = -9 * S;
            ballActive = true; ballCarried = false; ballIdleSince = Environment.TickCount;
            ballWin.Show();
            SetState(St.Fetch, 600);
        }

        void KickBall()
        {
            if (!ballActive || ballCarried) return;
            bvx = (rnd.Next(2) == 0 ? -1 : 1) * (7 + rnd.NextDouble() * 6) * S; bvy = -10 * S;
            ballIdleSince = Environment.TickCount;
            if (st != St.Focus) SetState(St.Fetch, 600);
        }

        void StepBall()
        {
            if (!ballActive || ballCarried) return;
            var wa = Screen.PrimaryScreen.WorkingArea;
            double gy = wa.Bottom - BallR;
            bvy += 0.9 * S; bx += bvx; by += bvy;
            bRot += bvx / BallR;
            if (by >= gy) { by = gy; if (Math.Abs(bvy) > 2.5 * S) bvy = -bvy * 0.55; else bvy = 0; bvx *= 0.97; }
            if (bx < wa.Left + BallR) { bx = wa.Left + BallR; bvx = Math.Abs(bvx) * 0.7; }
            if (bx > wa.Right - BallR) { bx = wa.Right - BallR; bvx = -Math.Abs(bvx) * 0.7; }
            if (Math.Abs(bvx) < 0.05) bvx = 0;
            if (st != St.Fetch && Environment.TickCount - ballIdleSince > 45000) { ballActive = false; ballWin.Hide(); return; }
            int sz = (int)(BallR * 2 + 4);
            using (var bmp = new Bitmap(sz, sz, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp)) { g.SmoothingMode = SmoothingMode.AntiAlias; DrawBall(g, sz / 2f, sz / 2f, BallR, (float)bRot); }
                ballWin.Push(bmp, (int)(bx - sz / 2.0), (int)(by - sz / 2.0));
            }
        }

        void DropBone()
        {
            if (st == St.Focus) { Say("Tập trung xong rồi Lucky ăn sau nha", 2500); return; }
            var wa = Screen.PrimaryScreen.WorkingArea;
            boneX = MouthX(150); boneX = Math.Max(wa.Left + 30 * S, Math.Min(wa.Right - 30 * S, boneX));
            boneY = wa.Bottom - 160 * S; boneVy = 0; boneBites = 0; boneActive = true;
            boneWin.Show();
            SetState(St.GoEat, 600);
        }

        void StepBone()
        {
            if (!boneActive) return;
            var wa = Screen.PrimaryScreen.WorkingArea;
            double gy = wa.Bottom - 4 * S;
            if (boneY < gy) { boneVy += 0.9 * S; boneY = Math.Min(gy, boneY + boneVy); }
            int w = (int)(30 * S), h = (int)(16 * S);
            using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp)) { g.SmoothingMode = SmoothingMode.AntiAlias; DrawBone(g, w / 2f, h / 2f, S, boneBites); }
                boneWin.Push(bmp, (int)(boneX - w / 2.0), (int)(boneY - h + 2 * S));
            }
        }

        // ---------------------------------------------------------- focus / speech / clock
        void ToggleFocus()
        {
            if (st == St.Focus)
            {
                focusEnd = DateTime.Now; SetState(St.Sit, 60);
                focusItem.Text = "Tập trung 25 phút 🎯"; Say("Dừng tập trung rồi nè", 2000); UpdateTooltip(); return;
            }
            if (ballActive) { ballActive = false; ballCarried = false; ballWin.Hide(); }
            if (boneActive) { boneActive = false; boneWin.Hide(); }
            focusEnd = DateTime.Now.AddMinutes(FocusMinutes);
            SetState(St.Focus, int.MaxValue);
            focusItem.Text = "Dừng tập trung";
            Say("Bắt đầu 25 phút tập trung! Lucky sẽ nằm im ♥", 3500);
            UpdateTooltip();
        }

        void Say(string text, int ms)
        {
            bubbleText = text; bubbleUntil = Environment.TickCount + ms;
            if (!hiddenFs) bubbleWin.Show();
        }

        void StepBubble()
        {
            if (bubbleText == null) return;
            if (Environment.TickCount > bubbleUntil) { bubbleText = null; bubbleWin.Hide(); return; }
            using (var f = new Font("Segoe UI", 9f))
            {
                var flags = TextFormatFlags.WordBreak | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding;
                int maxW = (int)(150 * dpiScale);
                var ts = TextRenderer.MeasureText(bubbleText, f, new Size(maxW, 1000), flags);
                int pad = (int)(8 * dpiScale), tail = (int)(7 * dpiScale);
                int w = ts.Width + pad * 2, h = ts.Height + pad * 2;
                using (var opaque = new Bitmap(w, h + tail, PixelFormat.Format32bppArgb))
                using (var outBmp = new Bitmap(w, h + tail, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(opaque))
                    {
                        g.Clear(Color.FromArgb(250, 252, 255));
                        TextRenderer.DrawText(g, bubbleText, f, new Rectangle(pad, pad, ts.Width, ts.Height), Color.FromArgb(40, 52, 84), Color.FromArgb(250, 252, 255), flags);
                    }
                    using (var g = Graphics.FromImage(outBmp))
                    using (var path = Bubble(0.75f, 0.75f, w - 1.5f, h - 1.5f, 10 * dpiScale, tail))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.SetClip(path);
                        g.DrawImageUnscaled(opaque, 0, 0);
                        g.ResetClip();
                        using (var p = new Pen(Color.FromArgb(90, 200, 220), 1.5f * dpiScale)) g.DrawPath(p, path);
                    }
                    var wa = Screen.PrimaryScreen.WorkingArea;
                    int hx = (int)MouthX(80);
                    int bxp = Math.Max(wa.Left, Math.Min(wa.Right - w, hx - w / 2));
                    int byp = (int)(y + 8 * S) - (h + tail);
                    bubbleWin.Push(outBmp, bxp, byp);
                }
            }
        }

        static GraphicsPath Bubble(float x, float y, float w, float h, float r, float tail)
        {
            var p = new GraphicsPath();
            p.AddArc(x, y, r * 2, r * 2, 180, 90); p.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
            p.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
            p.AddLine(x + w / 2 + tail, y + h, x + w / 2, y + h + tail);
            p.AddLine(x + w / 2, y + h + tail, x + w / 2 - tail, y + h);
            p.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
            p.CloseFigure(); return p;
        }

        void ClockChecks()
        {
            var now = DateTime.Now; string today = now.ToString("yyyy-MM-dd");
            if (now.Hour >= 5 && now.Hour < 11 && mem.LastMorning != today)
            {
                mem.LastMorning = today; dirty = true;
                Say("Chào buổi sáng ☀ Chúc bạn một ngày thật may mắn!", 6000);
                SetState(St.Happy, 60);
            }
            bool late = now.Hour >= 23 || now.Hour < 4;
            string nightKey = (now.Hour < 4 ? now.AddDays(-1) : now).ToString("yyyy-MM-dd");
            if (late && mem.LastNight != nightKey && st != St.Focus)
            {
                mem.LastNight = nightKey; dirty = true;
                Say("Khuya rồi… đi ngủ sớm cho khoẻ nha ☾", 7000);
                SetState(St.Sleep, 1500);
            }
        }

        // ---------------------------------------------------------- input
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
            if (!moved && (Math.Abs(c.X - downPt.X) > 4 || Math.Abs(c.Y - downPt.Y) > 4))
            {
                moved = true;
                if (ballCarried) DropCarriedBall(false);
                if (st == St.Focus) { focusEnd = DateTime.Now; focusItem.Text = "Tập trung 25 phút 🎯"; }
                st = St.Drag; animStart = Environment.TickCount; timer.Interval = 40;
            }
            if (moved) { x = c.X - dragOff.X; y = c.Y - dragOff.Y; }
        }
        void OnUp(object s, MouseEventArgs e)
        {
            if (!dragging) return;
            dragging = false;
            if (moved) { mem.Drops++; dirty = true; st = St.Fall; vy = 0; animStart = Environment.TickCount; }
            else Pet();
        }

        void DropCarriedBall(bool proud)
        {
            ballCarried = false;
            bx = MouthX(117); by = y + 71 * S; bvx = dir * 2 * S; bvy = -2 * S;
            ballIdleSince = Environment.TickCount;
            ballWin.Show();
            if (proud)
            {
                mem.Balls++; mem.Affection = Math.Min(100, mem.Affection + 2); dirty = true;
                if (rnd.Next(2) == 0) Say("Nữa đi! Ném nữa đi! ♥", 2500);
                SetState(St.Remind, 50); // happy sitting wag
            }
        }

        // ---------------------------------------------------------- main loop
        void Tick(object sender, EventArgs e)
        {
            frame++;
            if (frame == 20 && pendingTitle != null) { tray.ShowBalloonTip(7000, pendingTitle, pendingText, ToolTipIcon.None); pendingTitle = null; }
            var now = DateTime.Now;
            if ((now - lastDecay).TotalMinutes >= 1) { lastDecay = now; mem.Affection = Math.Max(0, mem.Affection - 0.02); UpdateTooltip(); }
            if ((now - lastClock).TotalSeconds >= 30) { lastClock = now; ClockChecks(); }
            if ((dirty && (now - lastSave).TotalSeconds > 30) || (now - lastSave).TotalMinutes > 5) SaveMem();

            if (++fsCheck >= 25)
            {
                fsCheck = 0;
                bool fs = IsFullscreenApp();
                if (fs && !hiddenFs) { hiddenFs = true; Hide(); ballWin.Hide(); boneWin.Hide(); bubbleWin.Hide(); }
                else if (!fs && hiddenFs) { hiddenFs = false; Show(); if (ballActive && !ballCarried) ballWin.Show(); if (boneActive) boneWin.Show(); }
            }
            if (hiddenFs) return;

            if (st == St.Focus && now >= focusEnd)
            {
                mem.FocusSessions++; dirty = true;
                focusItem.Text = "Tập trung 25 phút 🎯";
                SetState(St.Happy, 60);
                tray.ShowBalloonTip(7000, "Xong 25 phút rồi! 🎉", "Bạn giỏi quá. Đứng dậy nghỉ 5 phút, uống nước nha!", ToolTipIcon.None);
                Say("Xong rồi! Nghỉ 5 phút nha ☕", 6000);
            }
            if (st == St.Focus && frame % 250 == 0) UpdateTooltip();

            if (remindOn && now >= nextRemind && st != St.Drag && st != St.Focus)
            {
                nextRemind = now.AddMinutes(RemindMinutes);
                mem.Reminders++; dirty = true;
                SetState(St.Remind, 200);
                Say("Uống một ngụm nước và vươn vai nha!", 7000);
                tray.ShowBalloonTip(7000, "Lucky nhắc nè 💧", "Uống một ngụm nước và đứng dậy vươn vai 1 phút nhé!", ToolTipIcon.None);
            }
            if (now >= nextChat)
            {
                nextChat = now.AddMinutes(rnd.Next(20, 36));
                if (st != St.Focus && st != St.Sleep) Say(Cheers[rnd.Next(Cheers.Length)], 6000);
            }

            var wa = Screen.PrimaryScreen.WorkingArea;
            double g = Ground();
            var cur = Cursor.Position;
            int chaseOdds = mem.Affection > 70 ? 70 : mem.Affection > 40 ? 120 : 260;
            switch (st)
            {
                case St.Walk:
                    x += dir * 1.7 * S;
                    if (Math.Abs(cur.X - (x + PW / 2.0)) < 230 * S && cur.Y > g - 120 * S && rnd.Next(chaseOdds) == 0) SetState(St.Chase, 200);
                    break;
                case St.Chase:
                    {
                        double d = (cur.X - PW / 2.0) - x;
                        dir = d > 0 ? 1 : -1;
                        if (Math.Abs(d) > 14 * S) x += dir * 3.6 * S; else SetState(St.Happy, 50);
                        break;
                    }
                case St.Fetch:
                    {
                        if (!ballActive || ballCarried) { SetState(St.Sit, 60); break; }
                        double d = bx - MouthX(115);
                        if (Math.Abs(d) > 7 * S) { dir = d > 0 ? 1 : -1; x += dir * 4 * S; stateTicks = 400; }
                        else if (by >= wa.Bottom - BallR - 2)
                        {
                            ballCarried = true; ballWin.Hide();
                            SetState(St.Carry, 600);
                        }
                        break;
                    }
                case St.Carry:
                    {
                        double target = Math.Max(wa.Left, Math.Min(wa.Right - PW, cur.X - PW / 2.0));
                        double d = target - x;
                        if (Math.Abs(d) > 24 * S) { dir = d > 0 ? 1 : -1; x += dir * 3.4 * S; stateTicks = 600; }
                        else { dir = cur.X > x + PW / 2.0 ? 1 : -1; DropCarriedBall(true); }
                        break;
                    }
                case St.GoEat:
                    {
                        if (!boneActive) { SetState(St.Sit, 60); break; }
                        double d = boneX - MouthX(116);
                        if (Math.Abs(d) > 5 * S) { dir = d > 0 ? 1 : -1; x += dir * 3.6 * S; stateTicks = 400; }
                        else if (boneY >= wa.Bottom - 4 * S - 1) { eatStart = Environment.TickCount; SetState(St.Eat, int.MaxValue); }
                        break;
                    }
                case St.Eat:
                    {
                        int t = Environment.TickCount - eatStart;
                        boneBites = Math.Min(3, t / 1300);
                        if (t > 4000)
                        {
                            boneActive = false; boneWin.Hide();
                            mem.Treats++; mem.LastFed = DateTime.Now; mem.Affection = Math.Min(100, mem.Affection + 5); dirty = true;
                            UpdateTooltip();
                            Say(rnd.Next(2) == 0 ? "Ngon quá! Cảm ơn bạn ♥" : "Măm măm… no rồi nè ♥", 3000);
                            SetState(St.Happy, 60);
                        }
                        break;
                    }
                case St.Fall:
                    vy += 0.9 * S; y += vy;
                    if (y >= g) { y = g; if (vy > 9 * S) vy = -vy * 0.35; else { vy = 0; SetState(St.Sit, 70); } }
                    break;
                case St.Happy:
                    if (y < g || vy < 0) { vy += 0.9 * S; y += vy; if (y >= g) { y = g; vy = 0; } }
                    break;
            }
            if (st != St.Drag && st != St.Fall && st != St.Happy) y = g;
            if (x < wa.Left) { x = wa.Left; dir = 1; }
            if (x > wa.Right - PW) { x = wa.Right - PW; dir = -1; }
            if (st == St.Sit && rnd.Next(70) == 0) blinkUntil = Environment.TickCount + 140;
            if (st != St.Drag && st != St.Focus && st != St.Eat && --stateTicks <= 0) NextRandomState();

            StepBall(); StepBone(); StepBubble();
            Render();
        }

        bool IsFullscreenApp()
        {
            IntPtr h = GetForegroundWindow();
            if (h == IntPtr.Zero || h == Handle || h == GetShellWindow()) return false;
            var sb = new StringBuilder(256); GetClassName(h, sb, 256);
            string c = sb.ToString();
            if (c == "WorkerW" || c == "Progman" || c == "Shell_TrayWnd") return false;
            RECT r; GetWindowRect(h, out r);
            var b = Screen.FromHandle(h).Bounds;
            return r.L <= b.Left && r.T <= b.Top && r.R >= b.Right && r.B >= b.Bottom;
        }

        // ---------------------------------------------------------- rendering
        void Render()
        {
            int fps; string anim = Anim(out fps);
            var frames = (dir > 0 ? sprR : sprL)[anim];
            int idx = (int)(((long)(Environment.TickCount - animStart) * fps / 1000) % frames.Length);
            if (idx < 0) idx = 0;
            using (var g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                if ((Ground() - y) / S < 3)
                {
                    float sw = anim == "sleep" ? 78 : anim.StartsWith("sit") ? 56 : 74;
                    float scx = anim.StartsWith("sit") ? 64 : 62;
                    if (dir < 0) scx = CW - scx;
                    using (var p = new GraphicsPath())
                    {
                        p.AddEllipse((scx - sw / 2) * S, 110.5f * S, sw * S, 8 * S);
                        using (var b = new PathGradientBrush(p) { CenterColor = Color.FromArgb(75, 8, 14, 34), SurroundColors = new[] { Color.FromArgb(0, 8, 14, 34) } })
                            g.FillPath(b, p);
                    }
                }
                g.DrawImageUnscaled(frames[idx], 0, 0);
                g.ScaleTransform(S, S);
                if (st == St.Carry) DrawBall(g, dir > 0 ? 117 : CW - 117, 71, 5.5f, 0);
                DrawOverlay(g);
            }
            Push(canvas, (int)x, (int)y);
        }

        float HeadX(float hx) { return dir > 0 ? hx : CW - hx; }

        void DrawOverlay(Graphics g)
        {
            if (st == St.Sleep || st == St.Focus)
            {
                float hx = HeadX(86);
                using (var f = new Font("Segoe UI", 8.5f, FontStyle.Bold))
                    for (int k = 0; k < 3; k++)
                    {
                        double ph = (frame * 0.012 + k / 3.0) % 1.0;
                        using (var b = new SolidBrush(Color.FromArgb((int)(255 * (1 - ph)), 214, 232, 255)))
                        {
                            var s0 = g.Save();
                            g.TranslateTransform(hx + (float)(ph * 16 * (dir > 0 ? 1 : -1)), 62 - (float)(ph * 40));
                            g.ScaleTransform(0.7f + (float)ph * 0.6f, 0.7f + (float)ph * 0.6f);
                            g.DrawString(st == St.Focus && k == 1 ? "✦" : "z", f, b, 0, 0);
                            g.Restore(s0);
                        }
                    }
            }
            if (st == St.Happy || st == St.Eat)
            {
                float hx = HeadX(st == St.Eat ? 100 : 92);
                int n = mem.Affection > 80 ? 5 : 3;
                for (int k = 0; k < n; k++)
                {
                    double ph = (frame * 0.03 + k / (double)n) % 1.0;
                    float hxk = hx - 20 + k * (40f / (n - 1)) + (float)Math.Sin(frame * 0.2 + k) * 2;
                    Heart(g, hxk, (st == St.Eat ? 62 : 24) - (float)ph * 22, 4.2f, (int)(255 * (1 - ph)));
                }
            }
        }

        static void Heart(Graphics g, float x, float y, float s, int a)
        {
            a = Math.Max(0, Math.Min(255, a));
            using (var b = new SolidBrush(Color.FromArgb(a, 255, 118, 164)))
            {
                g.FillEllipse(b, x - s, y - s / 2, s * 1.1f, s * 1.1f);
                g.FillEllipse(b, x - s * 0.1f, y - s / 2, s * 1.1f, s * 1.1f);
                g.FillPolygon(b, new[] { new PointF(x - s * 0.95f, y + s * 0.2f), new PointF(x + s * 0.95f, y + s * 0.2f), new PointF(x, y + s * 1.4f) });
            }
            using (var b = new SolidBrush(Color.FromArgb(a * 3 / 4, 255, 255, 255))) g.FillEllipse(b, x - s * 0.7f, y - s * 0.25f, s * 0.4f, s * 0.3f);
        }

        // Blue & white water-element ball with a rolling seam
        static void DrawBall(Graphics g, float cx, float cy, float r, float rot)
        {
            var rect = new RectangleF(cx - r, cy - r, r * 2, r * 2);
            using (var p = new GraphicsPath())
            {
                p.AddEllipse(rect);
                using (var b = new PathGradientBrush(p) { CenterColor = Color.FromArgb(120, 196, 255), SurroundColors = new[] { Color.FromArgb(22, 86, 196) }, CenterPoint = new PointF(cx - r * 0.35f, cy - r * 0.4f) })
                    g.FillPath(b, p);
                var old = g.Clip; g.SetClip(p, CombineMode.Intersect);
                var gs = g.Save();
                g.TranslateTransform(cx, cy); g.RotateTransform(rot * 57.3f);
                using (var pen = new Pen(Color.FromArgb(235, 255, 255, 255), r * 0.22f))
                {
                    g.DrawArc(pen, -r * 1.6f, -r * 0.9f, r * 1.6f, r * 1.8f, -60, 120);
                    g.DrawArc(pen, 0, -r * 0.9f, r * 1.6f, r * 1.8f, 120, 120);
                }
                g.Restore(gs);
                g.Clip = old;
                using (var pen = new Pen(Color.FromArgb(160, 20, 50, 110), Math.Max(1f, r * 0.08f))) g.DrawEllipse(pen, rect);
                using (var b = new SolidBrush(Color.FromArgb(200, 255, 255, 255))) g.FillEllipse(b, cx - r * 0.55f, cy - r * 0.6f, r * 0.42f, r * 0.3f);
            }
        }

        static void DrawBone(Graphics g, float cx, float cy, float s, int bites)
        {
            float len = 9 * s * (1 - bites * 0.22f);
            if (len < 2 * s) return;
            float k = 3.4f * s;
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(cx - len - k, cy - k * 1.35f, k * 1.5f, k * 1.5f);
                path.AddEllipse(cx - len - k, cy - k * 0.15f, k * 1.5f, k * 1.5f);
                if (bites < 2)
                {
                    path.AddEllipse(cx + len - k * 0.5f, cy - k * 1.35f, k * 1.5f, k * 1.5f);
                    path.AddEllipse(cx + len - k * 0.5f, cy - k * 0.15f, k * 1.5f, k * 1.5f);
                }
                path.AddRectangle(new RectangleF(cx - len, cy - k * 0.6f, len * 2, k * 1.2f));
                path.FillMode = FillMode.Winding;
                using (var b = new LinearGradientBrush(new RectangleF(cx - len - k, cy - k * 1.4f, len * 2 + k * 2, k * 2.8f), Color.FromArgb(255, 252, 244), Color.FromArgb(222, 206, 178), LinearGradientMode.Vertical))
                    g.FillPath(b, path);
                using (var p = new Pen(Color.FromArgb(170, 150, 120, 90), Math.Max(1f, 0.9f * s))) g.DrawPath(p, path);
            }
        }

        Icon MakeIcon()
        {
            using (var bmp = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    var sit = sprR["sit"][0];
                    float hx = 66 * S, hy = 44 * S, hr = 32 * S;   // crop the head from the sitting sprite
                    g.DrawImage(sit, new Rectangle(0, 0, 32, 32), hx - hr, hy - hr, hr * 2, hr * 2, GraphicsUnit.Pixel);
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
