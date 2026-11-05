# Lucky 🐶: Husky desktop pet for Windows

A plush-looking Husky that lives on top of your Windows taskbar. Lucky walks, naps, fetches a ball, eats treats, cheers you on, keeps you company while you focus, and remembers you between sessions. The app is a single ~0.9 MB exe that uses about 40 MB of RAM and almost no CPU.

![Lucky's sprite sheet: walk, run, happy, sit, blink, sit-happy, sleep, eat](docs/sprites.png)

## Features

| | |
|---|---|
| 🚶 **Lives on the taskbar** | Walks, sits, blinks, wags, and curls up to sleep (floating *z*'s) |
| 🖱️ **Chases the cursor** | Chases your cursor near the taskbar. It does this more often the closer you are |
| 💕 **Click to pet** | Lucky hops, smiles, sticks out its tongue and shows hearts. Sometimes it says something cute |
| ✋ **Drag & drop** | Drop Lucky anywhere and it falls back down with a bounce |
| 🎾 **Fetch** | Double-click Lucky or use the menu to throw a ball. The ball bounces and rolls on the taskbar, Lucky runs after it and carries it back to your cursor. Click the ball to kick it again |
| 🦴 **Treats** | A bone drops and Lucky runs over and munches it. Lucky gets hungry after 6 h and will ask for one |
| 💬 **Speech bubbles** | Lucky says something encouraging every 20–35 min ("Bạn đang làm rất tốt luôn!") |
| 🎯 **Focus mode** | 25-minute Pomodoro. Lucky lies still and quiet, the tray shows a countdown, and it tells you to take a break at the end |
| 🌙 **Knows the time** | Lucky says good morning once a day and nudges you to go to bed after 23:00 |
| 💧 **Health reminder** | Every 45 minutes Lucky reminds you to drink water and stretch |
| 🎮 **Stays out of the way** | Hides while a full-screen app or game is in the foreground |
| 📏 **Three sizes** | Nhỏ / Vừa / Lớn |

## Idle game 🏆

| | |
|---|---|
| ⭐ **Levels & XP** | Pet +3, fetch +12, treat +8, finished focus session +40, water reminder +3, and +1 per minute while you're using the PC. Each level-up pays 20 × level coins, with a sparkle burst |
| 🪙 **Lucky coins** | Passive income every active minute (1 + 0.2 × level, plus upgrades). **Offline earnings**: 50 % of the rate while Lucky was closed, capped at 8 h |
| 🛍️ **Shop** | Tray menu → *Cửa hàng & cấp độ*, or double-click the tray icon. **Accessories** (worn on the sprite, following every pose): aqua / navy / silver scarves, pink bow, beanie, party hat, sunglasses (Lv 3), silver crown (Lv 10). **Upgrades**: silver bowl, comfy bed, wooden house (+coins per minute), glowing ball (2× fetch rewards) |
| ✨ **Feedback** | Floating "+XP" / "+xu" numbers, and level, coins and affection in the tray tooltip. New players get a 60-coin welcome gift |

## Productivity features (F1–F7)

Each feature lives in its own file under `src/Features/` and can be turned on or off on its own in **Cài đặt…** (tray menu). One priority coordinator decides which state Lucky shows, from highest to lowest: alerts → breaks → focus → short reactions → work context → activity → idle. When a higher-priority state ends, Lucky returns to the previous one. Sit↔stand changes play a transition animation, and every other change cross-fades.

| | What it does |
|---|---|
| **F1 Breaks** | Counts *continuous* activity time. Being away for more than 5 min resets it. Reminders: 20-20-20 eyes (20 min), water (60 min), stretch (50 min). Lucky runs to the corner and plays the matching animation, with **Xong rồi / Hoãn 10 phút / Bỏ qua** buttons. Reminders are skipped during meetings and full-screen apps, and during a Pomodoro they wait for the break. Done/skipped counts are kept per day |
| **F3 Pomodoro** | 25 / 5 min with a 15-min long break every 4 sessions (all adjustable). Start from the tray, the right-click menu or **Ctrl+Alt+P**. Lucky types on a mini laptop with a progress ring next to it and stays quiet. At the end it celebrates with confetti, an optional sound, then the break. It shakes its head once if you open something on your distraction list. Sessions are counted per day and per week |
| **F4 Battery / overload** | On battery only: at 20 % or less Lucky gets *hungry*, at 10 % or less it lies down (repeats at most every 5 min), and when you plug in it jumps for joy. When CPU or RAM stays above 90 % for 60 s, Lucky sweats and fans itself. The bubble button shows the top 3 processes (display only, nothing is killed). Thresholds and cooldown are adjustable |
| **F5 Activity** | Counts keys and clicks only, never their content. Typing ≥ 200 keys/min for 10 s triggers a cheer, and every 100 keys earns +1 XP. Steady typing makes Lucky sit and watch. After 3 min idle it yawns, after 10 min it sleeps, and it wakes and waves when you return. ≥ 8 clicks in 2 s startles it, and Ctrl+S makes it nod "saved". Its head and eyes follow the cursor within about 220–300 px |
| **F6 Work context** | Reads the foreground process name and window title every 2 s. Groups (editable): code (glasses + laptop), docs (pen + notepad), sheet (calculator), email (letter), meeting (headset, moves to the corner, silences reminders), design (brush), fun (popcorn, hidden during Pomodoro). Switches have a 20-s cooldown. Lucky praises 45 min on one app, suggests single-tasking after more than 15 switches in 5 min, brings a gift when a download finishes ("Mở thư mục"), hugs a sheet of paper on copy (the clipboard is never read), reacts to terminal titles containing error/failed or passed/success, and shows a day summary at 17:30 |
| **F7 Click guard** | Lucky is **click-through by default** (`WS_EX_TRANSPARENT`). Toggle interaction with **Ctrl+Alt+I**, the tray switch (✓, plus a green dot on the icon) or Settings. Once enabled, hold **Alt** or hover still for 800 ms (a progress ring fills) to activate: Lucky glows and the cursor becomes a hand. Lucky dodges a cursor rushing at it, stays clear of caption buttons and the text caret, and fades to 30 % while you drag windows or scroll. Bubble buttons only accept clicks 500 ms after they appear. Dragging needs more than 10 px. *Khoá thú* locks it in place, and interaction turns itself off after 5 idle minutes. The chosen mode is remembered |

Tray menu: Bật tương tác thú · Tạm ẩn thú (15 phút / 1 giờ / đến khi bật lại) · Không làm phiền · Pomodoro · Thống kê hôm nay · Cài đặt. On first run, a short intro explains exactly what is read. Data stays in `%APPDATA%\LuckyPet` (`settings.txt`, `stats.txt`, `memory.txt`) and nothing is sent anywhere.

All 40 new animations (`art/actions.py`) use the same SDF toon renderer, palette, outline, 130×120 frame and 2× storage. Props such as the glass, laptop, notepad, calculator, letter, headset, brush, popcorn, gift and summary board are drawn as part of the sprite.

**Paused (not implemented yet):** F2 calendar alerts (needs Google/Microsoft OAuth) and its `meeting_alert / meeting_urgent / meeting_quiet` animations. Scrolling is detected through the mouse wheel only, not through scrollbar dragging.

## Memory 🧠

Lucky saves what it remembers to `%APPDATA%\LuckyPet\memory.txt`, a plain `key=value` file. Lucky writes it at most every 30 s when something changes, every 5 min regardless, on exit, and when Windows shuts down. Each write goes to a temp file first and then replaces the old one.

| Remembers | Effect |
|---|---|
| `first_met` | Days together. Lucky celebrates at 3 / 7 / 30 / 100 / 365 days |
| `last_seen` | After 12 h or more away, Lucky says it missed you, and affection drops a little |
| `affection` (0–100) | +3 per pet, +2 per fetch, +5 per treat, −1.2 per hour. A high value means more chasing and more hearts. A low value means Lucky naps more |
| `last_fed` | Lucky is hungry after 6 h and asks for a bone |
| `pets`, `balls`, `treats`, `chases`, `naps`, `drops`, `reminders`, `focus_sessions` | Shown in **Kỷ niệm với Lucky 📖** |
| `pos_x`, `facing`, `size`, `remind_on`, `last_morning`, `last_night` | Position, settings and daily greetings |

## Art

The art uses a cartoon **idle-game style**: bold ink outlines, flat two-tone cel shading with cool blue shadows, saturated colours, and crisp white specular dots. All sprites are drawn procedurally by [`art/render_sprites.py`](art/render_sprites.py) (Python, NumPy and Pillow):

- **Signed distance fields** with smooth unions, so body parts merge into clean chibi shapes.
- Surface normals drive a **two-band toon shader**. White fur gets a lighter shadow so it stays clean.
- Every part gets an **ink line** along its inner edge. The silhouette gets a thicker ink outline plus a white sticker ring.
- Subtle fluffy edges from value noise anchored to each part, so nothing swims between frames.
- Husky cap and goggle markings, anime eyes with a lash line and highlights, and light ambient occlusion where parts overlap.
- Rendered at 4×, then downsampled in premultiplied space.
- Accessories are drawn at runtime in the same toon style and follow per-frame head and neck anchors.

```powershell
python art/render_sprites.py assets --preview docs/sprites.png   # regenerate sprites (~4 min)
```

## Build & run

Requirements: Windows 10/11. It uses the C# compiler that ships with .NET Framework 4.x, so no SDK is needed. `assets/*.png` are embedded into the exe.

```powershell
./build.ps1
./bin/Lucky.exe
```

To start it with Windows, use the tray menu: **Khởi động cùng Windows**. This adds an `HKCU\...\Run` entry named `LuckyPet`.
