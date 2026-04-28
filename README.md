# AutoNexus for Darza's Dominion

[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4)](https://dotnet.microsoft.com/) [![Platform](https://img.shields.io/badge/platform-Windows-0078D6)](#) [![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE) [![Status](https://img.shields.io/badge/status-personal_project-lightgrey)](#)

> ### ℹ️ Personal hobby project
> This is a **personal experimental project** shared publicly so other developers can read the code. It is **not actively promoted** for player use, **not maintained as a product**, and **not endorsed by anyone**. I publish it for educational and reverse-engineering interest — if you choose to run it, you do so entirely at your own risk. See the [Disclaimer](#disclaimer) section before using.

A small **.NET startup-hook** for **Darza's Dominion** that auto-teleports your character to the Nexus when your HP drops below a configurable threshold. The original motivation was learning how the .NET `DOTNET_STARTUP_HOOKS` mechanism interacts with WinForms-based games and how to do safe, read-only reflection-based interop with a third-party process — escaping a dying character is just the example use-case it grew around.

> **What it does in one sentence**
> Polls your in-game HP every ~150 ms and fires the same `/escape` packet the client sends when you press F5 — automatically, before you die.

![Tray menu screenshot placeholder](docs/screenshot-tray.png)

---

## Features

- **Hard-floor HP threshold** — escape when HP ≤ N (default: 100, fully configurable)
- **Configurable hotkey** to toggle ON/OFF mid-game (default: `F10`, supports modifiers)
- **System tray icon** with right-click menu (Settings, Open log, About, Quit)
- **In-game chat splash** — shows a colored line in your normal Darza chat panel when AutoNexus loads / toggles / saves you
- **Persistent settings** stored at `%APPDATA%\AutoNexusHook\settings.json`
- **Zero memory writes** — uses .NET reflection on the loaded game assemblies, only reads HP and sends a normal game packet
- **No DLL injection drama** — uses the official `DOTNET_STARTUP_HOOKS` environment variable, so antivirus & game don't see anything unusual

---

## How it works

1. The launcher (`AutoNexusLauncher.exe`) sets the env var `DOTNET_STARTUP_HOOKS=AutoNexusHook.dll`, then starts `DarzasDominion.exe`.
2. The .NET 9 runtime sees the env var at game-process startup and loads our DLL into the game process automatically.
3. Inside the game process, `AutoNexusHook` resolves the game's types via reflection (`World.Shared`, `Player.Health`, `Client.SendPacket`, `GmEscape`).
4. A background thread polls `Player.Health` every 150 ms. When HP ≤ threshold, it builds a `GmEscape` packet and pushes it through the same `Client.SendPacket(...)` method the rendered client uses for F5.
5. Three escapes are sent back-to-back (with 80 ms gaps) for resilience against single-packet drops.

The reflection chain is forgiving — short-name fallbacks let the hook keep working even if Darza's namespaces shift between builds.

---

## Quick start

### 1. Install .NET 9 Desktop Runtime

[Download from Microsoft](https://dotnet.microsoft.com/en-us/download/dotnet/9.0) — pick "Desktop Runtime", x64.

### 2. Get the build artifacts

Either download a release from the [Releases page](../../releases), or build yourself:

```pwsh
git clone https://github.com/purbler7-coder/AutoNexus-for-Darzas-Dominion.git
cd AutoNexus-for-Darzas-Dominion
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

| Field | What it does |
|---|---|
| AutoNexus enabled | Master on/off (also toggleable via hotkey & double-click on tray) |
| Hotkey | Click the textbox, press the key you want. Supports Ctrl/Shift/Alt modifiers. |
| Escape when HP ≤ N | Threshold in flat HP. Default 100. Higher = safer but escapes sooner. |
| Show splash on game launch | Disable to hide the in-game "AutoNexus loaded" chat line |
| Show balloon when AutoNexus saves you | Disable to suppress Windows tray notifications on save |

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
├── StartupHook.cs          # Entry point per .NET startup-hook protocol
├── NexusEngine.cs          # HP poll loop + GmEscape sender
├── HotkeyHook.cs           # WH_KEYBOARD_LL global hotkey
├── Notifier.cs             # Tray icon, splash, balloons, log
├── SettingsForm.cs         # In-tray settings dialog (WinForms)
├── Settings.cs             # JSON-persisted user config
├── InGameMessage.cs        # Posts colored lines to Darza's SideChat
└── Launcher/
    ├── AutoNexusLauncher.csproj   # Standalone .exe launcher
    └── Program.cs                 # Sets env var → starts game
```

Built with .NET 9, Windows Forms (for tray UI), no external dependencies.

---

## Troubleshooting

| Symptom | Likely cause / fix |
|---|---|
| Tray icon never appears | .NET 9 Desktop Runtime not installed. Check `%TEMP%\AutoNexusHook.log` for crash. |
| `Cannot resolve game types after 60s` in log | Darza updated and renamed the `World`/`Player`/`Client` types. Open an issue with the new build version. |
| Hotkey doesn't toggle | Another app already owns it (e.g. RivaTuner on F10). Change to a free key in Settings. |
| In-game chat splash doesn't show | Splash retries 3× over 6 sec. If world isn't loaded by then it gives up. Tray balloon still works. |
| AutoNexus says "saved you" but I died anyway | Server-side lag or the boss hit dealt > full HP in one packet. Try raising the threshold (e.g. 250). |

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

**Rights-holders / takedown:** If you represent Darza's Dominion and want this repository removed, please open an issue or contact me — I'll comply promptly. No specific persons or company names are referenced in this repository on purpose.

---

## License

MIT — see [LICENSE](LICENSE).

---

*Made with ❤️ by PurBler*
