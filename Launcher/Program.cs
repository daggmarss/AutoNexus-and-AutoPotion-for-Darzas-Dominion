using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace AutoNexusLauncher;

/// <summary>
/// One-click launcher: sets the DOTNET_STARTUP_HOOKS env var to point
/// at AutoNexusHook.dll, then starts DarzasDominion.exe with that env
/// inherited. The .NET runtime sees the env var and loads our DLL
/// into the game process automatically — no CreateRemoteThread, no
/// antivirus drama, no admin rights.
///
/// Usage: drop AutoNexusLauncher.exe + AutoNexusHook.dll in the same
/// folder as DarzasDominion.exe and double-click the launcher.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        string baseDir = AppContext.BaseDirectory;
        string hookDll = Path.Combine(baseDir, "AutoNexusHook.dll");
        string gameExe = Path.Combine(baseDir, "DarzasDominion.exe");

        // Allow CLI override: `AutoNexusLauncher.exe <path-to-game.exe>`
        if (args.Length > 0 && File.Exists(args[0]))
            gameExe = args[0];

        if (!File.Exists(hookDll))
        {
            MessageBox.Show(
                $"AutoNexusHook.dll not found next to launcher.\n\nExpected: {hookDll}",
                "AutoNexusLauncher",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
        if (!File.Exists(gameExe))
        {
            // Browse-for-game-exe fallback.
            using var dlg = new OpenFileDialog
            {
                Title = "Locate DarzasDominion.exe",
                Filter = "DarzasDominion.exe|DarzasDominion.exe|All exe|*.exe",
                CheckFileExists = true
            };
            if (dlg.ShowDialog() != DialogResult.OK)
                return 1;
            gameExe = dlg.FileName;
        }

        var psi = new ProcessStartInfo
        {
            FileName = gameExe,
            WorkingDirectory = Path.GetDirectoryName(gameExe) ?? baseDir,
            UseShellExecute = false,
        };
        // KEY LINE: this env var is read by the .NET runtime at startup
        // of the child process. No further setup needed.
        psi.EnvironmentVariables["DOTNET_STARTUP_HOOKS"] = hookDll;

        try
        {
            Process.Start(psi);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to start game:\n\n{ex.Message}",
                "AutoNexusLauncher",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 2;
        }
    }
}
