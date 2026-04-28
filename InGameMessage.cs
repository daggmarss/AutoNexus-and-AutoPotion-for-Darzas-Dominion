using System;
using System.Linq;
using System.Reflection;

namespace AutoNexusHook;

/// <summary>
/// Posts a message into the game's own chat panel (SideChat) so the
/// user sees it on the main HUD — not a Windows tray balloon. Looks
/// like a regular system chat line: name + colored text.
///
/// Reflection chain:
///   World.Shared (static)         — current world instance
///     .GameScene                  — active scene
///       .GameUI                   — UI root
///         .Chat (SideChat)        — chat panel
///           .AddChat(name, text, color, isPlayer, ...)
///
/// All resolution is lazy so a single bad assembly version doesn't
/// kill the whole hook — failed lookups just downgrade to a Console
/// write that goes to the log instead.
/// </summary>
internal static class InGameMessage
{
    private static Type?       _worldType;
    private static FieldInfo?  _worldShared;
    private static Type?       _colorType;
    private static MethodInfo? _addChat;

    /// <summary>RGBA color, default soft pink for the PurBler signature.</summary>
    public static void Post(string text, byte r = 0xFF, byte g = 0x69, byte b = 0xB4)
    {
        try
        {
            EnsureResolved();
            if (_worldShared == null || _addChat == null || _colorType == null)
            {
                Notifier.Log($"[in-game] {text}  (chat unavailable, logged only)");
                return;
            }
            object? world = _worldShared.GetValue(null);
            if (world == null) { Notifier.Log($"[in-game] {text}  (no World yet)"); return; }

            // World.GameScene → GameScene.GameUI → GameUI.Chat
            object? scene  = GetMember(world, "GameScene");
            object? ui     = scene == null ? null : GetMember(scene, "GameUI") ?? GetMember(scene, "UI");
            object? chat   = ui == null    ? null : GetMember(ui, "Chat");
            if (chat == null) { Notifier.Log($"[in-game] {text}  (chat path not resolved)"); return; }

            // XNA/MonoGame Color is a struct: new Color(r,g,b,a).
            object color = Activator.CreateInstance(_colorType, (byte)r, (byte)g, (byte)b, (byte)0xFF)!;

            // Build full args array matching the longest overload we found.
            var ps = _addChat.GetParameters();
            object?[] args = new object?[ps.Length];
            args[0] = "AutoNexus";          // name
            args[1] = text;                 // text
            args[2] = color;                // color
            args[3] = false;                // isPlayer
            for (int i = 4; i < ps.Length; i++)
                args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : DefaultFor(ps[i].ParameterType);

            _addChat.Invoke(chat, args);
        }
        catch (Exception ex)
        {
            Notifier.LogError($"InGameMessage failed: {ex.Message}");
        }
    }

    private static void EnsureResolved()
    {
        if (_worldShared != null && _addChat != null && _colorType != null) return;

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                if (_worldType == null)
                {
                    _worldType = asm.GetType("DarzasDominion.Darza.Game.World", false);
                }
                if (_colorType == null)
                {
                    _colorType = asm.GetType("Microsoft.Xna.Framework.Color", false);
                }
            }
            catch { }
        }
        if (_worldType != null && _worldShared == null)
        {
            _worldShared = _worldType.GetField("Shared", BindingFlags.Public | BindingFlags.Static);
        }

        // Locate SideChat.AddChat by walking any loaded type with that name.
        if (_addChat == null)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var t in asm.GetTypes())
                    {
                        if (t.Name != "SideChat") continue;
                        var m = t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                                 .Where(x => x.Name == "AddChat")
                                 .OrderByDescending(x => x.GetParameters().Length)
                                 .FirstOrDefault();
                        if (m != null)
                        {
                            _addChat = m;
                            break;
                        }
                    }
                }
                catch { }
                if (_addChat != null) break;
            }
        }
    }

    private static object? GetMember(object obj, string name)
    {
        var t = obj.GetType();
        var f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (f != null) return f.GetValue(obj);
        var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        return p?.GetValue(obj);
    }

    private static object? DefaultFor(Type t)
    {
        if (!t.IsValueType) return null;
        return Activator.CreateInstance(t);
    }
}
