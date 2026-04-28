using System;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace AutoNexusHook;

/// <summary>
/// Background watcher: every 200 ms checks the local player's HP via
/// reflection on the loaded game assemblies. When HP drops below
/// <see cref="HpThresholdPercent"/> of MaxHp it constructs a GmEscape
/// packet and sends it through the game's network client — same code
/// path the rendered client uses when you press F5.
///
/// All access is reflection-based so the hook works against any build
/// of DarzasDominion that keeps the public surface (Player.Health,
/// Player.MaxHealth, Client.SendPacket(GmPacket), GmEscape).
/// </summary>
internal static class NexusEngine
{
    // Defaults moved into Settings.cs — these are kept as fallback
    // values only.
    private const int EscapeCooldownMs = 4000;
    private static int _lastEscapeTickCount = -100000;

    public static void Run()
    {
        // Wait until the game's assemblies are loaded.
        Type? worldType = null;
        Type? playerType = null;
        Type? clientType = null;
        Type? gmEscapeType = null;
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            worldType    ??= FindType("DarzasDominion.Darza.Game.World") ?? FindType("World");
            playerType   ??= FindType("DarzasDominion.Darza.Game.Objects.Entities.Player") ?? FindType("Player");
            clientType   ??= FindType("DarzasDominion.Darza.Networking.Client") ?? FindType("Client");
            gmEscapeType ??= FindType("DarzaGameNet.Packets.Game.Client.GmEscape") ?? FindType("GmEscape");
            if (worldType != null && playerType != null && clientType != null && gmEscapeType != null) break;
            Thread.Sleep(500);
        }

        if (worldType == null || playerType == null || clientType == null || gmEscapeType == null)
        {
            Notifier.LogError(
                $"Cannot resolve game types after 60s. " +
                $"World={worldType != null}, Player={playerType != null}, Client={clientType != null}, GmEscape={gmEscapeType != null}");
            return;
        }

        // World.Shared (static field) → World.Player (instance field)
        // → Player. This is the actual game pattern (verified in
        // FogPlane.cs:121 etc).
        FieldInfo? worldShared = worldType.GetField("Shared",
            BindingFlags.Public | BindingFlags.Static);
        FieldInfo? worldPlayer = playerType == null ? null : worldType.GetField("Player",
            BindingFlags.Public | BindingFlags.Instance);
        if (worldShared == null || worldPlayer == null)
        {
            Notifier.LogError($"World.Shared={worldShared != null} World.Player={worldPlayer != null} not found");
            return;
        }

        // Player has Health + MaxHealth (instance fields, NOT props in
        // some builds — try both).
        MemberInfo? hpMember    = (MemberInfo?)playerType.GetField("Health",    BindingFlags.Public | BindingFlags.Instance)
                                ?? (MemberInfo?)playerType.GetProperty("Health", BindingFlags.Public | BindingFlags.Instance);
        MemberInfo? maxHpMember = (MemberInfo?)playerType.GetField("MaxHealth", BindingFlags.Public | BindingFlags.Instance)
                                ?? (MemberInfo?)playerType.GetProperty("MaxHealth", BindingFlags.Public | BindingFlags.Instance);
        if (hpMember == null || maxHpMember == null)
        {
            Notifier.LogError($"Player.Health={hpMember != null} Player.MaxHealth={maxHpMember != null} not found");
            return;
        }

        // Client.SendPacket(GmPacket packet, Action callback = null).
        // The second parameter is optional with a default value, so we
        // must call it with TWO args (packet + null) — and we accept
        // any overload of SendPacket that takes a packet first.
        MethodInfo? sendPacket = clientType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == "SendPacket" && m.GetParameters().Length >= 1)
            .OrderBy(m => m.GetParameters().Length)
            .FirstOrDefault();
        if (sendPacket == null)
        {
            Notifier.LogError("Client.SendPacket not found (no static method named SendPacket)");
            return;
        }
        int sendPacketArgCount = sendPacket.GetParameters().Length;
        Notifier.Log($"Client.SendPacket resolved with {sendPacketArgCount} parameters");

        Notifier.Log($"Resolved game types — entering watch loop. " +
                     $"hpMember={hpMember.MemberType}, maxHpMember={maxHpMember.MemberType}");

        bool firstLogged = false;
        int loggedHp = -1;
        DateTime lastDebugLog = DateTime.MinValue;

        while (true)
        {
            try
            {
                object? world = worldShared.GetValue(null);
                if (world == null) { Thread.Sleep(500); continue; }
                object? player = worldPlayer.GetValue(world);
                if (player == null) { Thread.Sleep(500); continue; }

                int hp    = ReadInt(hpMember, player);
                int maxHp = ReadInt(maxHpMember, player);
                if (maxHp <= 0 || hp <= 0) { Thread.Sleep(200); continue; }

                // Periodic HP log so we can see we're polling.
                if (!firstLogged || (DateTime.UtcNow - lastDebugLog).TotalSeconds > 30)
                {
                    Notifier.Log($"Polling HP={hp}/{maxHp} (threshold={Notifier.HpFloor}, enabled={Notifier.IsEnabled})");
                    firstLogged = true; lastDebugLog = DateTime.UtcNow; loggedHp = hp;
                }

                if (!Notifier.IsEnabled)
                {
                    Thread.Sleep(300);
                    continue;
                }

                // ABSOLUTE HP THRESHOLD — user requested switch from %
                // to flat number. HpFloor is now the ONLY threshold.
                // Default 100 = escape any time HP <= 100.
                int threshold = Notifier.HpFloor;
                if (hp <= threshold && Environment.TickCount - _lastEscapeTickCount > EscapeCooldownMs)
                {
                    _lastEscapeTickCount = Environment.TickCount;
                    SendEscape(sendPacket, gmEscapeType, sendPacketArgCount);
                    Thread.Sleep(80);
                    SendEscape(sendPacket, gmEscapeType, sendPacketArgCount);
                    Thread.Sleep(80);
                    SendEscape(sendPacket, gmEscapeType, sendPacketArgCount);
                    Notifier.NotifySaved(hp, maxHp);
                }
            }
            catch (TargetInvocationException tie)
            {
                Notifier.LogError($"Reflection invoke failed: {tie.InnerException?.Message}");
            }
            catch (Exception ex)
            {
                Notifier.LogError($"Watch loop: {ex.Message}");
            }
            Thread.Sleep(150);
        }
    }

    /// <summary>
    /// Build the args array sized correctly for the SendPacket overload
    /// we resolved (some builds: 1 arg packet; others: packet + optional
    /// callback). For optional params we pass null / Type.Missing so
    /// the runtime fills in defaults.
    /// </summary>
    private static void SendEscape(MethodInfo sendPacket, Type gmEscapeType, int argCount)
    {
        var args = new object?[argCount];
        args[0] = Activator.CreateInstance(gmEscapeType);
        for (int i = 1; i < argCount; i++)
            args[i] = Type.Missing;  // tells reflection "use default value"
        sendPacket.Invoke(null, args);
    }

    private static int ReadInt(MemberInfo m, object obj)
    {
        object? raw = m switch
        {
            FieldInfo f    => f.GetValue(obj),
            PropertyInfo p => p.GetValue(obj),
            _              => null,
        };
        if (raw == null) return 0;
        try { return Convert.ToInt32(raw); } catch { return 0; }
    }

    /// <summary>
    /// Search every currently-loaded assembly for a type with the
    /// given fully-qualified name. Many obfuscated builds rename
    /// inner types but keep public top-level names.
    /// </summary>
    private static Type? FindType(string fullName)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                Type? t = asm.GetType(fullName, throwOnError: false);
                if (t != null) return t;
                // Fallback: short-name match across all exported types.
                foreach (var ex in asm.GetTypes())
                {
                    if (ex.FullName == fullName || ex.Name == fullName.Split('.').Last())
                        return ex;
                }
            }
            catch { /* dynamic asm with no GetTypes — skip */ }
        }
        return null;
    }
}
