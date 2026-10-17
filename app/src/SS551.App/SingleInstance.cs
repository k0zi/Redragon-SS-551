using System.Net.Sockets;

namespace SS551.App;

/// <summary>
/// Keeps one app per user: two instances would both drive the deck and run every action twice.
/// A later launch (e.g. from the menu while the autostarted one sits in the tray) tells the
/// running instance to show its window and exits.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const byte ShowMessage = (byte)'S';

    private readonly Socket _listener;
    private readonly string _path;

    private SingleInstance(Socket listener, string path)
    {
        _listener = listener;
        _path = path;
        new Thread(AcceptLoop) { IsBackground = true, Name = "SS551 single instance" }.Start();
    }

    /// <summary>Raised on a background thread when another launch asks for the window.</summary>
    public event Action? ShowRequested;

    private static string SocketPath
    {
        get
        {
            // $XDG_RUNTIME_DIR is per user and private; fall back to a per-user name in /tmp.
            var dir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            return string.IsNullOrEmpty(dir)
                ? Path.Combine(Path.GetTempPath(), $"ss551-{Environment.UserName}.sock")
                : Path.Combine(dir, "ss551.sock");
        }
    }

    /// <summary>
    /// Becomes the running instance, or returns null after handing over to the one already
    /// running (asking it to show its window unless <paramref name="show"/> is false).
    /// </summary>
    public static SingleInstance? TryAcquire(bool show)
    {
        var path = SocketPath;
        var endpoint = new UnixDomainSocketEndPoint(path);
        if (TryNotify(endpoint, show))
            return null;

        // Nobody answered: a leftover socket file from a crash.
        File.Delete(path);
        var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            listener.Bind(endpoint);
            listener.Listen(4);
            return new SingleInstance(listener, path);
        }
        catch (SocketException)
        {
            // Lost a race against another instance starting at the same moment.
            listener.Dispose();
            return TryNotify(endpoint, show) ? null : throw new InvalidOperationException($"Cannot bind {path}");
        }
    }

    private static bool TryNotify(UnixDomainSocketEndPoint endpoint, bool show)
    {
        using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            client.Connect(endpoint);
            if (show)
                client.Send([ShowMessage]);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private void AcceptLoop()
    {
        var buffer = new byte[1];
        while (true)
        {
            try
            {
                using var client = _listener.Accept();
                if (client.Receive(buffer) == 1 && buffer[0] == ShowMessage)
                    ShowRequested?.Invoke();
            }
            catch (SocketException)
            {
                // A client that hung up early; keep listening.
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }
    }

    public void Dispose()
    {
        _listener.Dispose();
        try
        {
            File.Delete(_path);
        }
        catch (IOException)
        {
        }
    }
}
