using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace AutoNexusHook;

/// <summary>
/// Background watcher that auto-drinks health / mana potions when the
/// player's HP/MP drops below a configurable percentage threshold.
///
/// Sends the same <c>GmUseItem</c> packet the rendered client builds
/// when the user presses the Q (HP pot) or W (MP pot) keybind. The
/// inventory slot is found via <c>Player.PotionIndex</c> /
/// <c>Player.ManaPotionIndex</c> — the game's own pre-cached "next
/// potion" slot lookup, same one the keybind handler uses.
///
/// All access is reflection-based so the hook keeps working against
/// any build of DarzasDominion that keeps the public surface
/// (<c>Player.Health</c>, <c>Player.PotionIndex</c>,
/// <c>Player.Slots</c>, <c>Client.SendPacket</c>, <c>GmUseItem</c>,
/// <c>GamePoint</c>).
///
/// Self-imposed 1.5 s cooldown between uses — the client itself has
/// no built-in pot cooldown (server enforces) but spamming would
/// look suspicious in network logs.
/// </summary>
internal static class PotionEngine
{
    private const int PollIntervalMs = 180;
    // Real players spam Q/W much faster than 1.5s — matching that here.
    // 500 ms = 2 pots/second, roughly the server-side rate limit. Still
    // long enough that we don't waste a pot before the previous one's
    // heal has propagated back from the server (typical roundtrip ~200 ms).
    private const int CooldownMs     = 500;

    private static int _lastHpUseTickCount = -100000;
    private static int _lastMpUseTickCount = -100000;

    public static void Run()
    {
        // === Resolve game types (with retry while the game loads) =========
        Type? worldType = null, playerType = null, clientType = null;
        Type? gmUseItemType = null, gamePointType = null;
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            worldType     ??= FindType("DarzasDominion.Darza.Game.World") ?? FindType("World");
            playerType    ??= FindType("DarzasDominion.Darza.Game.Objects.Entities.Player") ?? FindType("Player");
            clientType    ??= FindType("DarzasDominion.Darza.Networking.Client") ?? FindType("Client");
            gmUseItemType ??= FindType("DarzaGameNet.Packets.Game.Client.GmUseItem") ?? FindType("GmUseItem");
            gamePointType ??= FindType("DarzaGameNet.Geometry.GamePoint") ?? FindType("GamePoint");
            if (worldType != null && playerType != null && clientType != null
                && gmUseItemType != null && gamePointType != null) break;
            Thread.Sleep(500);
        }

        if (worldType == null || playerType == null || clientType == null
            || gmUseItemType == null || gamePointType == null)
        {
            Notifier.LogError(
                $"PotionEngine: cannot resolve types after 60s. " +
                $"World={worldType != null}, Player={playerType != null}, " +
                $"Client={clientType != null}, GmUseItem={gmUseItemType != null}, " +
                $"GamePoint={gamePointType != null}");
            return;
        }

        // World.Shared (static) → World.Player (instance) — same chain as NexusEngine
        FieldInfo? worldShared = worldType.GetField("Shared",
            BindingFlags.Public | BindingFlags.Static);
        FieldInfo? worldPlayer = worldType.GetField("Player",
            BindingFlags.Public | BindingFlags.Instance);
        if (worldShared == null || worldPlayer == null)
        {
            Notifier.LogError("PotionEngine: World.Shared or World.Player not found");
            return;
        }

        // World.ClientTime — needed for GmUseItem.Time stamp
        MemberInfo? clientTimeMember = ResolveMember(worldType, "ClientTime");

        // Player members (stat values + cached potion-slot indices + slot list + pos)
        MemberInfo? hpMember     = ResolveMember(playerType, "Health");
        MemberInfo? maxHpMember  = ResolveMember(playerType, "MaxHealth");
        MemberInfo? mpMember     = ResolveMember(playerType, "Mana");
        MemberInfo? maxMpMember  = ResolveMember(playerType, "MaxMana");
        MemberInfo? hpPotIdxMem  = ResolveMember(playerType, "PotionIndex");
        MemberInfo? mpPotIdxMem  = ResolveMember(playerType, "ManaPotionIndex");
        MemberInfo? slotsMember  = ResolveMember(playerType, "Slots");
        MemberInfo? posXMember   = ResolveMember(playerType, "X");      // may be null
        MemberInfo? posYMember   = ResolveMember(playerType, "Y");      // may be null
        MemberInfo? posMember    = ResolveMember(playerType, "Position"); // fallback

        if (hpMember == null || maxHpMember == null
            || mpMember == null || maxMpMember == null
            || hpPotIdxMem == null || mpPotIdxMem == null || slotsMember == null)
        {
            Notifier.LogError(
                $"PotionEngine: missing Player members. " +
                $"HP={hpMember != null}, MaxHP={maxHpMember != null}, " +
                $"MP={mpMember != null}, MaxMP={maxMpMember != null}, " +
                $"PotionIndex={hpPotIdxMem != null}, ManaPotionIndex={mpPotIdxMem != null}, " +
                $"Slots={slotsMember != null}");
            return;
        }
        if (posXMember == null && posMember == null)
        {
            Notifier.LogError("PotionEngine: Player has neither X/Y nor Position member");
            return;
        }

        // Client.SendPacket — same lookup as NexusEngine
        MethodInfo? sendPacket = clientType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == "SendPacket" && m.GetParameters().Length >= 1)
            .OrderBy(m => m.GetParameters().Length)
            .FirstOrDefault();
        if (sendPacket == null)
        {
            Notifier.LogError("PotionEngine: Client.SendPacket not found");
            return;
        }
        int sendPacketArgCount = sendPacket.GetParameters().Length;

        // GamePoint(float, float) ctor — preferred 2-float ctor, fallback to any 2-arg
        ConstructorInfo? gamePointCtor =
            gamePointType.GetConstructor(new[] { typeof(float), typeof(float) })
            ?? gamePointType.GetConstructors()
                .FirstOrDefault(c => c.GetParameters().Length == 2);
        if (gamePointCtor == null)
        {
            Notifier.LogError("PotionEngine: GamePoint(float,float) constructor not found");
            return;
        }

        Notifier.Log("PotionEngine: types resolved, entering watch loop");

        // === Watch loop ====================================================
        DateTime lastDebugLog = DateTime.MinValue;
        while (true)
        {
            try
            {
                // Master gate (same hotkey-toggle as AutoNexus uses)
                if (!Notifier.IsEnabled)
                {
                    Thread.Sleep(400);
                    continue;
                }
                // Per-feature gates — if both off, sleep longer
                if (!Notifier.HpPotEnabled && !Notifier.MpPotEnabled)
                {
                    Thread.Sleep(400);
                    continue;
                }

                object? world = worldShared.GetValue(null);
                if (world == null) { Thread.Sleep(500); continue; }
                object? player = worldPlayer.GetValue(world);
                if (player == null) { Thread.Sleep(500); continue; }

                int hp    = ReadInt(hpMember, player);
                int maxHp = ReadInt(maxHpMember, player);
                int mp    = ReadInt(mpMember, player);
                int maxMp = ReadInt(maxMpMember, player);
                if (maxHp <= 0) { Thread.Sleep(200); continue; }

                if ((DateTime.UtcNow - lastDebugLog).TotalSeconds > 30)
                {
                    Notifier.Log(
                        $"AutoPot poll HP={hp}/{maxHp} MP={mp}/{maxMp} " +
                        $"(hp_on={Notifier.HpPotEnabled}@{Notifier.HpPotThresholdPercent}%, " +
                        $"mp_on={Notifier.MpPotEnabled}@{Notifier.MpPotThresholdPercent}%)");
                    lastDebugLog = DateTime.UtcNow;
                }

                // ---- HP potion ----
                if (Notifier.HpPotEnabled
                    && Environment.TickCount - _lastHpUseTickCount > CooldownMs)
                {
                    int hpPct = (hp * 100) / Math.Max(1, maxHp);
                    if (hpPct <= Notifier.HpPotThresholdPercent)
                    {
                        int idx = ReadInt(hpPotIdxMem, player);
                        if (TryUsePotion(world, player, idx, slotsMember, posXMember,
                                         posYMember, posMember, clientTimeMember,
                                         gmUseItemType, gamePointCtor,
                                         sendPacket, sendPacketArgCount))
                        {
                            _lastHpUseTickCount = Environment.TickCount;
                            Notifier.Log($"AutoPot: HP pot @ {hp}/{maxHp} ({hpPct}%)");
                        }
                    }
                }

                // ---- MP potion ----
                if (Notifier.MpPotEnabled
                    && maxMp > 0
                    && Environment.TickCount - _lastMpUseTickCount > CooldownMs)
                {
                    int mpPct = (mp * 100) / Math.Max(1, maxMp);
                    if (mpPct <= Notifier.MpPotThresholdPercent)
                    {
                        int idx = ReadInt(mpPotIdxMem, player);
                        if (TryUsePotion(world, player, idx, slotsMember, posXMember,
                                         posYMember, posMember, clientTimeMember,
                                         gmUseItemType, gamePointCtor,
                                         sendPacket, sendPacketArgCount))
                        {
                            _lastMpUseTickCount = Environment.TickCount;
                            Notifier.Log($"AutoPot: MP pot @ {mp}/{maxMp} ({mpPct}%)");
                        }
                    }
                }
            }
            catch (TargetInvocationException tie)
            {
                Notifier.LogError($"PotionEngine reflection invoke failed: {tie.InnerException?.Message}");
            }
            catch (Exception ex)
            {
                Notifier.LogError($"PotionEngine watch loop: {ex.Message}");
            }
            Thread.Sleep(PollIntervalMs);
        }
    }

    /// <summary>
    /// Build a <c>GmUseItem</c> packet for the slot index returned by
    /// PotionIndex/ManaPotionIndex and send it through Client.SendPacket.
    /// Returns false (silently) if the player has no potion in inventory
    /// or the slot can't be resolved.
    /// </summary>
    private static bool TryUsePotion(
        object world, object player, int slotIdx, MemberInfo slotsMember,
        MemberInfo? posXMember, MemberInfo? posYMember, MemberInfo? posMember,
        MemberInfo? clientTimeMember, Type gmUseItemType,
        ConstructorInfo gamePointCtor,
        MethodInfo sendPacket, int sendPacketArgCount)
    {
        if (slotIdx < 0) return false;  // no potion of this kind in inventory

        // Player.Slots[idx]
        object? slotsObj = ReadMember(slotsMember, player);
        if (slotsObj is not IList slots) return false;
        if (slotIdx >= slots.Count) return false;
        object? slot = slots[slotIdx];
        if (slot == null) return false;

        // slot.Id (byte) and slot.OwnerId (int) — same fields the client uses
        Type slotType = slot.GetType();
        MemberInfo? slotIdMem      = ResolveMember(slotType, "Id");
        MemberInfo? slotOwnerIdMem = ResolveMember(slotType, "OwnerId");
        if (slotIdMem == null || slotOwnerIdMem == null) return false;
        byte slotId  = (byte)ReadInt(slotIdMem, slot);
        int  ownerId = ReadInt(slotOwnerIdMem, slot);

        // Player position — try X/Y direct first, fall back to Position.X/.Y
        (float px, float py) = ReadPlayerXY(player, posXMember, posYMember, posMember);

        // new GamePoint(px, py) — used for both From and Target (self-target)
        object pos = gamePointCtor.Invoke(new object[] { px, py });

        // World.ClientTime → GmUseItem.Time (int in v1.x of Darza). Read as
        // int — SetMember will convert to whatever the actual field type is.
        int clientTime = 0;
        if (clientTimeMember != null)
        {
            object? ct = ReadMember(clientTimeMember, world);
            if (ct != null)
            {
                try { clientTime = Convert.ToInt32(ct); } catch { /* leave 0 */ }
            }
        }

        // new GmUseItem { Time, SlotId, From, Target, GameId }
        object packet = Activator.CreateInstance(gmUseItemType)!;
        SetMember(packet, "Time",   clientTime);
        SetMember(packet, "SlotId", slotId);
        SetMember(packet, "From",   pos);
        SetMember(packet, "Target", pos);
        SetMember(packet, "GameId", ownerId);

        // Client.SendPacket(packet [, callback])
        var args = new object?[sendPacketArgCount];
        args[0] = packet;
        for (int i = 1; i < sendPacketArgCount; i++) args[i] = Type.Missing;
        sendPacket.Invoke(null, args);
        return true;
    }

    /// <summary>
    /// Best-effort read of (X,Y) from the player. Tries direct
    /// <c>player.X</c> / <c>player.Y</c> first (most builds expose these),
    /// then falls back to <c>player.Position</c> (Vector2/Vector3 with
    /// .X / .Y). Returns (0,0) on failure — server still accepts the
    /// packet but the position-validation may flag it.
    /// </summary>
    private static (float, float) ReadPlayerXY(
        object player, MemberInfo? xMem, MemberInfo? yMem, MemberInfo? posMem)
    {
        if (xMem != null && yMem != null)
            return (ReadFloat(xMem, player), ReadFloat(yMem, player));

        if (posMem != null)
        {
            object? pos = ReadMember(posMem, player);
            if (pos != null)
            {
                Type t = pos.GetType();
                MemberInfo? mx = ResolveMember(t, "X");
                MemberInfo? my = ResolveMember(t, "Y");
                if (mx != null && my != null)
                    return (ReadFloat(mx, pos), ReadFloat(my, pos));
            }
        }
        return (0f, 0f);
    }

    // ===== Reflection helpers =================================================

    private static MemberInfo? ResolveMember(Type t, string name)
    {
        // Search BOTH instance and static. Darza's Player.Slots is a public
        // STATIC field (`public static Slot[] Slots;` in Player.cs:729) —
        // missing the Static flag is what made the v1.1.0-rc1 hook fail
        // with "Slots=False" in the log. For static fields, GetValue(obj)
        // ignores obj, so the rest of the code works unchanged.
        const BindingFlags flags = BindingFlags.Public
                                 | BindingFlags.Instance
                                 | BindingFlags.Static;
        return (MemberInfo?)t.GetField(name, flags)
             ?? (MemberInfo?)t.GetProperty(name, flags);
    }

    private static object? ReadMember(MemberInfo m, object obj) => m switch
    {
        FieldInfo f    => f.GetValue(obj),
        PropertyInfo p => p.GetValue(obj),
        _              => null,
    };

    private static int ReadInt(MemberInfo m, object obj)
    {
        object? raw = ReadMember(m, obj);
        if (raw == null) return 0;
        try { return Convert.ToInt32(raw); } catch { return 0; }
    }

    private static float ReadFloat(MemberInfo m, object obj)
    {
        object? raw = ReadMember(m, obj);
        if (raw == null) return 0f;
        try { return Convert.ToSingle(raw); } catch { return 0f; }
    }

    /// <summary>
    /// Set <paramref name="name"/> on <paramref name="obj"/> to
    /// <paramref name="value"/>, with automatic numeric conversion
    /// to match the destination field/property type. Reflection's
    /// <c>SetValue</c> is strict — passing a <c>long</c> to an
    /// <c>int</c> field throws "Int64 cannot be converted to Int32".
    /// We do the cast ourselves so the caller doesn't have to know
    /// the exact destination type. Catches the common int↔long↔byte
    /// mismatches that vary between game-build packet revisions.
    /// </summary>
    private static void SetMember(object obj, string name, object value)
    {
        Type t = obj.GetType();
        FieldInfo? f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
        if (f != null) { f.SetValue(obj, ConvertToType(value, f.FieldType)); return; }
        PropertyInfo? p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (p != null && p.CanWrite) p.SetValue(obj, ConvertToType(value, p.PropertyType));
    }

    private static object ConvertToType(object value, Type target)
    {
        if (value.GetType() == target) return value;
        if (target.IsPrimitive || target == typeof(decimal))
        {
            try { return Convert.ChangeType(value, target); }
            catch { /* fall through; reflection will throw a clearer error */ }
        }
        return value;
    }

    private static Type? FindType(string fullName)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                Type? t = asm.GetType(fullName, throwOnError: false);
                if (t != null) return t;
                foreach (var ex in asm.GetTypes())
                {
                    if (ex.FullName == fullName || ex.Name == fullName.Split('.').Last())
                        return ex;
                }
            }
            catch { /* dynamic assembly with no GetTypes — skip */ }
        }
        return null;
    }
}
