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
        public int Level = 1, Xp, Coins;
        public string Owned = "", EqHead = "", EqNeck = "", EqFace = "";
        public bool Has(string id) { return ("," + Owned + ",").Contains("," + id + ","); }
        public void Add(string id) { if (!Has(id)) Owned = Owned.Length == 0 ? id : Owned + "," + id; }
        public int XpNeeded { get { return 60 + 30 * Level; } }

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
                        case "level": m.Level = Math.Max(1, int.Parse(v, CI)); break;
                        case "xp": m.Xp = int.Parse(v, CI); break;
                        case "coins": m.Coins = int.Parse(v, CI); break;
                        case "owned": m.Owned = v; break;
                        case "eq_head": m.EqHead = v; break;
                        case "eq_neck": m.EqNeck = v; break;
                        case "eq_face": m.EqFace = v; break;
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
            sb.AppendLine("level=" + Level.ToString(CI));
            sb.AppendLine("xp=" + Xp.ToString(CI));
            sb.AppendLine("coins=" + Coins.ToString(CI));
            sb.AppendLine("owned=" + Owned);
            sb.AppendLine("eq_head=" + EqHead);
            sb.AppendLine("eq_neck=" + EqNeck);
            sb.AppendLine("eq_face=" + EqFace);
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

        // idle-game layer
        class Floater { public string Text; public float X, Y; public int T0; public Color C; }
        List<Floater> floaters = new List<Floater>();
        DateTime lastIncome = DateTime.Now;
        int sparkleUntil;
        ShopForm shop;
        [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

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
            menu.Items.Add("Cửa hàng & cấp độ 🏆", null, delegate { OpenShop(); });
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
            tray.DoubleClick += delegate { OpenShop(); };
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
            if (mem.Level == 1 && mem.Coins == 0 && mem.Owned == "")
            {
                mem.Coins = 60; dirty = true;
                pendingTitle = "Quà chào mừng 🎁";
                pendingText = "Lucky tặng bạn 60 xu may mắn! Chuột phải vào Lucky → Cửa hàng & cấp độ để mua đồ cho Lucky nha.";
                return;
            }
            TimeSpan away = DateTime.Now - mem.LastSeen;
            if (away.TotalMinutes >= 10)
            {
                int earned = (int)(Math.Min(away.TotalMinutes, 480) * IncomeRate() * 0.5);
                if (earned > 0)
                {
                    mem.Coins += earned;
                    pendingTitle = "Chào mừng trở lại! 🪙";
                    pendingText = "Lúc bạn vắng nhà, Lucky đã nhặt được " + earned + " xu may mắn.";
                }
            }
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
            Reward(3, 0);
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
            else t = "Lucky Lv." + mem.Level + " · " + mem.Coins + " xu · ♥ " + (int)Math.Round(mem.Affection) + "%" + (mem.Hungry ? " · đói" : "");
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
                int mul = mem.Has("up_glowball") ? 2 : 1;
                Reward(12 * mul, 3 * mul);
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
            if ((now - lastIncome).TotalSeconds >= 60)
            {
                lastIncome = now;
                if (UserActive()) Reward(1, (int)Math.Round(IncomeRate()));
            }
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
                Reward(40, 15);
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
                Reward(3, 0);
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
                            Reward(8, 0);
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

            if (floaters.Count > 0 || Environment.TickCount < sparkleUntil) timer.Interval = 40;
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
                DrawAccessories(g, anim, idx);
                if (st == St.Carry) DrawBall(g, dir > 0 ? 117 : CW - 117, 71, 5.5f, 0);
                DrawOverlay(g);
            }
            Push(canvas, (int)x, (int)y);
        }

        float HeadX(float hx) { return dir > 0 ? hx : CW - hx; }

        // ---------------------------------------------------------- idle game
        public Memory Mem { get { return mem; } }

        public double IncomeRate()
        {
            double r = 1 + mem.Level * 0.2;
            if (mem.Has("up_bowl")) r += 1;
            if (mem.Has("up_bed")) r += 2;
            if (mem.Has("up_house")) r += 5;
            return r;
        }

        static bool UserActive()
        {
            var li = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
            if (!GetLastInputInfo(ref li)) return true;
            return unchecked((uint)Environment.TickCount - li.dwTime) < 5 * 60 * 1000;
        }

        void Reward(int xp, int coins)
        {
            mem.Xp += xp; mem.Coins += coins; dirty = true;
            float hx = HeadX(80);
            if (xp > 0) floaters.Add(new Floater { Text = "+" + xp + " XP", X = hx - 18, Y = 30, T0 = Environment.TickCount, C = Color.FromArgb(130, 240, 236) });
            if (coins > 0) floaters.Add(new Floater { Text = "+" + coins + " xu", X = hx + 2, Y = 40, T0 = Environment.TickCount + 250, C = Color.FromArgb(255, 226, 120) });
            while (mem.Xp >= mem.XpNeeded)
            {
                mem.Xp -= mem.XpNeeded; mem.Level++;
                int bonus = 20 * mem.Level; mem.Coins += bonus;
                sparkleUntil = Environment.TickCount + 2500;
                Say("Lên cấp " + mem.Level + "! Thưởng " + bonus + " xu ✦", 4500);
                tray.ShowBalloonTip(5000, "Lucky lên cấp " + mem.Level + "! ⭐", "Thưởng " + bonus + " xu may mắn. Mở Cửa hàng để mua đồ cho Lucky nha!", ToolTipIcon.None);
                if (st != St.Focus && st != St.Eat && st != St.Carry) SetState(St.Happy, 60);
            }
            UpdateTooltip();
            if (shop != null && !shop.IsDisposed && shop.Visible) shop.RefreshAll();
        }

        public void Changed() { dirty = true; UpdateTooltip(); Render(); }

        void OpenShop()
        {
            if (shop == null || shop.IsDisposed) shop = new ShopForm(this);
            shop.RefreshAll(); shop.Show(); shop.Activate();
        }

        // Head / neck anchors for each animation frame (mirrors the poses in art/render_sprites.py)
        static void Anchors(string anim, int idx, out float cx, out float cy, out float r, out float nx, out float ny)
        {
            float bob = 0;
            if (anim == "walk") bob = (float)Math.Abs(Math.Sin(idx / 8.0 * 2 * Math.PI)) * 1.4f;
            else if (anim == "run") bob = (float)Math.Abs(Math.Sin(idx / 6.0 * 2 * Math.PI)) * 2.5f;
            if (anim.StartsWith("sit")) { cx = 66; cy = 47; r = 24; nx = 66; ny = 71; }
            else if (anim == "sleep") { cx = 84; cy = 95; r = 20; nx = 80; ny = 92; }
            else if (anim == "eat") { cx = 95; cy = 90; r = 20; nx = 88; ny = 85; }
            else { cx = 92; cy = 56 - bob; r = 23; nx = 87; ny = 79 - bob; }
        }

        void DrawAccessories(Graphics g, string anim, int idx)
        {
            if (mem.EqHead == "" && mem.EqNeck == "" && mem.EqFace == "") return;
            float cx, cy, r, nx, ny;
            Anchors(anim, idx, out cx, out cy, out r, out nx, out ny);
            var gs = g.Save();
            if (dir < 0) { g.TranslateTransform(CW, 0); g.ScaleTransform(-1, 1); }
            if (mem.EqNeck != "") Accessory.Draw(g, mem.EqNeck, cx, cy, r, nx, ny);
            if (mem.EqFace != "" && anim != "sleep") Accessory.Draw(g, mem.EqFace, cx, cy, r, nx, ny);
            if (mem.EqHead != "") Accessory.Draw(g, mem.EqHead, cx, cy, r, nx, ny);
            g.Restore(gs);
        }

        void DrawOverlay(Graphics g)
        {
            int nowT = Environment.TickCount;
            if (floaters.Count > 0)
            {
                using (var f = new Font("Segoe UI", 6.5f, FontStyle.Bold))
                    for (int i = floaters.Count - 1; i >= 0; i--)
                    {
                        var fl = floaters[i];
                        int t = nowT - fl.T0;
                        if (t > 1600) { floaters.RemoveAt(i); continue; }
                        if (t < 0) continue;
                        float ph = t / 1600f;
                        int a = (int)(255 * (1 - ph * ph));
                        float yy = fl.Y - ph * 22;
                        using (var sh = new SolidBrush(Color.FromArgb(a * 3 / 4, 24, 30, 60)))
                            for (int k = 0; k < 4; k++) g.DrawString(fl.Text, f, sh, fl.X + (k % 2 == 0 ? -0.7f : 0.7f), yy + (k < 2 ? -0.7f : 0.7f));
                        using (var b = new SolidBrush(Color.FromArgb(a, fl.C))) g.DrawString(fl.Text, f, b, fl.X, yy);
                    }
            }
            if (nowT < sparkleUntil)
            {
                float ph = 1 - (sparkleUntil - nowT) / 2500f;
                for (int k = 0; k < 8; k++)
                {
                    double a = k * Math.PI / 4 + ph * 2;
                    float rr = 20 + ph * 30, sx = 65 + (float)Math.Cos(a) * rr, sy = 60 + (float)Math.Sin(a) * rr * 0.7f;
                    int al = (int)(255 * (1 - ph));
                    using (var p = new Pen(Color.FromArgb(al, 255, 236, 140), 1.6f))
                    {
                        g.DrawLine(p, sx - 3, sy, sx + 3, sy); g.DrawLine(p, sx, sy - 3, sx, sy + 3);
                    }
                }
            }
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

    // ================================================================ accessories (idle-game cartoon style: bold outlines, flat 2-tone)
    static class Accessory
    {
        public class Item { public string Id, Name, Slot, Desc; public int Price, MinLevel; }
        public static readonly Item[] Catalog = {
            new Item { Id = "neck_aqua",   Slot = "neck", Name = "Khăn cổ aqua",      Price = 30,  MinLevel = 1,  Desc = "Màu nước, hợp mệnh Thủy" },
            new Item { Id = "neck_navy",   Slot = "neck", Name = "Khăn navy chấm bi", Price = 30,  MinLevel = 1,  Desc = "Lịch lãm như dân văn phòng" },
            new Item { Id = "head_bow",    Slot = "head", Name = "Nơ hồng",           Price = 40,  MinLevel = 1,  Desc = "Dễ thương xỉu" },
            new Item { Id = "neck_silver", Slot = "neck", Name = "Khăn bạc",          Price = 60,  MinLevel = 2,  Desc = "Kim sinh Thủy, thêm may mắn" },
            new Item { Id = "head_beanie", Slot = "head", Name = "Nón len xanh",      Price = 80,  MinLevel = 2,  Desc = "Ấm áp mùa đông" },
            new Item { Id = "head_party",  Slot = "head", Name = "Nón sinh nhật",     Price = 100, MinLevel = 3,  Desc = "Ngày nào cũng là ngày vui" },
            new Item { Id = "face_shades", Slot = "face", Name = "Kính râm",          Price = 120, MinLevel = 3,  Desc = "Ngầu hết nấc" },
            new Item { Id = "head_crown",  Slot = "head", Name = "Vương miện bạc",    Price = 500, MinLevel = 10, Desc = "Dành cho Lucky đẳng cấp" },
            new Item { Id = "up_bowl",     Slot = "up",   Name = "Bát ăn bằng bạc",   Price = 50,  MinLevel = 1,  Desc = "+1 xu mỗi phút" },
            new Item { Id = "up_bed",      Slot = "up",   Name = "Đệm ngủ êm",        Price = 150, MinLevel = 2,  Desc = "+2 xu mỗi phút" },
            new Item { Id = "up_glowball", Slot = "up",   Name = "Bóng phát sáng",    Price = 220, MinLevel = 4,  Desc = "Nhặt bóng được thưởng x2" },
            new Item { Id = "up_house",    Slot = "up",   Name = "Nhà gỗ nhỏ",        Price = 600, MinLevel = 6,  Desc = "+5 xu mỗi phút" },
        };

        static readonly Color Ink = Color.FromArgb(34, 38, 66);

        // Flat cel shading: base fill, lower shadow band, white highlight, bold ink outline
        static void Toon(Graphics g, GraphicsPath path, Color baseC, Color shadeC, float lw)
        {
            using (var b = new SolidBrush(baseC)) g.FillPath(b, path);
            var bb = path.GetBounds();
            var old = g.Clip;
            g.SetClip(path, CombineMode.Intersect);
            using (var b = new SolidBrush(shadeC)) g.FillEllipse(b, bb.X + bb.Width * 0.15f, bb.Y + bb.Height * 0.55f, bb.Width * 1.2f, bb.Height);
            using (var b = new SolidBrush(Color.FromArgb(200, 255, 255, 255))) g.FillEllipse(b, bb.X + bb.Width * 0.15f, bb.Y + bb.Height * 0.12f, bb.Width * 0.28f, bb.Height * 0.2f);
            g.Clip = old;
            using (var p = new Pen(Ink, lw) { LineJoin = LineJoin.Round }) g.DrawPath(p, path);
        }

        public static void Draw(Graphics g, string id, float cx, float cy, float r, float nx, float ny)
        {
            switch (id)
            {
                case "neck_aqua": Scarf(g, nx, ny, Color.FromArgb(64, 214, 226), Color.FromArgb(24, 160, 196), false); break;
                case "neck_navy": Scarf(g, nx, ny, Color.FromArgb(64, 98, 186), Color.FromArgb(40, 66, 140), true); break;
                case "neck_silver": Scarf(g, nx, ny, Color.FromArgb(240, 244, 252), Color.FromArgb(188, 200, 224), false); break;
                case "head_bow": Bow(g, cx + r * 0.52f, cy - r * 0.72f, r * 0.3f); break;
                case "head_beanie": Beanie(g, cx, cy, r); break;
                case "head_party": Party(g, cx, cy, r); break;
                case "head_crown": Crown(g, cx, cy, r); break;
                case "face_shades": Shades(g, cx, cy, r); break;
            }
        }

        static void Scarf(Graphics g, float nx, float ny, Color a, Color b, bool dots)
        {
            using (var path = new GraphicsPath())
            {
                path.AddClosedCurve(new[] { new PointF(nx - 10, ny - 1.5f), new PointF(nx + 12, ny + 0.5f), new PointF(nx + 2.5f, ny + 13) }, 0.18f);
                Toon(g, path, a, b, 1.1f);
                if (dots) using (var w = new SolidBrush(Color.White))
                    foreach (var d in new[] { new PointF(nx - 4, ny + 2), new PointF(nx + 4, ny + 3), new PointF(nx + 1, ny + 7), new PointF(nx + 8, ny + 1) })
                        g.FillEllipse(w, d.X - 1f, d.Y - 1f, 2f, 2f);
            }
            using (var k = new GraphicsPath()) { k.AddEllipse(nx + 8.5f, ny - 3, 5, 4.5f); Toon(g, k, a, b, 1f); }
        }

        static void Bow(Graphics g, float x, float y, float s)
        {
            Color a = Color.FromArgb(255, 150, 196), b = Color.FromArgb(232, 96, 150);
            foreach (int side in new[] { -1, 1 })
                using (var path = new GraphicsPath())
                {
                    path.AddClosedCurve(new[] { new PointF(x, y), new PointF(x + side * s * 1.6f, y - s * 0.95f), new PointF(x + side * s * 1.7f, y + s * 0.95f) }, 0.35f);
                    Toon(g, path, a, b, 1f);
                }
            using (var c = new GraphicsPath()) { c.AddEllipse(x - s * 0.48f, y - s * 0.48f, s * 0.96f, s * 0.96f); Toon(g, c, a, b, 1f); }
        }

        static void Beanie(Graphics g, float cx, float cy, float r)
        {
            var dome = new RectangleF(cx - r * 0.66f, cy - r * 1.22f, r * 1.32f, r * 1.0f);
            using (var path = new GraphicsPath())
            {
                path.AddArc(dome, 180, 180); path.CloseFigure();
                Toon(g, path, Color.FromArgb(70, 150, 240), Color.FromArgb(40, 106, 200), 1.1f);
                using (var p = new Pen(Color.FromArgb(90, 255, 255, 255), 0.9f))
                    for (int k = -2; k <= 2; k++) g.DrawLine(p, cx + k * r * 0.2f, cy - r * 0.76f, cx + k * r * 0.14f, cy - r * 1.1f);
            }
            using (var band = new GraphicsPath())
            {
                band.AddRectangle(new RectangleF(cx - r * 0.72f, cy - r * 0.8f, r * 1.44f, r * 0.26f));
                Toon(g, band, Color.FromArgb(246, 248, 255), Color.FromArgb(200, 212, 236), 1.1f);
            }
            using (var pom = new GraphicsPath()) { pom.AddEllipse(cx - r * 0.21f, cy - r * 1.4f, r * 0.42f, r * 0.42f); Toon(g, pom, Color.White, Color.FromArgb(206, 218, 240), 1f); }
        }

        static void Party(Graphics g, float cx, float cy, float r)
        {
            using (var path = new GraphicsPath())
            {
                path.AddPolygon(new[] { new PointF(cx - r * 0.36f, cy - r * 0.8f), new PointF(cx + r * 0.34f, cy - r * 0.84f), new PointF(cx + r * 0.12f, cy - r * 1.75f) });
                using (var b = new SolidBrush(Color.FromArgb(70, 214, 230))) g.FillPath(b, path);
                var old = g.Clip; g.SetClip(path, CombineMode.Intersect);
                using (var p = new Pen(Color.White, r * 0.1f))
                    for (int k = 0; k < 4; k++) g.DrawLine(p, cx - r, cy - r * (0.9f + k * 0.24f) + r * 0.3f, cx + r, cy - r * (0.9f + k * 0.24f) - r * 0.2f);
                g.Clip = old;
                using (var p = new Pen(Ink, 1.1f) { LineJoin = LineJoin.Round }) g.DrawPath(p, path);
            }
            using (var pom = new GraphicsPath()) { pom.AddEllipse(cx + r * 0.12f - r * 0.17f, cy - r * 1.92f, r * 0.34f, r * 0.34f); Toon(g, pom, Color.FromArgb(255, 150, 196), Color.FromArgb(232, 96, 150), 1f); }
        }

        static void Crown(Graphics g, float cx, float cy, float r)
        {
            float w = r * 0.95f, b0 = cy - r * 0.78f, h = r * 0.55f;
            var pts = new[] {
                new PointF(cx - w / 2, b0), new PointF(cx - w / 2, b0 - h * 0.6f), new PointF(cx - w * 0.25f, b0 - h * 0.25f),
                new PointF(cx, b0 - h), new PointF(cx + w * 0.25f, b0 - h * 0.25f), new PointF(cx + w / 2, b0 - h * 0.6f), new PointF(cx + w / 2, b0) };
            using (var path = new GraphicsPath()) { path.AddPolygon(pts); Toon(g, path, Color.FromArgb(236, 242, 252), Color.FromArgb(182, 196, 222), 1.1f); }
            foreach (var gx in new[] { -w * 0.3f, 0f, w * 0.3f })
                using (var gem = new GraphicsPath()) { gem.AddEllipse(cx + gx - r * 0.08f, b0 - h * 0.34f, r * 0.16f, r * 0.16f); Toon(g, gem, Color.FromArgb(80, 170, 255), Color.FromArgb(40, 110, 220), 0.8f); }
        }

        static void Shades(Graphics g, float cx, float cy, float r)
        {
            var L = new RectangleF(cx - r * 0.62f, cy - r * 0.27f, r * 0.5f, r * 0.38f);
            var R = new RectangleF(cx + r * 0.12f, cy - r * 0.29f, r * 0.48f, r * 0.37f);
            using (var p = new Pen(Ink, 1.3f))
            {
                g.DrawLine(p, L.Right, L.Y + L.Height * 0.3f, R.X, R.Y + R.Height * 0.3f);
                g.DrawLine(p, L.X, L.Y + L.Height * 0.3f, cx - r * 0.95f, cy - r * 0.32f);
            }
            foreach (var rr in new[] { L, R })
                using (var path = new GraphicsPath())
                {
                    float k = rr.Height * 0.45f;
                    path.AddArc(rr.X, rr.Y, k, k, 180, 90); path.AddArc(rr.Right - k, rr.Y, k, k, 270, 90);
                    path.AddArc(rr.Right - k, rr.Bottom - k, k, k, 0, 90); path.AddArc(rr.X, rr.Bottom - k, k, k, 90, 90);
                    path.CloseFigure();
                    Toon(g, path, Color.FromArgb(46, 56, 96), Color.FromArgb(24, 28, 52), 1f);
                }
        }

        public static Bitmap Preview(Item it, int size)
        {
            var b = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.FromArgb(228, 240, 255));
                float s = size / 40f;
                g.ScaleTransform(s, s);
                if (it.Slot == "up") UpgradeIcon(g, it.Id);
                else if (it.Slot == "neck") Draw(g, it.Id, 20, 8, 20, 17, 13);
                else Draw(g, it.Id, 20, it.Slot == "face" ? 22 : 37, 20, 0, 0);
            }
            return b;
        }

        static void UpgradeIcon(Graphics g, string id)
        {
            using (var p = new GraphicsPath())
            {
                switch (id)
                {
                    case "up_bowl":
                        p.AddArc(8, 10, 24, 22, 0, 180); p.CloseFigure();
                        Toon(g, p, Color.FromArgb(236, 242, 252), Color.FromArgb(182, 196, 222), 1.4f);
                        using (var k = new GraphicsPath()) { k.AddEllipse(10, 16, 20, 6); Toon(g, k, Color.FromArgb(214, 160, 100), Color.FromArgb(180, 120, 70), 1f); }
                        break;
                    case "up_bed":
                        p.AddEllipse(5, 16, 30, 16);
                        Toon(g, p, Color.FromArgb(110, 170, 250), Color.FromArgb(70, 120, 210), 1.4f);
                        using (var k = new GraphicsPath()) { k.AddEllipse(10, 18, 20, 9); Toon(g, k, Color.FromArgb(250, 252, 255), Color.FromArgb(210, 220, 240), 1f); }
                        break;
                    case "up_glowball":
                        using (var gl = new SolidBrush(Color.FromArgb(90, 160, 240, 255))) g.FillEllipse(gl, 5, 5, 30, 30);
                        p.AddEllipse(10, 10, 20, 20);
                        Toon(g, p, Color.FromArgb(80, 170, 255), Color.FromArgb(40, 110, 220), 1.4f);
                        break;
                    default:
                        p.AddPolygon(new[] { new PointF(6, 20), new PointF(20, 7), new PointF(34, 20) });
                        Toon(g, p, Color.FromArgb(80, 120, 200), Color.FromArgb(50, 84, 160), 1.4f);
                        using (var w = new GraphicsPath()) { w.AddRectangle(new RectangleF(10, 20, 20, 14)); Toon(g, w, Color.FromArgb(214, 164, 110), Color.FromArgb(176, 124, 76), 1.4f); }
                        using (var d = new SolidBrush(Ink)) g.FillEllipse(d, 16, 24, 8, 10);
                        break;
                }
            }
        }
    }

    // ================================================================ shop / progress window
    class ShopForm : Form
    {
        readonly PetForm pet;
        readonly Label header = new Label(), stats = new Label();
        readonly ProgressBar bar = new ProgressBar();
        readonly FlowLayoutPanel list = new FlowLayoutPanel();
        static readonly Color Bg = Color.FromArgb(236, 244, 255), Card = Color.White, Accent = Color.FromArgb(28, 150, 196), InkC = Color.FromArgb(34, 46, 80);
        float scale = -1;

        public ShopForm(PetForm p)
        {
            pet = p;
            Text = "Lucky · Cửa hàng & cấp độ";
            Font = new Font("Segoe UI", 9.5f);
            BackColor = Bg; ForeColor = InkC;
            FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowIcon = false;
            ClientSize = new Size(Dp(440), Dp(570));
            header.Font = new Font("Segoe UI Black", 15f); header.AutoSize = true; header.Location = new Point(Dp(14), Dp(10));
            bar.Location = new Point(Dp(18), Dp(50)); bar.Size = new Size(Dp(404), Dp(14));
            stats.AutoSize = true; stats.Location = new Point(Dp(16), Dp(70)); stats.ForeColor = Color.FromArgb(70, 84, 120);
            list.Location = new Point(Dp(10), Dp(122)); list.Size = new Size(Dp(420), Dp(440));
            list.AutoScroll = true; list.FlowDirection = FlowDirection.TopDown; list.WrapContents = false;
            Controls.Add(header); Controls.Add(bar); Controls.Add(stats); Controls.Add(list);
            FormClosing += delegate(object s, FormClosingEventArgs e) { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        }

        int Dp(int v)
        {
            if (scale < 0) using (var g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
            return (int)(v * scale);
        }

        public void RefreshAll()
        {
            var m = pet.Mem;
            header.Text = "⭐ Lucky  Lv." + m.Level;
            bar.Maximum = m.XpNeeded; bar.Value = Math.Min(m.Xp, m.XpNeeded);
            stats.Text = "XP " + m.Xp + " / " + m.XpNeeded + "      🪙 " + m.Coins + " xu      ⏱ " + pet.IncomeRate().ToString("0.#") + " xu/phút\n" +
                         "Xoa đầu, ném bóng, cho ăn, tập trung 25 phút để Lucky lên cấp nhanh!";
            list.SuspendLayout();
            var old = new List<Control>(); foreach (Control c in list.Controls) old.Add(c);
            list.Controls.Clear(); foreach (var c in old) c.Dispose();
            AddSection("🎀 Phụ kiện");
            foreach (var it in Accessory.Catalog) if (it.Slot != "up") AddRow(it);
            AddSection("🏠 Nâng cấp – tăng thu nhập");
            foreach (var it in Accessory.Catalog) if (it.Slot == "up") AddRow(it);
            list.ResumeLayout();
        }

        void AddSection(string t)
        {
            list.Controls.Add(new Label { Text = t, AutoSize = true, Font = new Font("Segoe UI Semibold", 10.5f), Margin = new Padding(Dp(6), Dp(10), 0, Dp(2)) });
        }

        void AddRow(Accessory.Item it)
        {
            var m = pet.Mem;
            var row = new Panel { Size = new Size(Dp(392), Dp(54)), BackColor = Card, Margin = new Padding(Dp(4), Dp(3), 0, Dp(3)) };
            var pic = new PictureBox { Image = Accessory.Preview(it, Dp(42)), Size = new Size(Dp(42), Dp(42)), Location = new Point(Dp(6), Dp(6)) };
            var name = new Label { Text = it.Name, Font = new Font("Segoe UI Semibold", 9.5f), AutoSize = true, Location = new Point(Dp(56), Dp(8)) };
            var desc = new Label { Text = it.Desc, AutoSize = true, ForeColor = Color.FromArgb(110, 122, 150), Location = new Point(Dp(56), Dp(28)) };
            var btn = new Button { Size = new Size(Dp(118), Dp(32)), Location = new Point(Dp(266), Dp(11)), FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI Semibold", 9f) };
            btn.FlatAppearance.BorderSize = 0;
            bool owned = m.Has(it.Id);
            string eq = it.Slot == "head" ? m.EqHead : it.Slot == "neck" ? m.EqNeck : it.Slot == "face" ? m.EqFace : null;
            if (!owned && m.Level < it.MinLevel) { btn.Text = "🔒 Cần Lv." + it.MinLevel; btn.Enabled = false; btn.BackColor = Color.FromArgb(226, 230, 240); }
            else if (!owned) { btn.Text = "Mua · " + it.Price + " xu"; bool ok = m.Coins >= it.Price; btn.BackColor = ok ? Accent : Color.FromArgb(200, 210, 226); btn.ForeColor = Color.White; btn.Enabled = ok; }
            else if (it.Slot == "up") { btn.Text = "✔ Đã có"; btn.Enabled = false; btn.BackColor = Color.FromArgb(214, 244, 236); }
            else if (eq == it.Id) { btn.Text = "Tháo ra"; btn.BackColor = Color.FromArgb(255, 226, 236); }
            else { btn.Text = "Đeo vào"; btn.BackColor = Color.FromArgb(214, 236, 248); }
            btn.Click += delegate
            {
                if (!m.Has(it.Id)) { if (m.Coins < it.Price) return; m.Coins -= it.Price; m.Add(it.Id); if (it.Slot != "up") Equip(it, true); }
                else Equip(it, (it.Slot == "head" ? m.EqHead : it.Slot == "neck" ? m.EqNeck : m.EqFace) != it.Id);
                pet.Changed(); RefreshAll();
            };
            row.Controls.Add(pic); row.Controls.Add(name); row.Controls.Add(desc); row.Controls.Add(btn);
            list.Controls.Add(row);
        }

        void Equip(Accessory.Item it, bool on)
        {
            var m = pet.Mem; string v = on ? it.Id : "";
            if (it.Slot == "head") m.EqHead = v; else if (it.Slot == "neck") m.EqNeck = v; else if (it.Slot == "face") m.EqFace = v;
        }
    }

}
