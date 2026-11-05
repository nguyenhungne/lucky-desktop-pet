// Self-play: while you work, Lucky entertains itself on the taskbar (dribbles a ball, zoomies,
// chases its tail, follows a butterfly). It never talks, never pops bubbles and stays click-through,
// so it is something nice to glance at, not something that pulls you out of your work.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LuckyPet
{
    partial class PetForm
    {
        bool selfBall; int selfKicks; double selfTarget;
        int zoomTurns; double zoomFrom, zoomTo;
        int spinTick, dizzyUntil;
        double bfX, bfY, bfVx, bfVy; int bfHop, bfAwayStart = -100000;

        bool UserBusy() { return IdleMs() < 60000; }

        // called from NextRandomState: returns true if a play behaviour was started
        bool MaybePlay()
        {
            if (cfg == null || !cfg.SelfPlay || Quiet() || cfg.LockPet || mem.Hungry && rnd.Next(2) == 0) return false;
            if (rnd.Next(100) >= (UserBusy() ? 38 : 18)) return false;
            switch (rnd.Next(4))
            {
                case 0: StartSelfBall(); break;
                case 1: StartZoomies(); break;
                case 2: spinTick = 0; SetState(St.Spin, rnd.Next(45, 70)); break;
                default: StartButterfly(); break;
            }
            return true;
        }

        void StartSelfBall()
        {
            if (ballActive) return;
            selfBall = true; selfKicks = rnd.Next(3, 7);
            bx = MouthX(115); by = y + 90 * S;
            bvx = dir * (3 + rnd.NextDouble() * 4) * S; bvy = -6 * S;
            ballActive = true; ballCarried = false; ballIdleSince = Environment.TickCount;
            ballWin.Show();
            SetState(St.Fetch, 600);
        }

        // Fetch reached the ball during self-play: nudge it on, or pick it up and put it away
        void SelfBallReached()
        {
            if (--selfKicks > 0)
            {
                if (rnd.Next(3) == 0) dir = -dir;   // sometimes turn and bat it the other way
                bvx = dir * (3 + rnd.NextDouble() * 5) * S; bvy = -(3 + rnd.NextDouble() * 6) * S;
                ballIdleSince = Environment.TickCount;
                if (rnd.Next(3) == 0) { vy = -5 * S; }  // little hop
                return;
            }
            var wa = Screen.PrimaryScreen.WorkingArea;
            ballCarried = true; ballWin.Hide();
            selfTarget = Math.Max(wa.Left, Math.Min(wa.Right - PW, x + (rnd.Next(2) == 0 ? -1 : 1) * rnd.Next(80, 260) * S));
            SetState(St.Carry, 600);
        }

        // Carry during self-play: trot off with the ball, then tuck it away
        void SelfBallCarry()
        {
            double d = selfTarget - x;
            if (Math.Abs(d) > 6 * S) { dir = d > 0 ? 1 : -1; x += dir * 2.8 * S; stateTicks = 600; return; }
            ballActive = false; ballCarried = false; selfBall = false;
            SetState(St.Remind, 70);   // proud wag
        }

        void StartZoomies()
        {
            var wa = Screen.PrimaryScreen.WorkingArea;
            double span = rnd.Next(180, 360) * S;
            zoomFrom = Math.Max(wa.Left, x - span / 2); zoomTo = Math.Min(wa.Right - PW, x + span / 2);
            zoomTurns = rnd.Next(3, 6);
            SetState(St.Zoom, 1200);
        }

        void StartButterfly()
        {
            bfX = dir > 0 ? 128 : 2; bfY = 18; bfVx = -dir * 0.6; bfVy = 0.3; bfHop = 0; bfAwayStart = -100000;
            SetState(St.Butterfly, rnd.Next(220, 380));
        }

        void PlayStep(Rectangle wa)
        {
            switch (st)
            {
                case St.Zoom:
                    x += dir * 6.2 * S;
                    if ((dir > 0 && x >= zoomTo) || (dir < 0 && x <= zoomFrom))
                    {
                        dir = -dir;
                        if (--zoomTurns <= 0) { SetState(St.Happy, 40); }
                    }
                    break;
                case St.Spin:
                    if (++spinTick % 4 == 0) dir = -dir;   // chasing its own tail
                    if (stateTicks <= 1) { dizzyUntil = Environment.TickCount + 2200; SetState(St.Sit, 80); }
                    break;
                case St.Butterfly:
                    {
                        // butterfly flutters around Lucky's head (canvas coordinates), Lucky follows and hops at it
                        double t = Environment.TickCount / 1000.0;
                        double hx = dir > 0 ? 92 : CW - 92;
                        bfVx += ((hx + dir * 22 + Math.Sin(t * 1.3) * 18) - bfX) * 0.004 + (rnd.NextDouble() - 0.5) * 0.25;
                        bfVy += ((22 + Math.Sin(t * 2.1) * 12) - bfY) * 0.006 + (rnd.NextDouble() - 0.5) * 0.25;
                        bfVx *= 0.92; bfVy *= 0.92;
                        bfX = Math.Max(4, Math.Min(CW - 4, bfX + bfVx)); bfY = Math.Max(6, Math.Min(70, bfY + bfVy));
                        if (bfX > hx + 8) dir = 1; else if (bfX < hx - 8) dir = -1;
                        x += dir * 1.1 * S;
                        if (++bfHop % 70 == 0 && y >= Ground() - 1) vy = -7 * S;
                        double g = Ground();
                        if (y < g || vy < 0) { vy += 0.9 * S; y += vy; if (y >= g) { y = g; vy = 0; } }
                        if (stateTicks <= 1) bfAwayStart = Environment.TickCount;
                        break;
                    }
            }
        }

        string PlayAnim(out int fps)
        {
            fps = 8;
            switch (st)
            {
                case St.Zoom: fps = 18; return "run";
                case St.Spin: fps = 16; return "run";
                case St.Butterfly: fps = 10; return y < Ground() - 2 ? "happy" : "walk";
            }
            return null;
        }

        void PlayOverlay(Graphics g)
        {
            int now = Environment.TickCount;
            if (st == St.Butterfly) Butterfly(g, (float)bfX, (float)bfY, now);
            else if (now - bfAwayStart < 1600)
            {
                float k = (now - bfAwayStart) / 1600f;
                Butterfly(g, (float)(bfX + (dir > 0 ? 1 : -1) * k * 30), (float)(bfY - k * 60), now);
            }
            if (now < dizzyUntil)
            {
                float hx = HeadX(66), ph = now / 300f;
                for (int k = 0; k < 3; k++)
                {
                    double a = ph + k * Math.PI * 2 / 3;
                    Star(g, hx + (float)Math.Cos(a) * 15, 20 + (float)Math.Sin(a) * 4, 2.6f);
                }
            }
        }

        static void Butterfly(Graphics g, float cx, float cy, int now)
        {
            float flap = (float)Math.Abs(Math.Sin(now / 70.0)) * 0.8f + 0.2f;
            using (var wing = new SolidBrush(Color.FromArgb(240, 120, 214, 255)))
            using (var wing2 = new SolidBrush(Color.FromArgb(240, 255, 168, 214)))
            using (var ink = new Pen(Color.FromArgb(220, 28, 36, 72), 0.7f))
            {
                var s = g.Save();
                g.TranslateTransform(cx, cy);
                g.ScaleTransform(flap, 1);
                g.FillEllipse(wing, -5.5f, -5f, 5.5f, 5f); g.DrawEllipse(ink, -5.5f, -5f, 5.5f, 5f);
                g.FillEllipse(wing, 0, -5f, 5.5f, 5f); g.DrawEllipse(ink, 0, -5f, 5.5f, 5f);
                g.FillEllipse(wing2, -4f, -0.5f, 4f, 3.6f); g.DrawEllipse(ink, -4f, -0.5f, 4f, 3.6f);
                g.FillEllipse(wing2, 0, -0.5f, 4f, 3.6f); g.DrawEllipse(ink, 0, -0.5f, 4f, 3.6f);
                g.Restore(s);
                using (var body = new SolidBrush(Color.FromArgb(255, 28, 36, 72))) g.FillEllipse(body, cx - 0.8f, cy - 3.5f, 1.6f, 6.5f);
            }
        }

        static void Star(Graphics g, float cx, float cy, float r)
        {
            var pts = new PointF[10];
            for (int i = 0; i < 10; i++)
            {
                double a = -Math.PI / 2 + i * Math.PI / 5; float rr = i % 2 == 0 ? r : r * 0.45f;
                pts[i] = new PointF(cx + (float)Math.Cos(a) * rr, cy + (float)Math.Sin(a) * rr);
            }
            using (var b = new SolidBrush(Color.FromArgb(255, 255, 226, 120))) g.FillPolygon(b, pts);
            using (var p = new Pen(Color.FromArgb(200, 28, 36, 72), 0.6f)) g.DrawPolygon(p, pts);
        }
    }
}
