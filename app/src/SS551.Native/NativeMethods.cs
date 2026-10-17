using System.Reflection;
using System.Runtime.InteropServices;

namespace SS551.Native;

/// <summary>P/Invoke surface of libss551 (driver/include/ss551.h).</summary>
internal static unsafe partial class NativeMethods
{
    private const string Lib = "ss551";

    public const int InPacket = 512;

    // Runs before the first P/Invoke call through this class.
    static NativeMethods() =>
        NativeLibrary.SetDllImportResolver(typeof(NativeMethods).Assembly, Resolve);

    // Look next to the app first, then in SS551_LIB_DIR, then the default search path.
    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? path)
    {
        if (name != Lib)
            return IntPtr.Zero;
        var dirs = new[] { AppContext.BaseDirectory, Environment.GetEnvironmentVariable("SS551_LIB_DIR") };
        foreach (var dir in dirs)
        {
            if (string.IsNullOrEmpty(dir))
                continue;
            var file = Path.Combine(dir, "libss551.so");
            if (File.Exists(file) && NativeLibrary.TryLoad(file, out var handle))
                return handle;
        }
        return NativeLibrary.TryLoad("libss551.so.0", out var h) ? h : IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Event
    {
        public int Type;
        public byte Key;
        public byte State;
        public fixed byte Raw[InPacket];
        public int RawLen;
    }

    [LibraryImport(Lib, EntryPoint = "ss551_init")]
    public static partial int Init();

    [LibraryImport(Lib, EntryPoint = "ss551_exit")]
    public static partial void Exit();

    [LibraryImport(Lib, EntryPoint = "ss551_open", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr Open(string? serial);

    [LibraryImport(Lib, EntryPoint = "ss551_close")]
    public static partial void Close(IntPtr dev);

    [LibraryImport(Lib, EntryPoint = "ss551_error")]
    public static partial IntPtr Error(IntPtr dev);

    [LibraryImport(Lib, EntryPoint = "ss551_get_serial")]
    public static partial int GetSerial(IntPtr dev, byte* buf, nuint len);

    [LibraryImport(Lib, EntryPoint = "ss551_get_firmware")]
    public static partial int GetFirmware(IntPtr dev, byte* buf, nuint len);

    [LibraryImport(Lib, EntryPoint = "ss551_start_session")]
    public static partial int StartSession(IntPtr dev);

    [LibraryImport(Lib, EntryPoint = "ss551_wake_screen")]
    public static partial int WakeScreen(IntPtr dev);

    [LibraryImport(Lib, EntryPoint = "ss551_screen_off")]
    public static partial int ScreenOff(IntPtr dev);

    [LibraryImport(Lib, EntryPoint = "ss551_set_brightness")]
    public static partial int SetBrightness(IntPtr dev, int pct);

    [LibraryImport(Lib, EntryPoint = "ss551_clear_key")]
    public static partial int ClearKey(IntPtr dev, byte key);

    [LibraryImport(Lib, EntryPoint = "ss551_flush")]
    public static partial int Flush(IntPtr dev);

    [LibraryImport(Lib, EntryPoint = "ss551_heartbeat")]
    public static partial int Heartbeat(IntPtr dev);

    [LibraryImport(Lib, EntryPoint = "ss551_set_key_jpeg")]
    public static partial int SetKeyJpeg(IntPtr dev, byte key, byte* jpeg, nuint len);

    [LibraryImport(Lib, EntryPoint = "ss551_read_event")]
    public static partial int ReadEvent(IntPtr dev, Event* ev, int timeoutMs);

    [LibraryImport(Lib, EntryPoint = "ss551_vkbd_open")]
    public static partial IntPtr VkbdOpen();

    [LibraryImport(Lib, EntryPoint = "ss551_vkbd_close")]
    public static partial void VkbdClose(IntPtr kbd);

    [LibraryImport(Lib, EntryPoint = "ss551_vkbd_tap")]
    public static partial int VkbdTap(IntPtr kbd, ushort* codes, int n);
}
