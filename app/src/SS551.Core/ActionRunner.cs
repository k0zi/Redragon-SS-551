using System.Diagnostics;
using SS551.Core.Model;
using SS551.Native;

namespace SS551.Core;

/// <summary>Executes the system-side actions (hotkeys, media keys, commands, opening files).</summary>
public sealed class ActionRunner(VirtualKeyboard? keyboard)
{
    public event Action<string>? Log;

    public void Run(DeckAction action)
    {
        try
        {
            switch (action.Type)
            {
                case ActionType.Hotkey:
                    if (KeyCodes.TryParseHotkey(action.Hotkey, out var codes))
                        Tap(codes);
                    else
                        Log?.Invoke($"Unknown hotkey: {action.Hotkey}");
                    break;
                case ActionType.Media:
                    Tap([KeyCodes.Media(action.Media)]);
                    break;
                case ActionType.Command when !string.IsNullOrWhiteSpace(action.Command):
                    Start("/bin/sh", "-c", action.Command);
                    break;
                case ActionType.App when File.Exists(action.AppDesktopFile):
                    Start("gio", "launch", action.AppDesktopFile);
                    break;
                case ActionType.App:
                    Log?.Invoke($"Application not found: {action.AppName} ({action.AppDesktopFile})");
                    break;
                case ActionType.Open when !string.IsNullOrWhiteSpace(action.Target):
                    Start("xdg-open", KeyRenderer.ExpandHome(action.Target.Trim()));
                    break;
            }
        }
        catch (Exception e)
        {
            Log?.Invoke($"Action failed: {e.Message}");
        }
    }

    private void Tap(ushort[] codes)
    {
        if (keyboard is null)
        {
            Log?.Invoke("Virtual keyboard unavailable (/dev/uinput not writable)");
            return;
        }
        keyboard.Tap(codes);
    }

    private static void Start(string file, params string[] args)
    {
        var psi = new ProcessStartInfo(file) { UseShellExecute = false };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        // Fire and forget; the child is reaped by the runtime.
        Process.Start(psi)?.Dispose();
    }
}
