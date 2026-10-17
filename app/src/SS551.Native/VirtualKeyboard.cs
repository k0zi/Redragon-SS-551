namespace SS551.Native;

/// <summary>uinput keyboard used to emit hotkeys and media keys (works on Wayland and X11).</summary>
public sealed unsafe class VirtualKeyboard : IDisposable
{
    private readonly Lock _lock = new();
    private IntPtr _handle;

    private VirtualKeyboard(IntPtr handle) => _handle = handle;

    /// <summary>Returns null when /dev/uinput is not writable.</summary>
    public static VirtualKeyboard? TryOpen()
    {
        var h = NativeMethods.VkbdOpen();
        return h == IntPtr.Zero ? null : new VirtualKeyboard(h);
    }

    /// <summary>Presses the Linux key codes in order and releases them in reverse.</summary>
    public void Tap(IReadOnlyList<ushort> codes)
    {
        if (codes.Count == 0)
            return;
        var arr = codes.ToArray();
        lock (_lock)
        {
            if (_handle == IntPtr.Zero)
                return;
            fixed (ushort* p = arr)
                NativeMethods.VkbdTap(_handle, p, arr.Length);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_handle == IntPtr.Zero)
                return;
            NativeMethods.VkbdClose(_handle);
            _handle = IntPtr.Zero;
        }
    }
}
