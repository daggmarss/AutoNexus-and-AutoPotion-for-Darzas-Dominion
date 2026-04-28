using System;
using System.Drawing;
using System.Windows.Forms;

namespace AutoNexusHook;

/// <summary>
/// Compact settings dialog. Lets the user configure both AutoNexus
/// (escape-on-low-HP) and AutoPotion (auto-drink HP/MP pot) features
/// from a single form. Opens via tray icon → Settings.
///
/// Sections:
///   1. Master enable + global hotkey
///   2. AutoNexus: hard HP-floor threshold
///   3. AutoPotion: per-feature enable + percentage thresholds
///   4. Notification preferences (splash / balloons)
/// </summary>
internal class SettingsForm : Form
{
    private readonly Settings _settings;

    // ---- Master + hotkey ----
    private CheckBox _chkEnabled  = null!;
    private CheckBox _chkCtrl     = null!;
    private CheckBox _chkShift    = null!;
    private CheckBox _chkAlt      = null!;
    private TextBox  _txtKey      = null!;

    // ---- AutoNexus ----
    private NumericUpDown _numPct   = null!;   // legacy; hidden but kept for json compat
    private NumericUpDown _numFloor = null!;

    // ---- AutoPotion ----
    private CheckBox _chkHpPot       = null!;
    private NumericUpDown _numHpPotPct = null!;
    private CheckBox _chkMpPot       = null!;
    private NumericUpDown _numMpPotPct = null!;

    // ---- Notifications ----
    private CheckBox _chkSplash   = null!;
    private CheckBox _chkBalloon  = null!;

    public SettingsForm(Settings settings)
    {
        _settings = settings;
        BuildUi();
    }

    private void BuildUi()
    {
        Text = "AutoNexus + AutoPotion Settings — by PurBler";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(460, 430);
        BackColor = Color.FromArgb(13, 17, 23);
        ForeColor = Color.FromArgb(201, 209, 217);
        Font = new Font("Segoe UI", 9F);

        int y = 12;

        // ============ 1. Master toggle ============
        _chkEnabled = NewCheck("Master enabled (AutoNexus + AutoPotion)",
                               _settings.Enabled, 12, y);
        y += 32;

        // ============ 2. Hotkey row ============
        AddLabel("Hotkey:", 12, y + 4);
        _txtKey = new TextBox
        {
            Text = _settings.HotKey,
            Location = new Point(80, y),
            Width = 80,
            ReadOnly = true,
            BackColor = Color.FromArgb(22, 27, 34),
            ForeColor = Color.FromArgb(88, 166, 255),
            BorderStyle = BorderStyle.FixedSingle,
            TextAlign = HorizontalAlignment.Center,
        };
        _txtKey.GotFocus  += (_, _) => _txtKey.BackColor = Color.FromArgb(45, 90, 50);
        _txtKey.LostFocus += (_, _) => _txtKey.BackColor = Color.FromArgb(22, 27, 34);
        _txtKey.KeyDown   += (_, e) =>
        {
            if (e.KeyCode == Keys.ControlKey
                || e.KeyCode == Keys.ShiftKey
                || e.KeyCode == Keys.Menu) return;
            _txtKey.Text = e.KeyCode.ToString();
            e.SuppressKeyPress = true;
        };
        Controls.Add(_txtKey);

        _chkCtrl  = NewCheck("Ctrl",  _settings.HotKeyCtrl,  170, y + 2);
        _chkShift = NewCheck("Shift", _settings.HotKeyShift, 230, y + 2);
        _chkAlt   = NewCheck("Alt",   _settings.HotKeyAlt,   295, y + 2);
        y += 36;

        AddLabel("Click the textbox above and press any key to bind.",
            12, y, italic: true, dim: true);
        y += 28;

        // ============ 3. AutoNexus section ============
        AddSectionDivider("AutoNexus", 12, y);
        y += 24;

        AddLabel("Escape when HP ≤", 12, y + 4);
        _numFloor = new NumericUpDown
        {
            Location = new Point(150, y),
            Width = 80,
            Minimum = 1, Maximum = 99999,
            Value = Math.Clamp(_settings.HpHardFloor, 1, 99999),
            BackColor = Color.FromArgb(22, 27, 34),
            ForeColor = Color.FromArgb(201, 209, 217),
            Increment = 10,
        };
        Controls.Add(_numFloor);
        AddLabel("HP", 235, y + 4);
        y += 28;

        AddLabel("Default 100. Higher = safer (escapes earlier).",
            12, y, italic: true, dim: true);
        y += 28;

        // _numPct stays for binary-compat with older settings.json files
        _numPct = new NumericUpDown { Visible = false };
        Controls.Add(_numPct);

        // ============ 4. AutoPotion section ============
        AddSectionDivider("AutoPotion", 12, y);
        y += 24;

        // --- HP pot row ---
        _chkHpPot = NewCheck("Auto-use HEALTH potion below",
                              _settings.HpPotEnabled, 12, y + 4);
        _numHpPotPct = new NumericUpDown
        {
            Location = new Point(280, y),
            Width = 60,
            Minimum = 5, Maximum = 95,
            Value = Math.Clamp(_settings.HpPotThresholdPercent, 5, 95),
            BackColor = Color.FromArgb(22, 27, 34),
            ForeColor = Color.FromArgb(201, 209, 217),
            Increment = 5,
        };
        Controls.Add(_numHpPotPct);
        AddLabel("%", 345, y + 4);
        y += 28;

        // --- MP pot row ---
        _chkMpPot = NewCheck("Auto-use MANA potion below",
                              _settings.MpPotEnabled, 12, y + 4);
        _numMpPotPct = new NumericUpDown
        {
            Location = new Point(280, y),
            Width = 60,
            Minimum = 5, Maximum = 95,
            Value = Math.Clamp(_settings.MpPotThresholdPercent, 5, 95),
            BackColor = Color.FromArgb(22, 27, 34),
            ForeColor = Color.FromArgb(201, 209, 217),
            Increment = 5,
        };
        Controls.Add(_numMpPotPct);
        AddLabel("%", 345, y + 4);
        y += 28;

        AddLabel("Uses the same HP/MP pot the Q/W keybind would use.",
            12, y, italic: true, dim: true);
        y += 30;

        // ============ 5. Notification preferences ============
        _chkSplash  = NewCheck("Show splash on game launch",
                               _settings.ShowSplashOnLoad, 12, y);
        y += 24;
        _chkBalloon = NewCheck("Show balloon when AutoNexus saves you",
                               _settings.ShowSaveBalloons, 12, y);
        y += 36;

        // ============ 6. Save / Cancel buttons (flush-right) ============
        var btnOk = new Button
        {
            Text = "Save",
            Location = new Point(256, y),
            Width = 90, Height = 30,
            BackColor = Color.FromArgb(35, 134, 54),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
        };
        btnOk.Click += (_, _) => { Apply(); DialogResult = DialogResult.OK; Close(); };
        Controls.Add(btnOk);

        var btnCancel = new Button
        {
            Text = "Cancel",
            Location = new Point(358, y),
            Width = 90, Height = 30,
            BackColor = Color.FromArgb(33, 38, 45),
            ForeColor = Color.FromArgb(201, 209, 217),
            FlatStyle = FlatStyle.Flat,
        };
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(btnCancel);
    }

    private void Apply()
    {
        _settings.Enabled               = _chkEnabled.Checked;
        _settings.HotKey                = _txtKey.Text.Trim();
        _settings.HotKeyCtrl            = _chkCtrl.Checked;
        _settings.HotKeyShift           = _chkShift.Checked;
        _settings.HotKeyAlt             = _chkAlt.Checked;
        _settings.HpThresholdPercent    = (int)_numPct.Value;       // legacy, hidden
        _settings.HpHardFloor           = (int)_numFloor.Value;
        _settings.HpPotEnabled          = _chkHpPot.Checked;
        _settings.HpPotThresholdPercent = (int)_numHpPotPct.Value;
        _settings.MpPotEnabled          = _chkMpPot.Checked;
        _settings.MpPotThresholdPercent = (int)_numMpPotPct.Value;
        _settings.ShowSplashOnLoad      = _chkSplash.Checked;
        _settings.ShowSaveBalloons      = _chkBalloon.Checked;
        _settings.Save();
        HotkeyHook.Update(_settings);
        Notifier.Log(
            $"Settings saved. master={_settings.Enabled} hotkey={HotkeyHook.Format()} " +
            $"nexus_floor={_settings.HpHardFloor} " +
            $"hpPot={_settings.HpPotEnabled}@{_settings.HpPotThresholdPercent}% " +
            $"mpPot={_settings.MpPotEnabled}@{_settings.MpPotThresholdPercent}%");
    }

    // ===== UI helpers =========================================================

    private CheckBox NewCheck(string text, bool initial, int x, int y)
    {
        var cb = new CheckBox
        {
            Text = text,
            Checked = initial,
            Location = new Point(x, y),
            AutoSize = true,
            ForeColor = Color.FromArgb(201, 209, 217),
            BackColor = Color.Transparent,
        };
        Controls.Add(cb);
        return cb;
    }

    private void AddLabel(string text, int x, int y, bool italic = false, bool dim = false)
    {
        Controls.Add(new Label
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            ForeColor = dim ? Color.FromArgb(139, 148, 158) : Color.FromArgb(201, 209, 217),
            BackColor = Color.Transparent,
            Font = italic ? new Font(Font, FontStyle.Italic) : Font,
        });
    }

    /// <summary>
    /// Section header — bold-ish title with a horizontal rule beneath
    /// to visually group the next few rows. Spans the form width.
    /// </summary>
    private void AddSectionDivider(string title, int x, int y)
    {
        Controls.Add(new Label
        {
            Text = title,
            Location = new Point(x, y),
            AutoSize = true,
            ForeColor = Color.FromArgb(88, 166, 255),
            BackColor = Color.Transparent,
            Font = new Font(Font.FontFamily, 9.5F, FontStyle.Bold),
        });
        // Underline rule
        Controls.Add(new Label
        {
            Location = new Point(x + 80, y + 9),
            Size = new Size(360, 1),
            BackColor = Color.FromArgb(48, 54, 61),
        });
    }
}
