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
    public bool   Enabled            { get; set; } = true;
    public string HotKey             { get; set; } = "F10";   // any Keys enum name
    public bool   HotKeyCtrl         { get; set; } = false;
    public bool   HotKeyShift        { get; set; } = false;
    public bool   HotKeyAlt          { get; set; } = false;
    public int    HpThresholdPercent { get; set; } = 50;       // legacy, no longer used
    public int    HpHardFloor        { get; set; } = 100;      // PRIMARY threshold: escape when HP <= this (flat HP)
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
