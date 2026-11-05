// Feature core: settings, daily stats, priority state coordinator, speech bubble with buttons,
// smooth transitions, global hotkeys, tray menu additions, settings window and first-run intro.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace LuckyPet
{
    // ================================================================ settings (all features toggle independently)
    class Settings
    {
        public bool F1 = true, F3 = true, F4 = true, F5 = true, F6 = true, F7 = true;
        public bool Dnd, Sound = true, Interact, LockPet, Dodge = true, SelfPlay = true, WorkOutfit;
        public int EyeMin = 20, WaterMin = 60, StretchMin = 50;
        public int FocusMin = 25, ShortBreakMin = 5, LongBreakMin = 15, LongEvery = 4;
        public string Distractions = "youtube,facebook,tiktok,netflix,instagram,reddit";
        public string HotkeyPomodoro = "Ctrl+Alt+P", HotkeyInteract = "Ctrl+Alt+I";
        public int BatteryLow = 20, BatteryCritical = 10, OverloadPct = 90, OverloadSec = 60, AlertCooldownMin = 10;
        public int FastKpm = 200, FastSec = 10, AutoOffMin = 5;
        public string SummaryTime = "17:30";
        public Dictionary<string, string> Groups = DefaultGroups();

        public static Dictionary<string, string> DefaultGroups()
        {
            return new Dictionary<string, string> {
                { "code", "code,devenv,idea64,pycharm64,webstorm64,rider64,windowsterminal,cmd,powershell,pwsh,sublime_text,notepad++,cursor,zed,kiro|visual studio code" },
                { "docs", "winword,wps,notion,obsidian,onenote|google docs,notion" },
                { "sheet", "excel,et|google sheets" },
                { "email", "outlook,olk,thunderbird|gmail,outlook" },
                { "meeting", "teams,ms-teams,zoom|google meet,meet -,zoom meeting" },
                { "design", "figma,photoshop,illustrator|figma,canva" },
                { "fun", "steam,cs2,valorant,leagueclient,robloxplayerbeta|youtube,netflix" },
            };
        }

        public static string FilePath { get { return Path.Combine(Memory.Folder, "settings.txt"); } }
        public static bool Exists { get { return File.Exists(FilePath); } }

        public static Settings Load()
        {
            var s = new Settings();
            if (!File.Exists(FilePath)) return s;
            try
            {
                foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i <= 0 || line.StartsWith("#")) continue;
                    string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                    if (k.StartsWith("group.")) { s.Groups[k.Substring(6)] = v; continue; }
                    foreach (var f in typeof(Settings).GetFields())
                    {
                        if (f.IsStatic || f.Name != k) continue;
                        try
                        {
                            if (f.FieldType == typeof(bool)) f.SetValue(s, v == "true");
                            else if (f.FieldType == typeof(int)) f.SetValue(s, int.Parse(v, CultureInfo.InvariantCulture));
                            else if (f.FieldType == typeof(string)) f.SetValue(s, v);
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Memory.Folder);
                var sb = new StringBuilder("# Lucky settings\n");
                foreach (var f in typeof(Settings).GetFields())
                {
                    if (f.IsStatic || f.FieldType == typeof(Dictionary<string, string>)) continue;
                    object v = f.GetValue(this);
                    sb.Append(f.Name).Append('=').Append(v is bool ? ((bool)v ? "true" : "false") : Convert.ToString(v, CultureInfo.InvariantCulture)).Append('\n');
                }
                foreach (var kv in Groups) sb.Append("group.").Append(kv.Key).Append('=').Append(kv.Value).Append('\n');
                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }
    }

    // ================================================================ aggregate daily stats (counts and durations only)
    class Stats
    {
        public class Day { public int Done, Skipped, Pomo; public Dictionary<string, int> Sec = new Dictionary<string, int>(); }
        public Dictionary<string, Day> Days = new Dictionary<string, Day>();
        public string LastSummary = "";
        public static string FilePath { get { return Path.Combine(Memory.Folder, "stats.txt"); } }
        static string Key(DateTime d) { return d.ToString("yyyy-MM-dd"); }

        public Day Today
        {
            get
            {
                string k = Key(DateTime.Now); Day d;
                if (!Days.TryGetValue(k, out d)) { d = new Day(); Days[k] = d; }
                return d;
            }
        }
        public int WeekPomo
        {
            get
            {
                int s = 0; Day d;
                for (int i = 0; i < 7; i++) if (Days.TryGetValue(Key(DateTime.Now.AddDays(-i)), out d)) s += d.Pomo;
                return s;
            }
        }

        public static Stats Load()
        {
            var st = new Stats();
            if (!File.Exists(FilePath)) return st;
            try
            {
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    if (line.StartsWith("#summary=")) { st.LastSummary = line.Substring(9); continue; }
                    var p = line.Split('|');
                    if (p.Length < 5) continue;
                    var d = new Day { Done = int.Parse(p[1]), Skipped = int.Parse(p[2]), Pomo = int.Parse(p[3]) };
                    foreach (var kv in p[4].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var q = kv.Split('=');
                        if (q.Length == 2) d.Sec[q[0]] = int.Parse(q[1]);
                    }
                    st.Days[p[0]] = d;
                }
            }
            catch { }
            return st;
        }

        public void Save()
        {
            try
            {
                var keys = new List<string>(Days.Keys); keys.Sort();
                if (keys.Count > 60) keys = keys.GetRange(keys.Count - 60, 60);
                var sb = new StringBuilder("#summary=" + LastSummary + "\n");
                foreach (var k in keys)
                {
                    var d = Days[k];
                    var parts = new List<string>();
                    foreach (var kv in d.Sec) parts.Add(kv.Key + "=" + kv.Value);
                    sb.Append(k).Append('|').Append(d.Done).Append('|').Append(d.Skipped).Append('|').Append(d.Pomo).Append('|').Append(string.Join(";", parts.ToArray())).Append('\n');
                }
                File.WriteAllText(FilePath, sb.ToString());
            }
            catch { }
        }
    }

    class ModeReq
    {
        public string Key, Anim;
        public int Prio, Start, Ms;
        public bool Once, Started, Arrived;
        public double TargetX = double.NaN;
        public Action OnDone;
    }

    partial class PetForm
    {
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);

        // priorities (lower = more important); idle wandering is the fallback
        const int P_ALERT = 20, P_BREAK = 30, P_FOCUS_EVT = 39, P_FOCUS = 40, P_REACT = 45, P_CTX_EVT = 49, P_CONTEXT = 50, P_ACT_EVT = 59, P_ACTIVITY = 60, P_TALK = 70;

        Settings cfg;
        Stats stats;
        readonly Dictionary<string, ModeReq> modes = new Dictionary<string, ModeReq>();
        readonly Dictionary<string, int[]> animInfo = new Dictionary<string, int[]>();   // name -> {fps, loop}
        ModeReq curMode;
        string lastTopKey;
        bool curOnce, movingToTarget, userHidden, lowPower;
        int hideUntil;
        ToolStripMenuItem interactItem, dndItem, pomoStart, pomoPause, pomoStop;

        // ---------------------------------------------------------- lifecycle
        void InitFeatures()
        {
            bool first = !Settings.Exists;
            cfg = Settings.Load();
            stats = Stats.Load();
            LoadAnimInfo();
            if (first) { cfg.Save(); introPending = true; }
            F7Init();
        }

        bool introPending;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (cfg == null) return;
            RegisterHotkeys(false);
            F6InitHandle();
            F5Install();
        }

        void FeaturesShutdown()
        {
            try { UnregisterHotKey(Handle, 1); UnregisterHotKey(Handle, 2); } catch { }
            F5Uninstall();
            F6Shutdown();
            stats.Save(); cfg.Save();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312)
            {
                int id = m.WParam.ToInt32();
                if (id == 1 && cfg.F7) SetInteract(!cfg.Interact, false);
                else if (id == 2 && cfg.F3) PomoHotkey();
            }
            else if (m.Msg == 0x031D) OnClipboardChanged();
            base.WndProc(ref m);
        }

        void FeaturesTick()
        {
            int now = Environment.TickCount;
            if (introPending && frame > 25) { introPending = false; BeginInvoke(new Action(ShowIntro)); }
            if (userHidden && hideUntil != 0 && now - hideUntil >= 0) ShowPetAgain();
            F4Tick();
            if (cfg.F7) F7Tick(); else F7Off();
            if (cfg.Dnd) { ClearModesFrom(P_BREAK); return; }
            F1Tick();
            F3Tick();
            F5Tick();
            F6Tick();
        }

        bool Quiet() { return cfg != null && (cfg.Dnd || PomoFocusing() || InMeeting() || userHidden); }

        // ---------------------------------------------------------- sprites metadata
        IEnumerable<string> SpriteNames()
        {
            foreach (var n in Assembly.GetExecutingAssembly().GetManifestResourceNames())
                if (n.EndsWith(".png")) yield return n.Substring(0, n.Length - 4);
        }

        void LoadAnimInfo()
        {
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("anims.txt"))
                using (var r = new StreamReader(s))
                {
                    string line;
                    while ((line = r.ReadLine()) != null)
                    {
                        var p = line.Split(',');
                        if (p.Length == 4) animInfo[p[0]] = new[] { int.Parse(p[2]), int.Parse(p[3]), int.Parse(p[1]) };
                    }
                }
            }
            catch { }
        }

        bool HasAnim(string a) { return sprR.ContainsKey(a); }
        int Fps(string a) { int[] v; return animInfo.TryGetValue(a, out v) ? Math.Max(1, v[0]) : 8; }

        // ---------------------------------------------------------- priority coordinator
        void Request(string key, int prio, string anim, bool once = false, double targetX = double.NaN, Action onDone = null, int ms = 0)
        {
            if (!HasAnim(anim)) return;
            modes[key] = new ModeReq { Key = key, Prio = prio, Anim = anim, Once = once, TargetX = targetX, OnDone = onDone, Ms = ms };
        }

        void SetLoop(string key, int prio, string anim, double targetX = double.NaN)
        {
            ModeReq m;
            if (modes.TryGetValue(key, out m) && m.Anim == anim && !m.Once) return;
            Request(key, prio, anim, false, targetX);
        }

        void ClearMode(string key) { modes.Remove(key); }
        bool HasMode(string key) { return modes.ContainsKey(key); }
        void ClearModesFrom(int prio)
        {
            foreach (var k in new List<string>(modes.Keys)) if (modes[k].Prio >= prio) modes.Remove(k);
        }
        bool AlertActive() { foreach (var m in modes.Values) if (m.Prio <= P_BREAK) return true; return false; }

        ModeReq TopMode()
        {
            ModeReq best = null;
            foreach (var v in modes.Values) if (best == null || v.Prio < best.Prio) best = v;
            return best;
        }

        int OnceMs(ModeReq m)
        {
            if (m.Ms > 0) return m.Ms;
            int[] inf; int n = animInfo.TryGetValue(m.Anim, out inf) ? inf[2] : sprR[m.Anim].Length;
            return (int)(n * 1000L / Fps(m.Anim)) + 100;
        }

        // Returns true while a feature state drives Lucky (idle wandering is paused)
        bool ModeTick()
        {
            if (cfg == null || st == St.Drag || st == St.Fall) { curMode = null; return false; }
            int now = Environment.TickCount;
            foreach (var k in new List<string>(modes.Keys))
            {
                var m = modes[k];
                if (!m.Started || !m.Arrived) continue;
                bool expired = m.Once ? now - m.Start >= OnceMs(m) : (m.Ms > 0 && now - m.Start >= m.Ms);
                if (expired) { modes.Remove(k); if (m.OnDone != null) m.OnDone(); }
            }
            var top = TopMode();
            // while Lucky sleeps only alerts / breaks / focus may take over (no laptop, no looking around)
            if (top != null && st == St.Sleep && top.Prio > P_FOCUS) top = null;
            if (top == null) { curMode = null; lastTopKey = null; movingToTarget = false; return false; }
            if (lastTopKey != top.Key + top.Anim) { lastTopKey = top.Key + top.Anim; animStart = now; }
            curMode = top;
            if (!top.Started) { top.Started = true; top.Start = now; }
            if (!top.Arrived)
            {
                if (!double.IsNaN(top.TargetX) && !cfg.LockPet && Math.Abs(top.TargetX - x) > 4 * S)
                {
                    double d = top.TargetX - x;
                    dir = d > 0 ? 1 : -1;
                    x += dir * Math.Min(Math.Abs(d), 4 * S);
                    movingToTarget = true;
                    y = Ground();
                    return true;
                }
                top.Arrived = true; top.Start = now; animStart = now; movingToTarget = false;
                if (!double.IsNaN(top.TargetX)) dir = top.TargetX > Screen.PrimaryScreen.WorkingArea.Left + Screen.PrimaryScreen.WorkingArea.Width / 2 ? -1 : 1;
            }
            y = Ground();
            if (timer.Interval > 40 * (lowPower ? 2 : 1)) timer.Interval = 40 * (lowPower ? 2 : 1);
            return true;
        }

        string ModeAnim(out int fps, out bool once)
        {
            fps = 8; once = false;
            if (curMode == null) return null;
            if (movingToTarget) { fps = 14; return "run"; }
            fps = Fps(curMode.Anim); once = curMode.Once;
            return curMode.Anim;
        }

        double Corner() { var wa = Screen.PrimaryScreen.WorkingArea; return wa.Right - PW - 6 * S; }

        // ---------------------------------------------------------- transitions, eye tracking, crossfade
        string shownAnim, transAnim; int transStart, fadeStart; Bitmap fadeFrom, lastFrame;
        static readonly HashSet<string> StandAnims = new HashSet<string> { "walk", "run", "happy", "eat", "dodge", "battery_charged" };
        static readonly HashSet<string> LieAnims = new HashSet<string> { "sleep", "focus_break", "battery_critical", "focus_working" };
        static string Cat(string a) { return StandAnims.Contains(a) ? "stand" : LieAnims.Contains(a) ? "lie" : "sit"; }

        string PresentAnim(string anim, ref int fps, out int idx)
        {
            idx = -1;
            int now = Environment.TickCount;
            if (cfg != null && shownAnim != null && anim != shownAnim && transAnim == null)
            {
                string a = Cat(shownAnim), b = Cat(anim);
                if (a == "stand" && b == "sit" && HasAnim("transition_in")) { transAnim = "transition_in"; transStart = now; }
                else if (a == "sit" && b == "stand" && HasAnim("transition_out")) { transAnim = "transition_out"; transStart = now; }
                else if (lastFrame != null) { fadeFrom = lastFrame; fadeStart = now; }
            }
            if (transAnim != null)
            {
                int[] inf; int n = animInfo.TryGetValue(transAnim, out inf) ? inf[2] : sprR[transAnim].Length, f = Fps(transAnim);
                int i = (int)((long)(now - transStart) * f / 1000);
                if (i < n) { idx = i; fps = f; shownAnim = anim; return transAnim; }
                transAnim = null; animStart = now;
            }
            shownAnim = anim;
            // eyes follow the cursor while sitting
            if (cfg != null && cfg.F5 && (anim == "sit" || anim == "activity_working" || anim == "activity_look") && HasAnim("activity_look"))
            {
                var c = Cursor.Position;
                double hx = x + (dir > 0 ? 66 : CW - 66) * S, hy = y + 47 * S;
                double dx = c.X - hx, dy = c.Y - hy, dist = Math.Sqrt(dx * dx + dy * dy);
                if ((dist < 300 * dpiScale && dist > 12 * dpiScale) || anim == "activity_look")
                {
                    double ang = Math.Atan2(dy, dir > 0 ? dx : -dx);
                    idx = ((int)Math.Round(ang / (Math.PI / 4)) + 8) % 8;
                    return "activity_look";
                }
            }
            return anim;
        }

        void DrawPetFrame(Graphics g, string anim, Bitmap frame)
        {
            int now = Environment.TickCount;
            if (cfg != null && cfg.F7 && interactActive)
            {
                float pulse = 0.45f + 0.25f * (float)Math.Sin(now / 160.0);
                using (var ia = Tint(Color.FromArgb(120, 236, 240), pulse))
                    for (int k = 0; k < 12; k++)
                    {
                        double a = k * Math.PI / 6; float o = 2.6f * S;
                        g.DrawImage(frame, new Rectangle((int)(Math.Cos(a) * o), (int)(Math.Sin(a) * o), PW, PH), 0, 0, PW, PH, GraphicsUnit.Pixel, ia);
                    }
            }
            float t = (now - fadeStart) / 140f;
            if (fadeFrom != null && t < 1 && fadeFrom != frame)
            {
                using (var ia = Alpha(1 - t)) g.DrawImage(fadeFrom, new Rectangle(0, 0, PW, PH), 0, 0, PW, PH, GraphicsUnit.Pixel, ia);
                using (var ia = Alpha(t)) g.DrawImage(frame, new Rectangle(0, 0, PW, PH), 0, 0, PW, PH, GraphicsUnit.Pixel, ia);
            }
            else { fadeFrom = null; g.DrawImageUnscaled(frame, 0, 0); }
            lastFrame = frame;
        }

        static ImageAttributes Alpha(float a)
        {
            var cm = new ColorMatrix { Matrix33 = Math.Max(0, Math.Min(1, a)) };
            var ia = new ImageAttributes(); ia.SetColorMatrix(cm); return ia;
        }
        static ImageAttributes Tint(Color c, float a)
        {
            var cm = new ColorMatrix(new float[][] {
                new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 0, 0 },
                new float[] { 0, 0, 0, a, 0 }, new float[] { c.R / 255f, c.G / 255f, c.B / 255f, 0, 1 } });
            var ia = new ImageAttributes(); ia.SetColorMatrix(cm); return ia;
        }

        void FeatureOverlay(Graphics g)
        {
            if (cfg == null) return;
            PomoOverlay(g);
            F7Overlay(g);
        }

        // ---------------------------------------------------------- speech bubble (with optional buttons)
        class BubbleBtn { public string Text; public Action Act; public Rectangle R; }
        List<BubbleBtn> bubbleBtns;
        int bubbleShownAt;

        static KeyValuePair<string, Action> Btn(string t, Action a) { return new KeyValuePair<string, Action>(t, a); }

        void Say(string text, int ms, bool force)
        {
            if (!force && Quiet()) return;
            if (bubbleBtns != null && Environment.TickCount < bubbleUntil) return;   // don't overwrite a pending question
            bubbleText = text.Length > 60 ? text.Substring(0, 59) + "…" : text;
            bubbleUntil = Environment.TickCount + Math.Min(ms, 6000);
            bubbleBtns = null; bubbleShownAt = Environment.TickCount;
            if (!hiddenFs && !userHidden) bubbleWin.Show();
            if (cfg != null && curMode == null && st != St.Sleep && HasAnim("bubble_talk")) Request("talk", P_TALK, "bubble_talk", false, double.NaN, null, Math.Min(ms, 3000));
        }

        void Ask(string text, int ms, params KeyValuePair<string, Action>[] btns)
        {
            bubbleText = text.Length > 60 ? text.Substring(0, 59) + "…" : text;
            bubbleUntil = Environment.TickCount + ms;
            bubbleBtns = new List<BubbleBtn>();
            foreach (var b in btns) bubbleBtns.Add(new BubbleBtn { Text = b.Key, Act = b.Value });
            bubbleShownAt = Environment.TickCount;
            if (!hiddenFs && !userHidden) bubbleWin.Show();
        }

        void CloseBubble() { bubbleText = null; bubbleBtns = null; bubbleUntil = 0; bubbleWin.Hide(); }

        void OnBubbleDown(object s, MouseEventArgs e)
        {
            if (bubbleBtns == null) { if (!(cfg != null && cfg.F7)) CloseBubble(); return; }
            if (Environment.TickCount - bubbleShownAt < 500) return;   // guard against a click that was already in flight
            foreach (var b in bubbleBtns)
                if (b.R.Contains(e.Location)) { var act = b.Act; CloseBubble(); if (act != null) act(); return; }
        }

        void StepBubble()
        {
            if (bubbleText == null) return;
            if (Environment.TickCount > bubbleUntil) { CloseBubble(); return; }
            bubbleWin.ClickThrough = bubbleBtns == null && cfg != null && cfg.F7;
            using (var f = new Font("Segoe UI", 9f))
            using (var fb = new Font("Segoe UI Semibold", 8.5f))
            {
                var flags = TextFormatFlags.WordBreak | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding;
                int maxW = (int)(170 * dpiScale);
                var ts = TextRenderer.MeasureText(bubbleText, f, new Size(maxW, 1000), flags);
                int pad = (int)(8 * dpiScale), tail = (int)(7 * dpiScale), gap = (int)(6 * dpiScale);
                int btnH = 0, btnsW = 0;
                if (bubbleBtns != null)
                {
                    btnH = (int)(22 * dpiScale);
                    foreach (var b in bubbleBtns) btnsW += TextRenderer.MeasureText(b.Text, fb).Width + (int)(16 * dpiScale) + gap;
                    btnsW -= gap;
                }
                int innerW = Math.Max(ts.Width, btnsW);
                int w = innerW + pad * 2, h = ts.Height + pad * 2 + (btnH > 0 ? btnH + gap : 0);
                var bg = Color.FromArgb(250, 252, 255);
                using (var opaque = new Bitmap(w, h + tail, PixelFormat.Format32bppArgb))
                using (var outBmp = new Bitmap(w, h + tail, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(opaque))
                    {
                        g.Clear(bg);
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        TextRenderer.DrawText(g, bubbleText, f, new Rectangle(pad + (innerW - ts.Width) / 2, pad, ts.Width, ts.Height), Color.FromArgb(40, 52, 84), bg, flags);
                        if (bubbleBtns != null)
                        {
                            int bxp = pad + (innerW - btnsW) / 2, byp = pad + ts.Height + gap;
                            bool armed = Environment.TickCount - bubbleShownAt >= 500;
                            for (int i = 0; i < bubbleBtns.Count; i++)
                            {
                                var b = bubbleBtns[i];
                                int bw = TextRenderer.MeasureText(b.Text, fb).Width + (int)(16 * dpiScale);
                                b.R = new Rectangle(bxp, byp, bw, btnH);
                                var fill = i == 0 ? Color.FromArgb(armed ? 255 : 150, 28, 160, 200) : Color.FromArgb(armed ? 255 : 150, 222, 236, 250);
                                using (var path = Pill(b.R)) using (var br = new SolidBrush(fill)) g.FillPath(br, path);
                                TextRenderer.DrawText(g, b.Text, fb, b.R, i == 0 ? Color.White : Color.FromArgb(40, 60, 100), fill,
                                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                                bxp += bw + gap;
                            }
                        }
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
                    int bx2 = Math.Max(wa.Left, Math.Min(wa.Right - w, hx - w / 2));
                    int by2 = Math.Max(wa.Top, (int)(y + 8 * S) - (h + tail));
                    bubbleWin.Push(outBmp, bx2, by2);
                }
            }
        }

        static GraphicsPath Pill(Rectangle r)
        {
            var p = new GraphicsPath(); int d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 90, 180); p.AddArc(r.Right - d, r.Y, d, d, 270, 180); p.CloseFigure(); return p;
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

        // ---------------------------------------------------------- hotkeys
        static bool ParseHotkey(string s, out uint mods, out uint vk)
        {
            mods = 0x4000; vk = 0;   // MOD_NOREPEAT
            foreach (var raw in s.Split('+'))
            {
                string p = raw.Trim().ToLowerInvariant();
                if (p == "ctrl" || p == "control") mods |= 2;
                else if (p == "alt") mods |= 1;
                else if (p == "shift") mods |= 4;
                else if (p == "win") mods |= 8;
                else
                {
                    Keys k;
                    if (!Enum.TryParse(raw.Trim(), true, out k)) return false;
                    vk = (uint)k;
                }
            }
            return vk != 0 && (mods & 0xF) != 0;
        }

        string RegisterHotkeys(bool report)
        {
            var errs = new List<string>();
            UnregisterHotKey(Handle, 1); UnregisterHotKey(Handle, 2);
            uint m, v;
            if (cfg.F7 && (!ParseHotkey(cfg.HotkeyInteract, out m, out v) || !RegisterHotKey(Handle, 1, m, v))) errs.Add(cfg.HotkeyInteract);
            if (cfg.F3 && (!ParseHotkey(cfg.HotkeyPomodoro, out m, out v) || !RegisterHotKey(Handle, 2, m, v))) errs.Add(cfg.HotkeyPomodoro);
            string err = errs.Count == 0 ? null : "Phím tắt không dùng được (trùng hoặc sai): " + string.Join(", ", errs.ToArray());
            if (err != null && tray != null) tray.ShowBalloonTip(6000, "Lucky", err, ToolTipIcon.Warning);
            return err;
        }

        // ---------------------------------------------------------- tray menu additions
        void AddFeatureMenu(ContextMenuStrip menu)
        {
            menu.Items.Add(new ToolStripSeparator());
            interactItem = new ToolStripMenuItem("Bật tương tác thú");
            interactItem.Click += delegate { SetInteract(!cfg.Interact, false); };
            menu.Items.Add(interactItem);
            var hide = new ToolStripMenuItem("Tạm ẩn thú");
            hide.DropDownItems.Add("15 phút", null, delegate { HidePet(15); });
            hide.DropDownItems.Add("1 giờ", null, delegate { HidePet(60); });
            hide.DropDownItems.Add("Đến khi bật lại", null, delegate { HidePet(0); });
            hide.DropDownItems.Add("Hiện lại thú", null, delegate { ShowPetAgain(); });
            menu.Items.Add(hide);
            dndItem = new ToolStripMenuItem("Không làm phiền");
            dndItem.Click += delegate { cfg.Dnd = !cfg.Dnd; cfg.Save(); dndItem.Checked = cfg.Dnd; if (cfg.Dnd) { ClearModesFrom(P_ALERT); CloseBubble(); } };
            menu.Items.Add(dndItem);
            var pomo = new ToolStripMenuItem("Pomodoro 🍅");
            pomoStart = new ToolStripMenuItem("Bắt đầu Pomodoro"); pomoStart.Click += delegate { PomoStart(); };
            pomoPause = new ToolStripMenuItem("Tạm dừng"); pomoPause.Click += delegate { PomoTogglePause(); };
            pomoStop = new ToolStripMenuItem("Dừng"); pomoStop.Click += delegate { PomoStop(true); };
            pomo.DropDownItems.Add(pomoStart); pomo.DropDownItems.Add(pomoPause); pomo.DropDownItems.Add(pomoStop);
            menu.Items.Add(pomo);
            menu.Items.Add("Thống kê hôm nay 📊", null, delegate { ShowDaySummary(true); });
            menu.Items.Add("Cài đặt…", null, delegate { OpenSettings(); });
            menu.Opening += delegate
            {
                interactItem.Checked = cfg.Interact; interactItem.Enabled = cfg.F7;
                dndItem.Checked = cfg.Dnd;
                pomo.Enabled = cfg.F3;
                pomoStart.Enabled = pomo_ == PomoState.Off;
                pomoPause.Enabled = pomo_ != PomoState.Off;
                pomoPause.Text = pomoPaused ? "Tiếp tục" : "Tạm dừng";
                pomoStop.Enabled = pomo_ != PomoState.Off;
            };
        }

        void HidePet(int minutes)
        {
            userHidden = true;
            hideUntil = minutes > 0 ? Environment.TickCount + minutes * 60000 : 0;
            Hide(); ballWin.Hide(); boneWin.Hide(); CloseBubble();
        }

        void ShowPetAgain()
        {
            if (!userHidden) return;
            userHidden = false; hideUntil = 0;
            if (!hiddenFs) Show();
        }

        void UpdateTrayIcon()
        {
            if (tray == null) return;
            var old = tray.Icon;
            tray.Icon = MakeIconDot(cfg != null && cfg.F7 && cfg.Interact);
            if (old != null) old.Dispose();
        }

        Icon MakeIconDot(bool dot)
        {
            var baseIcon = MakeIcon();
            if (!dot) return baseIcon;
            using (var bmp = baseIcon.ToBitmap())
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.FillEllipse(Brushes.White, 19, 19, 13, 13);
                    using (var b = new SolidBrush(Color.FromArgb(28, 200, 120))) g.FillEllipse(b, 21, 21, 9, 9);
                }
                baseIcon.Dispose();
                IntPtr h = bmp.GetHicon();
                var ic = (Icon)Icon.FromHandle(h).Clone();
                DestroyIcon(h);
                return ic;
            }
        }

        // ---------------------------------------------------------- first run + settings window
        void ShowIntro()
        {
            MessageBox.Show(
                "Chào bạn! Lucky có thêm các tính năng: nhắc nghỉ, Pomodoro, báo pin/quá tải, phản ứng theo hoạt động và theo ứng dụng đang dùng.\n\n" +
                "Để làm được việc đó, Lucky chỉ đọc trên máy bạn:\n" +
                "• SỐ LẦN gõ phím / click (không ghi nội dung phím).\n" +
                "• Tên tiến trình và tiêu đề cửa sổ đang mở (không chụp màn hình).\n" +
                "• Mức pin, % CPU / RAM.\n" +
                "\n" +
                "Chỉ lưu số đếm / thời lượng trong " + Memory.Folder + ". Không gửi dữ liệu ra ngoài.\n\n" +
                "Mặc định Lucky KHÔNG chặn chuột (click xuyên qua). Muốn chơi với Lucky: bấm Ctrl+Alt+I, rồi giữ Alt hoặc rê chuột giữ yên trên Lucky.\n" +
                "Mọi tính năng bật/tắt được trong Cài đặt (chuột phải icon Lucky ở khay hệ thống).",
                "Lucky – giới thiệu", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        SettingsForm settingsForm;
        void OpenSettings()
        {
            if (settingsForm == null || settingsForm.IsDisposed) settingsForm = new SettingsForm(this, cfg);
            settingsForm.Show(); settingsForm.Activate();
        }

        public void SettingsChanged()
        {
            cfg.Save();
            RegisterHotkeys(true);
            if (!cfg.F5 && !cfg.F7) F5Uninstall(); else F5Install();
            if (!cfg.F1) ClearMode("break");
            if (!cfg.F3) PomoStop(false);
            if (!cfg.F4) { ClearMode("battery"); ClearMode("overload"); }
            if (!cfg.F5) ClearMode("activity");
            if (!cfg.F6) ClearMode("context");
            UpdateTrayIcon();
        }
    }

    // ================================================================ settings window
    class SettingsForm : Form
    {
        readonly PetForm pet; readonly Settings s;
        readonly List<Action> apply = new List<Action>();
        float sc = 1;

        public SettingsForm(PetForm p, Settings settings)
        {
            pet = p; s = settings;
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) sc = g.DpiX / 96f;
            Text = "Lucky · Cài đặt"; Font = new Font("Segoe UI", 9.5f);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowIcon = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(D(520), D(470));
            var tabs = new TabControl { Location = new Point(D(8), D(8)), Size = new Size(D(504), D(410)) };

            var t = Tab(tabs, "Chung");
            Check(t, "Không làm phiền (tắt mọi nhắc, chỉ giữ thú idle)", () => s.Dnd, v => s.Dnd = v);
            Check(t, "Lucky tự chơi khi bạn làm việc (bóng, chạy vòng, đuổi đuôi, bướm)", () => s.SelfPlay, v => s.SelfPlay = v);
            Check(t, "Âm thanh khi hết phiên Pomodoro", () => s.Sound, v => s.Sound = v);
            Note(t, "Kích thước thú và khởi động cùng Windows: chuột phải Lucky → Kích thước / Khởi động cùng Windows.");

            t = Tab(tabs, "Nhắc nghỉ (F1)");
            Check(t, "Bật nhắc nghỉ ngơi", () => s.F1, v => s.F1 = v);
            Num(t, "Nghỉ mắt 20-20-20 mỗi (phút)", () => s.EyeMin, v => s.EyeMin = v, 5, 240);
            Num(t, "Uống nước mỗi (phút)", () => s.WaterMin, v => s.WaterMin = v, 10, 480);
            Num(t, "Đứng dậy vươn vai mỗi (phút)", () => s.StretchMin, v => s.StretchMin = v, 10, 480);
            Note(t, "Chỉ đếm thời gian bạn hoạt động liên tục; rời máy > 5 phút được tính là đã nghỉ.");

            t = Tab(tabs, "Pomodoro (F3)");
            Check(t, "Bật Pomodoro", () => s.F3, v => s.F3 = v);
            Num(t, "Phiên tập trung (phút)", () => s.FocusMin, v => s.FocusMin = v, 1, 180);
            Num(t, "Nghỉ ngắn (phút)", () => s.ShortBreakMin, v => s.ShortBreakMin = v, 1, 60);
            Num(t, "Nghỉ dài (phút)", () => s.LongBreakMin, v => s.LongBreakMin = v, 1, 120);
            Num(t, "Nghỉ dài sau mỗi … phiên", () => s.LongEvery, v => s.LongEvery = v, 2, 12);
            Text_(t, "Phím tắt", () => s.HotkeyPomodoro, v => s.HotkeyPomodoro = v);
            Text_(t, "Trang/app gây xao nhãng (phẩy)", () => s.Distractions, v => s.Distractions = v);

            t = Tab(tabs, "Pin & máy (F4)");
            Check(t, "Bật cảnh báo pin / quá tải", () => s.F4, v => s.F4 = v);
            Num(t, "Pin yếu (%)", () => s.BatteryLow, v => s.BatteryLow = v, 5, 50);
            Num(t, "Pin rất yếu (%)", () => s.BatteryCritical, v => s.BatteryCritical = v, 2, 30);
            Num(t, "Quá tải khi CPU hoặc RAM > (%)", () => s.OverloadPct, v => s.OverloadPct = v, 50, 99);
            Num(t, "… liên tục trong (giây)", () => s.OverloadSec, v => s.OverloadSec = v, 10, 600);
            Num(t, "Không nhắc lại trong (phút)", () => s.AlertCooldownMin, v => s.AlertCooldownMin = v, 1, 120);

            t = Tab(tabs, "Hoạt động (F5)");
            Check(t, "Phản ứng theo nhịp gõ phím / chuột (chỉ đếm, không ghi nội dung)", () => s.F5, v => s.F5 = v);
            Num(t, "Gõ nhanh khi > (phím/phút)", () => s.FastKpm, v => s.FastKpm = v, 60, 1000);
            Num(t, "… liên tục trong (giây)", () => s.FastSec, v => s.FastSec = v, 3, 120);

            t = Tab(tabs, "Ngữ cảnh (F6)");
            Check(t, "Phản ứng theo ứng dụng đang dùng", () => s.F6, v => s.F6 = v);
            Check(t, "Đổi dáng theo app (xem phim → bỏng ngô)", () => s.WorkOutfit, v => s.WorkOutfit = v);
            Text_(t, "Giờ tổng kết ngày (HH:mm)", () => s.SummaryTime, v => s.SummaryTime = v);
            Note(t, "Nhóm ứng dụng: mỗi dòng nhom=tiến trình,…|từ khoá tiêu đề,…");
            var groups = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Width = D(470), Height = D(150), Font = new Font("Consolas", 8.5f) };
            var gl = new List<string>(); foreach (var kv in s.Groups) gl.Add(kv.Key + "=" + kv.Value);
            groups.Text = string.Join("\r\n", gl.ToArray());
            t.Controls.Add(groups);
            apply.Add(() =>
            {
                var d = new Dictionary<string, string>();
                foreach (var line in groups.Lines) { int i = line.IndexOf('='); if (i > 0) d[line.Substring(0, i).Trim().ToLowerInvariant()] = line.Substring(i + 1).Trim(); }
                if (d.Count > 0) s.Groups = d;
            });

            t = Tab(tabs, "Tương tác (F7)");
            Check(t, "Bật chống click nhầm (click xuyên qua thú)", () => s.F7, v => s.F7 = v);
            Check(t, "Bật tương tác thú (giữ Alt hoặc rê chuột giữ yên 0,8 giây)", () => s.Interact, v => s.Interact = v);
            Text_(t, "Phím tắt bật/tắt tương tác", () => s.HotkeyInteract, v => s.HotkeyInteract = v);
            Num(t, "Tự tắt tương tác sau (phút, 0 = không)", () => s.AutoOffMin, v => s.AutoOffMin = v, 0, 120);
            Check(t, "Né con trỏ khi chuột lao tới", () => s.Dodge, v => s.Dodge = v);
            Check(t, "Khoá thú (không di chuyển, không kéo được)", () => s.LockPet, v => s.LockPet = v);

            Controls.Add(tabs);
            var ok = new Button { Text = "Lưu", Size = new Size(D(100), D(30)), Location = new Point(D(306), D(428)) };
            var cancel = new Button { Text = "Đóng", Size = new Size(D(100), D(30)), Location = new Point(D(412), D(428)) };
            ok.Click += delegate
            {
                string oldI = s.HotkeyInteract, oldP = s.HotkeyPomodoro;
                foreach (var a in apply) a();
                pet.SettingsChanged();
                Close();
            };
            cancel.Click += delegate { Close(); };
            Controls.Add(ok); Controls.Add(cancel);
        }

        int D(int v) { return (int)(v * sc); }

        FlowLayoutPanel Tab(TabControl tabs, string title)
        {
            var page = new TabPage(title) { BackColor = Color.White };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(D(10)) };
            page.Controls.Add(flow); tabs.TabPages.Add(page);
            return flow;
        }

        void Check(FlowLayoutPanel t, string text, Func<bool> get, Action<bool> set)
        {
            var c = new CheckBox { Text = text, Checked = get(), AutoSize = true, Margin = new Padding(0, D(4), 0, D(4)) };
            t.Controls.Add(c); apply.Add(() => set(c.Checked));
        }

        void Num(FlowLayoutPanel t, string text, Func<int> get, Action<int> set, int min, int max)
        {
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, D(2), 0, D(2)) };
            row.Controls.Add(new Label { Text = text, AutoSize = false, Width = D(280), TextAlign = ContentAlignment.MiddleLeft, Height = D(26) });
            var n = new NumericUpDown { Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, get())), Width = D(80) };
            row.Controls.Add(n); t.Controls.Add(row);
            apply.Add(() => set((int)n.Value));
        }

        void Text_(FlowLayoutPanel t, string text, Func<string> get, Action<string> set)
        {
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, D(2), 0, D(2)) };
            row.Controls.Add(new Label { Text = text, AutoSize = false, Width = D(200), TextAlign = ContentAlignment.MiddleLeft, Height = D(26) });
            var b = new TextBox { Text = get(), Width = D(250) };
            row.Controls.Add(b); t.Controls.Add(row);
            apply.Add(() => set(b.Text.Trim()));
        }

        void Note(FlowLayoutPanel t, string text)
        {
            t.Controls.Add(new Label { Text = text, AutoSize = false, Width = D(460), Height = D(40), ForeColor = Color.FromArgb(100, 112, 140), Margin = new Padding(0, D(8), 0, D(4)) });
        }
    }
}
