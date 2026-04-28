# AutoNexus + AutoPotion for Darza's Dominion

[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4)](https://dotnet.microsoft.com/) [![Platform](https://img.shields.io/badge/platform-Windows-0078D6)](#) [![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE) [![Status](https://img.shields.io/badge/status-personal_project-lightgrey)](#)

> ### ℹ️ Personal hobby project
> This is a **personal experimental project** shared publicly so other developers can read the code. It is **not actively promoted** for player use, **not maintained as a product**, and **not endorsed by anyone**. I publish it for educational and reverse-engineering interest — if you choose to run it, you do so entirely at your own risk. See the [Disclaimer](#disclaimer) section before using.

Two complementary QoL hooks for **Darza's Dominion**, packaged as a single .NET startup-hook:

- 🛡️ **AutoNexus** — auto-teleport to the Nexus when HP drops below a hard HP floor (default: HP ≤ 100)
- 💧 **AutoPotion** — auto-drink Health / Mana potions when stat falls below a percentage threshold (default: HP ≤ 70%, MP off)

Originally built as a learning exercise with .NET `DOTNET_STARTUP_HOOKS` and reflection-based interop with a third-party game process. The "save your character" use-cases are useful examples it grew around.

> **What they do in one sentence each**
> *AutoNexus* — polls your HP every ~150 ms and fires the same `/escape` packet pressing F5 sends, automatically before you die.
> *AutoPotion* — polls HP/MP every ~180 ms and fires the same `GmUseItem` packet pressing Q (HP pot) or W (MP pot) sends, automatically when you cross the threshold.

![Tray menu screenshot placeholder](docs/screenshot-tray.png)

---

## Features

### 🛡️ AutoNexus

- **Hard HP-floor threshold** — escape when `HP ≤ N` (default: 100, fully configurable)
- **Three escape packets** sent back-to-back (80 ms apart) — resilient against single-packet drops
- **4-second internal cooldown** so back-to-back triggers don't spam

### 💧 AutoPotion *(new in v1.1.0)*

- **Per-feature on/off** — run JUST AutoNexus, JUST AutoPot, or both
- **Percentage thresholds** — auto-drink HEALTH potion when `HP%` ≤ N (default 70%); same for MANA pot (default 40%, off by default)
- **Uses the game's own `Player.PotionIndex` lookup** — picks the same potion slot the Q/W keybind would use, of any tier (Minor → Supreme)
- **1.5 s cooldown between uses** — matches server limits, won't get flagged for spam

### 🔧 Shared infrastructure

- **One global hotkey** to toggle both features ON/OFF mid-game (default: `F10`, supports modifiers)
- **System tray icon** with right-click menu (Settings, Open log, About, Quit), live status row showing both thresholds
- **In-game chat splash** — shows a colored line in your normal Darza chat panel when the hook loads / toggles / saves you
- **Persistent settings** stored at `%APPDATA%\AutoNexusHook\settings.json`
- **Zero memory writes** — pure .NET reflection on the loaded game assemblies. Only reads stats and sends normal game packets
- **No DLL injection drama** — uses the official `DOTNET_STARTUP_HOOKS` environment variable, so antivirus & game don't see anything unusual

---

## How it works

1. The launcher (`AutoNexusLauncher.exe`) sets the env var `DOTNET_STARTUP_HOOKS=AutoNexusHook.dll`, then starts `DarzasDominion.exe`.
2. The .NET 9 runtime sees the env var at game-process startup and loads our DLL into the game process automatically.
3. Inside the game process, the hook resolves the game's public types via reflection (`World.Shared`, `Player`, `Client`, the relevant packet types).
4. **Two parallel watcher threads** start:
   - **NexusEngine** — polls `Player.Health` every 150 ms. When `HP ≤ HpHardFloor`, builds a `GmEscape` packet and sends it via `Client.SendPacket(...)`. Three back-to-back fires for drop-resilience.
   - **PotionEngine** — polls `Player.Health` / `MaxHealth` / `Mana` / `MaxMana` every 180 ms. When `HP%` (or `MP%`) crosses its threshold AND the player has a matching potion (looked up via `Player.PotionIndex` / `Player.ManaPotionIndex`), builds a `GmUseItem` packet for that slot and sends it — exactly mirroring what `Slot.Activate()` does when you press Q/W.
5. All access is read-only on the game's state. No memory is written, no game internals are mutated. Only normal client→server packets are sent.

The reflection chain is forgiving — short-name fallbacks let both engines keep working even if Darza's namespaces shift between builds. If a member can't be resolved, the engine logs an error and exits cleanly without affecting the game.

---

## Quick start

### 1. Install .NET 9 Desktop Runtime

[Download from Microsoft](https://dotnet.microsoft.com/en-us/download/dotnet/9.0) — pick "Desktop Runtime", x64.

### 2. Get the build artifacts

Either download a release from the [Releases page](../../releases), or build yourself:

```pwsh
git clone https://github.com/purbler7-coder/AutoNexus-and-AutoPotion-for-Darzas-Dominion.git
cd AutoNexus-and-AutoPotion-for-Darzas-Dominion
dotnet publish AutoNexusHook.csproj -c Release -o dist\hook
dotnet publish Launcher\AutoNexusLauncher.csproj -c Release -o dist\launcher
```

You'll end up with two files you actually need:
- `AutoNexusHook.dll`
- `AutoNexusLauncher.exe` (+ `.deps.json`, `.runtimeconfig.json`)

### 3. Drop them into your Darza folder

Copy `AutoNexusHook.dll` + `AutoNexusLauncher.exe` (and the two json files) into the same folder as `DarzasDominion.exe`.

```
DarzasDominion/
├── DarzasDominion.exe
├── AutoNexusHook.dll          ← this
├── AutoNexusLauncher.exe      ← this
├── AutoNexusLauncher.deps.json
├── AutoNexusLauncher.runtimeconfig.json
└── ... (everything else)
```

### 4. Run

Double-click **`AutoNexusLauncher.exe`** instead of `DarzasDominion.exe`.

The game starts as normal. After a few seconds you'll see:
- A pink/red **tray icon** (shield) appear in the Windows system tray
- An **in-game chat line**: `AutoNexus loaded — Hotkey: F10 — With Love by PurBler <3`
- A **balloon tip** confirming the load

That's it. Play normally; the tool watches HP in the background.

---

## Configuration

**Right-click the tray icon → Settings**

### Master + hotkey

| Field | What it does |
|---|---|
| Master enabled (AutoNexus + AutoPotion) | Global on/off — kills both engines at once. Toggleable via hotkey or double-click on the tray icon. |
| Hotkey | Click the textbox, press the key you want. Supports Ctrl/Shift/Alt modifiers. |

### AutoNexus

| Field | Default | What it does |
|---|---|---|
| Escape when HP ≤ N | `100` HP | Hard HP floor (flat number). Higher = safer (escapes earlier). |

### AutoPotion

| Field | Default | What it does |
|---|---|---|
| Auto-use HEALTH potion below | ✅ `70 %` | Drinks the next available HP pot from inventory when `HP%` ≤ N. Works with all tiers (Minor → Supreme). |
| Auto-use MANA potion below | ❌ `40 %` | Same for MP. **Off by default** — mana usage is class-strategic; some classes (Warrior, Knight) don't need it, others (Wizard, Sorcerer) waste pots on overflow if MP regen is high. |

Both AutoPotion rows respect the master toggle. Built-in 1.5 s cooldown between uses.

### Notifications

| Field | Default | What it does |
|---|---|---|
| Show splash on game launch | ✅ | Posts a colored line in your in-game chat when the hook loads |
| Show balloon when AutoNexus saves you | ✅ | Windows tray balloon when you get auto-nexus'd |

Settings persist to `%APPDATA%\AutoNexusHook\settings.json` and survive reinstalls.

### Logs

Every load, toggle, and escape is written to `%TEMP%\AutoNexusHook.log` — useful if something goes wrong:

```
[14:22:01] === AutoNexusHook loaded ===
[14:22:01] Hotkey set to: F10
[14:22:03] Polling HP=820/820 (threshold=100, enabled=True)
[14:31:54] AutoNexus triggered: 88/820 HP — sent /escape
```

You can also open the log straight from the tray menu → **Open log file**.

---

## Project structure

```
AutoNexusHook/
├── AutoNexusHook.csproj    # Hook DLL (loaded into game process)
├── StartupHook.cs          # Entry point per .NET startup-hook protocol;
│                           # spins up both engines on parallel threads
├── NexusEngine.cs          # HP poll loop → GmEscape packet sender
├── PotionEngine.cs         # HP/MP poll loop → GmUseItem packet sender
├── HotkeyHook.cs           # WH_KEYBOARD_LL global hotkey
├── Notifier.cs             # Tray icon, splash, balloons, log
├── SettingsForm.cs         # In-tray settings dialog (WinForms)
├── Settings.cs             # JSON-persisted user config
├── InGameMessage.cs        # Posts colored lines to Darza's SideChat
└── Launcher/
    ├── AutoNexusLauncher.csproj   # Standalone .exe launcher
    └── Program.cs                 # Sets env var → starts game
```

Built with .NET 9, Windows Forms (for tray UI), no external dependencies. Both engines share the `Notifier` static for log output and config forwarding — they run on independent threads but contend for nothing.

---

## Troubleshooting

| Symptom | Likely cause / fix |
|---|---|
| Tray icon never appears | .NET 9 Desktop Runtime not installed. Check `%TEMP%\AutoNexusHook.log` for crash. |
| `Cannot resolve game types after 60s` in log | Darza updated and renamed the `World`/`Player`/`Client` types. Open an issue with the new build version. |
| Hotkey doesn't toggle | Another app already owns it (e.g. RivaTuner on F10). Change to a free key in Settings. |
| In-game chat splash doesn't show | Splash retries 3× over 6 sec. If world isn't loaded by then it gives up. Tray balloon still works. |
| AutoNexus says "saved you" but I died anyway | Server-side lag or the boss hit dealt > full HP in one packet. Try raising the threshold (e.g. 250). |
| AutoPot doesn't drink even though HP is low | Either: (a) no HP pot in your inventory; (b) on the 1.5 s post-use cooldown; (c) AutoPot disabled or master toggle off; (d) game's `PotionIndex` not resolving — check the log. |
| AutoPot wastes my MANA potions | Lower the MP threshold (e.g. `20 %` instead of `40 %`), or simply disable MP pot in Settings — it's off by default for this reason. |
| Hook fires twice on the same dip | The 4 s AutoNexus cooldown and 1.5 s AutoPot cooldown are independent. If you escape AND drink a pot in the same low-HP frame, that's expected — both are armed for the same threshold. |

---

## Disclaimer

**Educational / personal-use project. Use entirely at your own risk.**

This is a **hobby project published for educational and research purposes** — specifically:
- Learning the .NET `DOTNET_STARTUP_HOOKS` runtime mechanism
- Practising reflection-based interop with a foreign .NET process
- Studying Win32 low-level keyboard hooks (`WH_KEYBOARD_LL`)
- Exploring tray-icon / notification UI patterns in WinForms

It is **not commercial**, **not maintained as a product**, **not actively promoted** to any player community, and the author **does not encourage anyone to run it in their own game**. If you stumbled onto this repository and decide to try it anyway, that decision is yours alone.

The tool reads memory of and sends a single emergency packet to a third-party game (Darza's Dominion) via reflection on its loaded .NET assemblies. AutoNexus deliberately does **not**:
- modify any game memory
- exploit bugs or unintended behaviour
- give any combat advantage, gold/loot/XP gain, or competitive edge
- automate gameplay beyond a one-shot emergency escape

Even so, third-party tools of any kind may be against the game's Terms of Service. The author assumes **no responsibility whatsoever** for:
- Account suspensions, bans, or other punitive action by the game's operators
- Lost characters, items, or progress
- Any other direct or indirect damages resulting from use of this code

**Rights-holders / takedown:** If you represent Darza's Dominion and want this repository removed, please open an issue **or message me directly on Discord: `purbler2.0`** — I'll comply promptly. No specific persons or company names are referenced in this repository on purpose.

---

## Contact

The fastest way to reach me is **Discord: `purbler2.0`**.

- 💬 Found a bug, want to chat about the code, have a feature idea? → Discord
- 📝 Prefer a public, searchable thread? → [GitHub Issues](../../issues)
- 🛡️ Rights-holder takedown request? → Discord (instant) or [GitHub Issue](../../issues/new)

I'm a single person doing this in spare time — no support guarantees, but I'll usually respond within a day or two.

---

## License

MIT — see [LICENSE](LICENSE).

---

*Made with ❤️ by PurBler*
