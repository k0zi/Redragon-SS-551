using System.Runtime.InteropServices;

namespace SS551.Native;

public sealed class DeckException(string message) : Exception(message);

/// <summary>
/// One opened SS-551. Writes are serialized with a lock so a multi-packet
/// image upload is never interleaved with another command; reading may run
/// concurrently on a separate thread.
/// </summary>
public sealed unsafe class DeckDevice : IDisposable
{
    public const int KeyCount = 6;
    public const int KeyImageSize = 64;

    private readonly Lock _writeLock = new();
    private IntPtr _handle;

    private DeckDevice(IntPtr handle)
    {
        _handle = handle;
        Serial = ReadString(NativeMethods.GetSerial);
        Firmware = ReadString(NativeMethods.GetFirmware);
    }

    public string Serial { get; }
    public string Firmware { get; }

    static DeckDevice()
    {
        if (NativeMethods.Init() != 0)
            throw new DeckException("hid_init failed");
    }

    /// <summary>Opens the first connected deck, or returns null if none is available.</summary>
    public static DeckDevice? TryOpen(out string? error)
    {
        var handle = NativeMethods.Open(null);
        if (handle == IntPtr.Zero)
        {
            error = Marshal.PtrToStringUTF8(NativeMethods.Error(IntPtr.Zero));
            return null;
        }
        error = null;
        return new DeckDevice(handle);
    }

    public void StartSession() => Write(NativeMethods.StartSession);
    public void WakeScreen() => Write(NativeMethods.WakeScreen);
    public void ScreenOff() => Write(NativeMethods.ScreenOff);
    public void Heartbeat() => Write(NativeMethods.Heartbeat);
    public void Flush() => Write(NativeMethods.Flush);
    public void SetBrightness(int percent) => Write(h => NativeMethods.SetBrightness(h, percent));

    /// <summary>Clears one key (0-based) or all keys when index is null, then refreshes.</summary>
    public void Clear(int? keyIndex = null)
    {
        var key = keyIndex is { } i ? (byte)(i + 1) : (byte)0xFF;
        lock (_writeLock)
        {
            Check(NativeMethods.ClearKey(Handle, key));
            Check(NativeMethods.Flush(Handle));
        }
    }

    /// <summary>
    /// Uploads JPEG images (64x64, already rotated for the panel) for several
    /// keys and refreshes the display once. Key indices are 0-based.
    /// </summary>
    public void SetKeyImages(IEnumerable<(int Index, byte[] Jpeg)> images)
    {
        lock (_writeLock)
        {
            foreach (var (index, jpeg) in images)
            {
                fixed (byte* p = jpeg)
                    Check(NativeMethods.SetKeyJpeg(Handle, (byte)(index + 1), p, (nuint)jpeg.Length));
            }
            Check(NativeMethods.Flush(Handle));
        }
    }

    /// <summary>
    /// Waits for the next input report. Returns null on timeout or for
    /// reports that are not input events; decode the result with
    /// <see cref="DeckInput.Decode"/>. Throws <see cref="DeckException"/>
    /// when the device is gone.
    /// </summary>
    public RawInput? ReadInput(int timeoutMs)
    {
        NativeMethods.Event ev;
        var r = NativeMethods.ReadEvent(Handle, &ev, timeoutMs);
        if (r < 0)
            throw new DeckException(LastError());
        // Type 1 = SS551_EVENT_KEY
        return r == 1 && ev.Type == 1 ? new RawInput(ev.Key, ev.State) : null;
    }

    public void Dispose()
    {
        lock (_writeLock)
        {
            if (_handle == IntPtr.Zero)
                return;
            NativeMethods.Close(_handle);
            _handle = IntPtr.Zero;
        }
    }

    private IntPtr Handle => _handle != IntPtr.Zero ? _handle : throw new ObjectDisposedException(nameof(DeckDevice));

    private void Write(Func<IntPtr, int> command)
    {
        lock (_writeLock)
            Check(command(Handle));
    }

    private void Check(int rc)
    {
        if (rc != 0)
            throw new DeckException(LastError());
    }

    private string LastError() => Marshal.PtrToStringUTF8(NativeMethods.Error(_handle)) ?? "unknown error";

    private delegate int StringGetter(IntPtr dev, byte* buf, nuint len);

    private string ReadString(StringGetter getter)
    {
        var buf = stackalloc byte[128];
        return getter(_handle, buf, 128) == 0 ? Marshal.PtrToStringUTF8((IntPtr)buf) ?? "" : "";
    }
}
