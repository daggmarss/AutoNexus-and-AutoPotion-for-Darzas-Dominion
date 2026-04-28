using System;
using System.Drawing;
using System.Windows.Forms;

namespace AutoNexusHook;

/// <summary>
/// Compact settings dialog. Lets the user:
///   • change the toggle hotkey (with modifier checkboxes)
///   • set HP threshold percent and hard floor
///   • toggle splash & balloon notifications
///   • disable/enable AutoNexus from here too
///
/// Opening: tray menu → Settings.
/// </summary>
internal class SettingsForm : Form
{
    private readonly Settings _settings;

    private CheckBox _chkEnabled  = null!;
    private CheckBox _chkCtrl     = null!;
    private CheckBox _chkShift    = null!;
    private CheckBox _chkAlt      = null!;
    private TextBox  _txtKey      = null!;
    private NumericUpDown _numPct = null!;
    private NumericUpDown _numFloor = null!;
    private CheckBox _chkSplash   = null!;
    private CheckBox _chkBalloon  = null!;

    public SettingsForm(Settings settings)
    {
        _settings = settings;
        BuildUi();
    }

    private void BuildUi()
    {
        Text = "AutoNexus Settings — by PurBler";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(420, 340);
        BackColor = Color.FromArgb(13, 17, 23);
        ForeColor = Color.FromArgb(201, 209, 217);
        Font = new Font("Segoe UI", 9F);

        int y = 12;

        _chkEnabled = NewCheck("AutoNexus enabled", _settings.Enabled, 12, y);
        y += 28;

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
        _txtKey.GotFocus += (_, _) => _txtKey.BackColor = Color.FromArgb(45, 90, 50);
        _txtKey.LostFocus += (_, _) => _txtKey.BackColor = Color.FromArgb(22, 27, 34);
        _txtKey.KeyDown += (_, e) =>
        {
            // Capture the next physical key as the hotkey.
            // Allow F-keys, letters, digits, etc. Skip modifier-only.
            if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.Menu)
                return;
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
        y += 30;

        AddLabel("Default 100. Higher = safer (escapes earlier).", 12, y, italic: true, dim: true);
        y += 28;

        // _numPct stays in field but isn't shown — kept for binary
        // compat with the Settings.json file format.
        _numPct = new NumericUpDown { Visible = false };
        Controls.Add(_numPct);

        _chkSplash  = NewCheck("Show splash on game launch", _settings.ShowSplashOnLoad, 12, y);
        y += 24;
        _chkBalloon = NewCheck("Show balloon when AutoNexus saves you", _settings.ShowSaveBalloons, 12, y);
        y += 36;

        var btnOk = new Button
        {
            Text = "Save",
            Location = new Point(220, y),
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
            Location = new Point(316, y),
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
        _settings.Enabled            = _chkEnabled.Checked;
        _settings.HotKey             = _txtKey.Text.Trim();
        _settings.HotKeyCtrl         = _chkCtrl.Checked;
        _settings.HotKeyShift        = _chkShift.Checked;
        _settings.HotKeyAlt          = _chkAlt.Checked;
        _settings.HpThresholdPercent = (int)_numPct.Value;
        _settings.HpHardFloor        = (int)_numFloor.Value;
        _settings.ShowSplashOnLoad   = _chkSplash.Checked;
        _settings.ShowSaveBalloons   = _chkBalloon.Checked;
        _settings.Save();
        HotkeyHook.Update(_settings);
        Notifier.Log($"Settings saved. Enabled={_settings.Enabled} hotkey={HotkeyHook.Format()} pct={_settings.HpThresholdPercent} floor={_settings.HpHardFloor}");
    }

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
            Font = italic
                ? new Font(Font, FontStyle.Italic)
                : Font,
        });
    }
}
