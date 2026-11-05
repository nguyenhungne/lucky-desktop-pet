// F3 – Pomodoro focus timer with a progress ring next to Lucky.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Media;

namespace LuckyPet
{
    enum PomoState { Off, Focus, Break }

    partial class PetForm
    {
        PomoState pomo_ = PomoState.Off;
        bool pomoPaused;
        int pomoRemain, pomoTotal, pomoLastTick, pomoCycle;
        string lastDistractKey;

        bool PomoFocusing() { return pomo_ == PomoState.Focus && !pomoPaused; }

        void PomoStart()
        {
            if (!cfg.F3) return;
            if (HasMode("break")) ClearMode("break");   // the pending reminder is re-queued and merged into the next Pomodoro break
            pomo_ = PomoState.Focus; pomoPaused = false;
            pomoTotal = pomoRemain = cfg.FocusMin * 60000;
            pomoLastTick = Environment.TickCount;
            ClearMode("focus_break");
            Request("focus", P_FOCUS, "focus_working");
            CloseBubble();
            Say("Bắt đầu " + cfg.FocusMin + " phút tập trung!", 2500, true);
            UpdateTooltip();
        }

        void PomoTogglePause()
        {
            if (pomo_ == PomoState.Off) return;
            pomoPaused = !pomoPaused;
            pomoLastTick = Environment.TickCount;
            if (pomo_ == PomoState.Focus)
            {
                if (pomoPaused) ClearMode("focus"); else Request("focus", P_FOCUS, "focus_working");
            }
            Say(pomoPaused ? "Tạm dừng Pomodoro" : "Tiếp tục nào!", 2000, true);
            UpdateTooltip();
        }

        void PomoStop(bool say)
        {
            if (pomo_ == PomoState.Off) return;
            pomo_ = PomoState.Off; pomoPaused = false;
            ClearMode("focus"); ClearMode("focus_break");
            if (say) Say("Đã dừng Pomodoro", 2000, true);
            UpdateTooltip();
        }

        void PomoHotkey()
        {
            if (pomo_ == PomoState.Off) PomoStart(); else PomoTogglePause();
        }

        void PomoMenuToggle()
        {
            if (pomo_ == PomoState.Off) PomoStart(); else PomoStop(true);
        }

        void F3Tick()
        {
            if (pomo_ == PomoState.Off) return;
            if (!cfg.F3) { PomoStop(false); return; }
            int now = Environment.TickCount;
            if (!pomoPaused) pomoRemain -= now - pomoLastTick;
            pomoLastTick = now;
            if (frame % 125 == 0) UpdateTooltip();
            if (pomoRemain > 0) return;

            if (pomo_ == PomoState.Focus)
            {
                stats.Today.Pomo++; pomoCycle++;
                
                ClearMode("focus");
                Request("focus_done", P_FOCUS_EVT, "focus_done", true);
                if (cfg.Sound) try { SystemSounds.Asterisk.Play(); } catch { }
                bool longBreak = pomoCycle % Math.Max(2, cfg.LongEvery) == 0;
                int mins = longBreak ? cfg.LongBreakMin : cfg.ShortBreakMin;
                pomo_ = PomoState.Break; pomoTotal = pomoRemain = mins * 60000;
                Request("focus_break", P_FOCUS, "focus_break");
                bool pend = pendingBreaks.Count > 0;
                Ask("Xong phiên! Nghỉ " + mins + " phút" + (pend ? ": mắt, nước, vươn vai" : " nha"), 60000,
                    Btn("Ok", () => MergeBreaksIntoPomodoroBreak()));
                if (!pend) MergeBreaksIntoPomodoroBreak();
            }
            else
            {
                pomo_ = PomoState.Off;
                ClearMode("focus_break");
                Ask("Hết giờ nghỉ. Làm phiên tiếp?", 60000, Btn("Bắt đầu", () => PomoStart()), Btn("Để sau", () => { }));
            }
            UpdateTooltip();
        }

        // Called by F6 with the active app; shake head once per distracting window while focusing
        void PomoDistractionCheck(string proc, string title)
        {
            if (!PomoFocusing()) { lastDistractKey = null; return; }
            string hay = (proc + " " + title).ToLowerInvariant();
            foreach (var w in cfg.Distractions.Split(','))
            {
                string k = w.Trim().ToLowerInvariant();
                if (k.Length > 0 && hay.Contains(k))
                {
                    string key = proc + "|" + title;
                    if (key != lastDistractKey) { lastDistractKey = key; Request("distracted", P_FOCUS_EVT, "focus_distracted", true); }
                    return;
                }
            }
            lastDistractKey = null;
        }

        string PomoTooltipText()
        {
            if (pomo_ == PomoState.Off) return null;
            int m = Math.Max(0, (pomoRemain + 59999) / 60000);
            return "Lucky 🍅 " + (pomo_ == PomoState.Focus ? "tập trung" : "giải lao") + (pomoPaused ? " (tạm dừng)" : "") + " · còn " + m + " phút";
        }

        void PomoOverlay(Graphics g)
        {
            if (pomo_ == PomoState.Off) return;
            float frac = pomoTotal > 0 ? 1f - Math.Max(0, pomoRemain) / (float)pomoTotal : 0;
            float cx = dir > 0 ? 24 : CW - 24, cy = 30, r = 9;
            var col = pomo_ == PomoState.Focus ? Color.FromArgb(28, 190, 214) : Color.FromArgb(40, 200, 140);
            using (var b = new SolidBrush(Color.FromArgb(235, 250, 252, 255))) g.FillEllipse(b, cx - r - 2.5f, cy - r - 2.5f, (r + 2.5f) * 2, (r + 2.5f) * 2);
            using (var p = new Pen(Color.FromArgb(60, 30, 40, 70), 2.6f)) g.DrawEllipse(p, cx - r, cy - r, r * 2, r * 2);
            using (var p = new Pen(col, 2.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                if (frac > 0.005f) g.DrawArc(p, cx - r, cy - r, r * 2, r * 2, -90, 360 * frac);
            using (var p = new Pen(Color.FromArgb(200, 34, 38, 66), 1f)) g.DrawEllipse(p, cx - r - 2.5f, cy - r - 2.5f, (r + 2.5f) * 2, (r + 2.5f) * 2);
            if (pomoPaused)
                using (var b = new SolidBrush(Color.FromArgb(60, 70, 110))) { g.FillRectangle(b, cx - 3, cy - 3.5f, 2, 7); g.FillRectangle(b, cx + 1, cy - 3.5f, 2, 7); }
        }
    }
}
