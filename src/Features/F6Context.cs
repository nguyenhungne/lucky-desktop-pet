// F6 – reacts to what the user is working on: active app group, long focus streaks and an end-of-day summary.
// Reads only the foreground process name + window title (every 2 s); never screenshots or clipboard contents.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace LuckyPet
{
    partial class PetForm
    {
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);

        static readonly Dictionary<string, string> GroupNames = new Dictionary<string, string> {
            { "code", "lập trình" }, { "docs", "tài liệu" }, { "sheet", "bảng tính" }, { "email", "email" },
            { "meeting", "họp" }, { "design", "thiết kế" }, { "fun", "giải trí" } };

        int f6Last, ctxChangedAt = -100000, appSince;
        string ctxGroup, ctxProc;
        bool praised;
        readonly Dictionary<uint, string> pidNames = new Dictionary<uint, string>();

        bool InMeeting() { return cfg != null && cfg.F6 && ctxGroup == "meeting"; }

        void F6InitHandle() { }
        void F6Shutdown() { }
        void OnClipboardChanged() { }

        string ProcName(uint pid)
        {
            string n;
            if (pidNames.TryGetValue(pid, out n)) return n;
            try { n = Process.GetProcessById((int)pid).ProcessName.ToLowerInvariant(); } catch { n = ""; }
            if (pidNames.Count > 300) pidNames.Clear();
            pidNames[pid] = n;
            return n;
        }

        string Classify(string proc, string title)
        {
            string t = title.ToLowerInvariant();
            foreach (var kv in cfg.Groups)
            {
                var parts = kv.Value.Split('|');
                foreach (var p in parts[0].Split(',')) if (p.Trim().Length > 0 && proc == p.Trim().ToLowerInvariant()) return kv.Key;
                if (parts.Length > 1) foreach (var k in parts[1].Split(',')) if (k.Trim().Length > 0 && t.Contains(k.Trim().ToLowerInvariant())) return kv.Key;
            }
            return null;
        }

        void F6Tick()
        {
            int now = Environment.TickCount;
            if (now - f6Last < 2000) return;
            int dtSec = f6Last == 0 ? 0 : (now - f6Last) / 1000;
            f6Last = now;
            if (!cfg.F6 && !cfg.F3) return;

            IntPtr h = GetForegroundWindow();
            if (h == IntPtr.Zero || h == Handle) return;
            uint pid; GetWindowThreadProcessId(h, out pid);
            string proc = ProcName(pid);
            var sb = new StringBuilder(512); GetWindowText(h, sb, 512);
            string title = sb.ToString();
            PomoDistractionCheck(proc, title);
            if (!cfg.F6) { ClearMode("context"); ctxGroup = null; return; }

            if (proc != ctxProc) { ctxProc = proc; appSince = now; praised = false; }
            string group = Classify(proc, title);
            if (group != null && dtSec > 0)
            {
                var d = stats.Today.Sec; int v; d.TryGetValue(group, out v); d[group] = v + dtSec;
            }

            // context "outfit" (20 s cooldown so fast tab switching doesn't flicker)
            if (group != ctxGroup && now - ctxChangedAt >= 20000)
            {
                ctxGroup = group; ctxChangedAt = now;
            }
            // the outfit is a short "I'm working with you" moment, not a permanent pose:
            // 25 s after switching app group, then 25 s every 4 min (meetings stay in the corner the whole time)
            int since = now - ctxChangedAt;
            bool showCtx = ctxGroup == "meeting" || (cfg.WorkOutfit && (since < 25000 || since % 240000 < 25000));
            if (ctxGroup == null || !showCtx || (ctxGroup == "fun" && pomo_ != PomoState.Off)) ClearMode("context");
            else SetLoop("context", P_CONTEXT, "context_" + ctxGroup, ctxGroup == "meeting" ? Corner() : double.NaN);

            // long focus on one app
            if (group != null && group != "fun" && !praised && now - appSince > 45 * 60000)
            {
                praised = true;
                Request("ctx_evt", P_CTX_EVT, "context_praise", true);
                Say("Tập trung giỏi quá!", 4000, false);
            }
            // end-of-day summary
            DateTime at;
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            if (stats.LastSummary != today && DateTime.TryParse(cfg.SummaryTime, out at) && DateTime.Now.TimeOfDay >= at.TimeOfDay)
            {
                stats.LastSummary = today;
                ShowDaySummary(false);
            }
        }

        void ShowDaySummary(bool manual)
        {
            var d = stats.Today;
            string top = null; int topSec = 0;
            foreach (var kv in d.Sec) if (kv.Value > topSec) { topSec = kv.Value; top = kv.Key; }
            string g = top == null ? "" : " · nhiều nhất: " + (GroupNames.ContainsKey(top) ? GroupNames[top] : top);
            Request("summary", P_CTX_EVT - 1, "context_day_summary", false, double.NaN, null, 15000);
            Ask("Hôm nay: " + d.Pomo + " Pomodoro · nghỉ " + d.Done + " lần" + g, 15000, Btn("Chi tiết", ShowSummaryDetails), Btn("Đóng", () => ClearMode("summary")));
        }

        void ShowSummaryDetails()
        {
            ClearMode("summary");
            var d = stats.Today;
            var sb = new StringBuilder();
            sb.Append("Pomodoro hoàn thành: ").Append(d.Pomo).Append(" hôm nay · ").Append(stats.WeekPomo).Append(" trong 7 ngày\n");
            sb.Append("Lần nghỉ đã làm: ").Append(d.Done).Append(" · bỏ qua: ").Append(d.Skipped).Append("\n\nThời gian theo nhóm ứng dụng:\n");
            var list = new List<KeyValuePair<string, int>>(d.Sec);
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            foreach (var kv in list)
                sb.Append("  • ").Append(GroupNames.ContainsKey(kv.Key) ? GroupNames[kv.Key] : kv.Key).Append(": ")
                  .Append(kv.Value / 3600).Append("h ").Append(kv.Value / 60 % 60).Append("m\n");
            if (list.Count == 0) sb.Append("  (chưa có dữ liệu)\n");
            MessageBox.Show(sb.ToString(), "Lucky – tổng kết ngày", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
