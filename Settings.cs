using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoNexusHook;

/// <summary>
/// User-editable runtime config. Persisted to
/// %APPDATA%\AutoNexusHook\settings.json so changes survive game
/// restarts. Defaults match the original AutoNexus behaviour
/// (50 % threshold, F10 toggle, enabled at startup).
/// </summary>
public class Settings
{
    // ===== Master toggle (governs BOTH AutoNexus and AutoPotion) =====
    public bool   Enabled            { get; set; } = true;
    public string HotKey             { get; set; } = "F10";   // any Keys enum name
    public bool   HotKeyCtrl         { get; set; } = false;
    public bool   HotKeyShift        { get; set; } = false;
    public bool   HotKeyAlt          { get; set; } = false;

    // ===== AutoNexus =====
    public int    HpThresholdPercent { get; set; } = 50;       // legacy, no longer used
    public int    HpHardFloor        { get; set; } = 100;      // PRIMARY threshold: escape when HP <= this (flat HP)

    // ===== AutoPotion (added in v1.1.0) =====
    // Per-feature on/off so the user can run JUST AutoNexus, JUST
    // AutoPot, or both. Both default to enabled for HP (most-wanted)
    // and disabled for MP (mana usage is class-strategic — auto-pot
    // can waste pots on overflow for some classes).
    public bool   HpPotEnabled              { get; set; } = true;
    public int    HpPotThresholdPercent     { get; set; } = 70;   // drink HP pot when HP% <= this
    public bool   MpPotEnabled              { get; set; } = false;
    public int    MpPotThresholdPercent     { get; set; } = 40;   // drink MP pot when MP% <= this

    // ===== UI / notifications =====
    public bool   ShowSplashOnLoad   { get; set; } = true;
    public bool   ShowSaveBalloons   { get; set; } = true;

    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AutoNexusHook");
    private static readonly string ConfigPath = Path.Combine(ConfigDir, "settings.json");
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var loaded = JsonSerializer.Deserialize<Settings>(json, JsonOpts);
                if (loaded != null) return loaded;
            }
        }
        catch (Exception ex)
        {
            Notifier.LogError($"Settings.Load failed, using defaults: {ex.Message}");
        }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, JsonOpts));
        }
        catch (Exception ex)
        {
            Notifier.LogError($"Settings.Save failed: {ex.Message}");
        }
    }
}
