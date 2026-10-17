using System.Diagnostics;
using SS551.Core.Model;

namespace SS551.Core;

/// <summary>Evaluates the polled state sources (audio mute via wpctl, user commands).</summary>
internal static class StateProbe
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>Wpctl output is shared by all keys polled in the same round.</summary>
    public sealed class Round
    {
        private Task<bool?>? _sink;
        private Task<bool?>? _source;

        public Task<bool?> SpeakerMuted => _sink ??= IsMutedAsync("@DEFAULT_AUDIO_SINK@");
        public Task<bool?> MicMuted => _source ??= IsMutedAsync("@DEFAULT_AUDIO_SOURCE@");
    }

    /// <summary>Returns null when the state cannot be determined (it is then left unchanged).</summary>
    public static Task<bool?> EvaluateAsync(StateConfig state, Round round) => state.Source switch
    {
        StateSource.SpeakerMuted => round.SpeakerMuted,
        StateSource.MicMuted => round.MicMuted,
        StateSource.Command when !string.IsNullOrWhiteSpace(state.Command) => CommandSucceedsAsync(state.Command),
        _ => Task.FromResult<bool?>(null),
    };

    /// <summary>How often a source is polled; null for sources that are not polled.</summary>
    public static TimeSpan? Interval(StateConfig state) => state.Source switch
    {
        StateSource.SpeakerMuted or StateSource.MicMuted => TimeSpan.FromSeconds(1),
        StateSource.Command => TimeSpan.FromSeconds(Math.Max(0.2, state.IntervalSeconds)),
        _ => null,
    };

    // "wpctl get-volume" prints e.g. "Volume: 0.36" or "Volume: 0.36 [MUTED]".
    private static async Task<bool?> IsMutedAsync(string node)
    {
        var r = await RunAsync("wpctl", "get-volume", node);
        return r is (0, var output) ? output.Contains("[MUTED]", StringComparison.Ordinal) : null;
    }

    private static async Task<bool?> CommandSucceedsAsync(string command)
    {
        var r = await RunAsync("/bin/sh", "-c", command);
        return r is var (exit, _) ? exit == 0 : null;
    }

    private static async Task<(int Exit, string Output)?> RunAsync(string file, params string[] args)
    {
        var psi = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
                return null;
            var stdout = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(Timeout);
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                return null;
            }
            return (process.ExitCode, await stdout);
        }
        catch (Exception)
        {
            // e.g. wpctl not installed
            return null;
        }
    }
}
