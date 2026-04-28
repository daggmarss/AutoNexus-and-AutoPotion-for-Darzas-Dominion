using System;
using System.Threading;

internal class StartupHook
{
    public static void Initialize()
    {
        try
        {
            // === Bootstrap thread: settings + hotkey + UI + AutoNexus loop ===
            new Thread(() =>
            {
                try
                {
                    var settings = AutoNexusHook.Settings.Load();
                    AutoNexusHook.HotkeyHook.Start(settings);
                    AutoNexusHook.HotkeyHook.OnTriggered += AutoNexusHook.Notifier.Toggle;
                    AutoNexusHook.Notifier.Init(settings);
                    AutoNexusHook.NexusEngine.Run();
                }
                catch (Exception ex)
                {
                    AutoNexusHook.Notifier.LogError($"AutoNexus thread crashed: {ex}");
                }
            })
            { IsBackground = true, Name = "AutoNexusHook" }.Start();

            // === Parallel thread for AutoPotion engine ===
            // Runs alongside NexusEngine — both poll Player state every
            // ~150–200 ms; cheap and they don't contend for any locks.
            new Thread(() =>
            {
                try
                {
                    AutoNexusHook.PotionEngine.Run();
                }
                catch (Exception ex)
                {
                    AutoNexusHook.Notifier.LogError($"AutoPotion thread crashed: {ex}");
                }
            })
            { IsBackground = true, Name = "AutoPotionHook" }.Start();
        }
        catch (Exception ex)
        {
            try { Console.Error.WriteLine($"[AutoNexusHook] init failed: {ex}"); } catch { }
        }
    }
}
