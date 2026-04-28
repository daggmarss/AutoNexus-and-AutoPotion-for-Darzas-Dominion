using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace AutoNexusHook;

/// <summary>
/// Global low-level keyboard hook. Listens to ALL keypresses regardless
/// of which window has focus (works while playing the game, in chat,
/// menu, anywhere). Fires <see cref="OnTriggered"/> when the configured
/// modifier+key combo is pressed.
///
/// Implementation: WH_KEYBOARD_LL hook on a dedicated thread that owns
/// a message pump. A message pump is required by Windows for low-level
/// hook callbacks to fire — without it the hook is silently inactive.
/// </summary>
internal static class HotkeyHook
{
    public static event Action? OnTriggered;

    // Mutable target keys; can be reconfigured at runtime via Update().
    private static Keys _targetKey = Keys.F10;
    private static bool _ctrl, _shift, _alt;

    private static IntPtr _hookId = IntPtr.Zero;
    private static LowLevelKeyboardProc? _proc;
    private static Thread? _thread;

    public static void Start(Settings cfg)
    {
        Update(cfg);
        if (_thread != null) return;
        _thread = new Thread(MessagePumpThread)
        {
            IsBackground = true,
            Name = "AutoNexus-Hotkey"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public static void Update(Settings cfg)
    {
        if (!Enum.TryParse<Keys>(cfg.HotKey, ignoreCase: true, out var k)) k = Keys.F10;
        _targetKey = k;
        _ctrl = cfg.HotKeyCtrl;
        _shift = cfg.HotKeyShift;
        _alt = cfg.HotKeyAlt;
        Notifier.Log($"Hotkey set to: {Format()}");
    }

    public static string Format()
    {
        string s = "";
        if (_ctrl)  s += "Ctrl+";
        if (_shift) s += "Shift+";
        if (_alt)   s += "Alt+";
        s += _targetKey.ToString();
        return s;
    }

    private static void MessagePumpThread()
    {
        _proc = HookCallback;
        _hookId = SetHook(_proc);
        if (_hookId == IntPtr.Zero)
        {
            Notifier.LogError($"SetWindowsHookEx failed (err={Marshal.GetLastWin32Error()}). Hotkey will not work.");
            return;
        }
        // Plain Win32 message pump — we don't need a Form here.
        Application.Run();
        UnhookWindowsHookEx(_hookId);
    }

    private static IntPtr SetHook(LowLevelKeyboardProc proc)
    {
        // Module handle = current module. SetWindowsHookEx needs a
        // valid HINSTANCE for WH_KEYBOARD_LL on some Windows builds.
        IntPtr hMod = GetModuleHandle(null);
        return SetWindowsHookEx(WH_KEYBOARD_LL, proc, hMod, 0);
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode == 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                int vkCode = Marshal.ReadInt32(lParam);
                Keys pressed = (Keys)vkCode;
                bool ctrl  = (Control.ModifierKeys & Keys.Control) == Keys.Control;
                bool shift = (Control.ModifierKeys & Keys.Shift)   == Keys.Shift;
                bool alt   = (Control.ModifierKeys & Keys.Alt)     == Keys.Alt;
                if (pressed == _targetKey && ctrl == _ctrl && shift == _shift && alt == _alt)
                {
                    try { OnTriggered?.Invoke(); }
                    catch (Exception ex) { Notifier.LogError($"Hotkey handler crashed: {ex.Message}"); }
                }
            }
        }
        catch { /* never throw out of a hook callback */ }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    // ===== Win32 P/Invoke =====
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN     = 0x0100;
    private const int WM_SYSKEYDOWN  = 0x0104;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
