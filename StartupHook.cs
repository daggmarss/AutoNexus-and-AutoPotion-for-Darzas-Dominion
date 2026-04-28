using System;
using System.Threading;

internal class StartupHook
{
    public static void Initialize()
    {
        try
        {
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
        }
        catch (Exception ex)
        {
            try { Console.Error.WriteLine($"[AutoNexusHook] init failed: {ex}"); } catch { }
        }
    }
}
