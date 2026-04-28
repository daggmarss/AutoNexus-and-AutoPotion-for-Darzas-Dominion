using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace AutoNexusHook;

/// <summary>
/// Tray icon, menus, splash, balloons, log file. All UI runs on a
/// dedicated STA thread that owns the message pump for the icon —
/// without that pump tray events / settings dialogs hang.
/// </summary>
internal static class Notifier
{
    private static readonly string LogPath = Path.Combine(
        Path.GetTempPath(), "AutoNexusHook.log");
    private static readonly object _logLock = new();

    private static NotifyIcon? _trayIcon;
    private static ToolStripMenuItem? _enableItem;
    private static Settings _settings = new();
    private static Thread? _uiThread;
    private static ApplicationContext? _ctx;

    public static void Init(Settings settings)
    {
        _settings = settings;
        Log("=== AutoNexusHook loaded ===");
        StartUiThread();
    }

    private static void StartUiThread()
    {
        if (_uiThread != null) return;
        _uiThread = new Thread(() =>
        {
            try
            {
                BuildTray();
                if (_settings.ShowSplashOnLoad)
                    ShowSplashBalloon();
                _ctx = new ApplicationContext();
                Application.Run(_ctx);
            }
            catch (Exception ex)
            {
                LogError($"UI thread crashed: {ex}");
            }
        })
        { IsBackground = true, Name = "AutoNexus-UI" };
        _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.Start();
    }

    private static void BuildTray()
    {
        var menu = new ContextMenuStrip
        {
            BackColor = Color.FromArgb(22, 27, 34),
            ForeColor = Color.FromArgb(201, 209, 217),
            ShowImageMargin = false,
        };

        _enableItem = new ToolStripMenuItem
        {
            Text = _settings.Enabled ? "✓ AutoNexus: ON" : "✗ AutoNexus: OFF",
            ForeColor = _settings.Enabled ? Color.LimeGreen : Color.IndianRed,
        };
        _enableItem.Click += (_, _) => Toggle();
        menu.Items.Add(_enableItem);

        var hotkeyItem = new ToolStripMenuItem($"Hotkey: {HotkeyHook.Format()}")
        {
            Enabled = false,
        };
        menu.Items.Add(hotkeyItem);

        menu.Items.Add(new ToolStripSeparator());

        var settingsItem = new ToolStripMenuItem("Settings...");
        settingsItem.Click += (_, _) => OpenSettings(hotkeyItem);
        menu.Items.Add(settingsItem);

        var openLogItem = new ToolStripMenuItem("Open log file");
        openLogItem.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start("notepad.exe", LogPath); }
            catch (Exception ex) { LogError($"OpenLog: {ex.Message}"); }
        };
        menu.Items.Add(openLogItem);

        menu.Items.Add(new ToolStripSeparator());

        var aboutItem = new ToolStripMenuItem("About / With Love by PurBler <3")
        {
            ForeColor = Color.FromArgb(255, 105, 180),
        };
        aboutItem.Click += (_, _) =>
            MessageBox.Show("AutoNexus by PurBler\n\nWith Love <3",
                "AutoNexus", MessageBoxButtons.OK, MessageBoxIcon.Information);
        menu.Items.Add(aboutItem);

        var quitItem = new ToolStripMenuItem("Quit AutoNexus (game keeps running)");
        quitItem.Click += (_, _) =>
        {
            try { _trayIcon!.Visible = false; _trayIcon.Dispose(); } catch { }
            _ctx?.ExitThread();
        };
        menu.Items.Add(quitItem);

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Shield,
            Text = $"AutoNexus by PurBler — {(_settings.Enabled ? "ON" : "OFF")}",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _trayIcon.DoubleClick += (_, _) => Toggle();
    }

    public static void Toggle()
    {
        _settings.Enabled = !_settings.Enabled;
        _settings.Save();
        UpdateEnableUi();
        NotifyToggled(_settings.Enabled);
        try
        {
            _trayIcon?.ShowBalloonTip(1500,
                "AutoNexus",
                _settings.Enabled
                    ? "Enabled — you'll be auto-escaped when low HP"
                    : "Disabled — manual escape only",
                _settings.Enabled ? ToolTipIcon.Info : ToolTipIcon.Warning);
        }
        catch { }
        Log($"Toggle → Enabled = {_settings.Enabled}");
    }

    public static bool IsEnabled => _settings.Enabled;
    public static int  HpPct     => _settings.HpThresholdPercent;
    public static int  HpFloor   => _settings.HpHardFloor;

    private static void UpdateEnableUi()
    {
        if (_enableItem == null || _trayIcon == null) return;
        // Tray UI can only be touched on the UI thread — marshal it.
        try
        {
            _trayIcon.ContextMenuStrip!.BeginInvoke(new Action(() =>
            {
                _enableItem.Text = _settings.Enabled ? "✓ AutoNexus: ON" : "✗ AutoNexus: OFF";
                _enableItem.ForeColor = _settings.Enabled ? Color.LimeGreen : Color.IndianRed;
                _trayIcon.Text = $"AutoNexus by PurBler — {(_settings.Enabled ? "ON" : "OFF")}";
            }));
        }
        catch { }
    }

    private static void OpenSettings(ToolStripMenuItem hotkeyItem)
    {
        try
        {
            using var dlg = new SettingsForm(_settings);
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                hotkeyItem.Text = $"Hotkey: {HotkeyHook.Format()}";
                UpdateEnableUi();
            }
        }
        catch (Exception ex)
        {
            LogError($"Settings dialog failed: {ex}");
        }
    }

    private static void ShowSplashBalloon()
    {
        // Try once; SideChat may not exist yet at startup-hook time.
        // Retry every 2 s for up to 30 s — by then the world is loaded.
        new Thread(() =>
        {
            string msg = $"AutoNexus loaded — Hotkey: {HotkeyHook.Format()} — With Love by PurBler <3";
            for (int i = 0; i < 15; i++)
            {
                try { InGameMessage.Post(msg); }
                catch { }
                Thread.Sleep(2000);
                // Check log for "chat path not resolved" — if it
                // resolved at least once we can stop retrying.
                // Simpler: just stop after first attempt that didn't
                // hit the "unavailable" path (we don't have a return
                // signal, so just do 3 retries = 6 sec).
                if (i >= 2) break;
            }
        }) { IsBackground = true, Name = "AutoNexus-Splash" }.Start();

        // Also tray balloon as fallback — friend definitely sees it.
        try
        {
            _trayIcon?.ShowBalloonTip(4000,
                "AutoNexus loaded",
                $"Hotkey: {HotkeyHook.Format()}\n" +
                "Right-click tray icon for settings.\n" +
                "With Love by PurBler <3",
                ToolTipIcon.Info);
        }
        catch { }
    }

    public static void NotifySaved(int hp, int maxHp)
    {
        Log($"AutoNexus triggered: {hp}/{maxHp} HP — sent /escape");
        // Primary: show in game chat (visible while playing).
        try { InGameMessage.Post($"AutoNexus saved you @ {hp}/{maxHp} HP — by PurBler <3"); }
        catch { }
        // Secondary: tray balloon if user opted in.
        if (!_settings.ShowSaveBalloons) return;
        try
        {
            _trayIcon?.ShowBalloonTip(2500,
                "AutoNexus saved you",
                $"HP {hp}/{maxHp} — escape sent\nWith Love by PurBler <3",
                ToolTipIcon.Warning);
        }
        catch { }
    }

    public static void NotifyToggled(bool nowOn)
    {
        try
        {
            InGameMessage.Post(
                nowOn ? "AutoNexus ON — by PurBler <3"
                      : "AutoNexus OFF",
                r: nowOn ? (byte)0x3F : (byte)0xF8,
                g: nowOn ? (byte)0xB9 : (byte)0x51,
                b: nowOn ? (byte)0x50 : (byte)0x49);
        }
        catch { }
    }

    public static void Log(string msg)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
        lock (_logLock)
        {
            try { File.AppendAllText(LogPath, line); } catch { }
        }
        try { Console.Out.WriteLine($"[AutoNexus] {msg}"); } catch { }
    }

    public static void LogError(string msg) => Log("ERROR: " + msg);
}
