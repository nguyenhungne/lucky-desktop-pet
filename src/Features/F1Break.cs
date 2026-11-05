// F1 – break reminders (20-20-20 eyes, water, stretch) based on continuous activity time.
using System;
using System.Collections.Generic;

namespace LuckyPet
{
    partial class PetForm
    {
        double eyeAcc, waterAcc, stretchAcc;     // minutes of continuous activity per reminder
        int f1LastTick = Environment.TickCount;
        readonly List<string> pendingBreaks = new List<string>();   // deferred while a Pomodoro is running

        void F1Tick()
        {
            int now = Environment.TickCount;
            double dt = (now - f1LastTick) / 60000.0;
            f1LastTick = now;
            if (!cfg.F1) return;
            if (IdleMs() > 5 * 60 * 1000) { eyeAcc = waterAcc = stretchAcc = 0; pendingBreaks.Clear(); return; }   // away = rested
            eyeAcc += dt; waterAcc += dt; stretchAcc += dt;
            if (HasMode("break") || hiddenFs || userHidden || InMeeting() || pomo_ == PomoState.Break) return;

            string kind = stretchAcc >= cfg.StretchMin ? "stretch" : waterAcc >= cfg.WaterMin ? "water" : eyeAcc >= cfg.EyeMin ? "eye" : null;
            if (kind == null) return;
            if (PomoFocusing())
            {
                if (!pendingBreaks.Contains(kind)) pendingBreaks.Add(kind);
                SetAcc(kind, -1e9);   // hold until the Pomodoro break; the break resets it
                return;
            }
            ShowBreak(kind);
        }

        double Period(string kind) { return kind == "eye" ? cfg.EyeMin : kind == "water" ? cfg.WaterMin : cfg.StretchMin; }
        void SetAcc(string kind, double v)
        {
            if (kind == "eye") eyeAcc = v; else if (kind == "water") waterAcc = v; else stretchAcc = v;
        }

        void ShowBreak(string kind)
        {
            string text = kind == "eye" ? "Nghỉ mắt 20 giây: nhìn xa 6 mét nha"
                        : kind == "water" ? "Uống một ngụm nước nào!"
                        : "Đứng dậy vươn vai một chút nhé!";
            Request("break", P_BREAK, "break_" + kind, false, Corner());
            Ask(text, 10 * 60 * 1000,
                Btn("Xong rồi", () => BreakDone(kind)),
                Btn("Hoãn 10 phút", () => BreakSnooze(kind)),
                Btn("Bỏ qua", () => BreakSkip(kind)));
        }

        void BreakDone(string kind)
        {
            SetAcc(kind, 0);
            stats.Today.Done++;
            ClearMode("break");
            Request("break_done", P_BREAK, "break_done", true);
            Reward(3, 1);
        }

        void BreakSnooze(string kind)
        {
            SetAcc(kind, Period(kind) - 10);   // comes back after exactly 10 more active minutes
            ClearMode("break");
        }

        void BreakSkip(string kind)
        {
            SetAcc(kind, 0);
            stats.Today.Skipped++;
            ClearMode("break");
        }

        // Pomodoro break: merge any deferred reminders into it
        void MergeBreaksIntoPomodoroBreak()
        {
            foreach (var k in pendingBreaks) SetAcc(k, 0);
            if (pendingBreaks.Count > 0) stats.Today.Done += pendingBreaks.Count;
            pendingBreaks.Clear();
        }
    }
}
