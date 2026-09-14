using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace Umsatzschätzung.App.Platform;

static class CrashLog
{
    static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Umsatzschätzung", "crash.log");

    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Report(e.ExceptionObject as Exception, "Schwerwiegender Fehler");
        TaskScheduler.UnobservedTaskException += (_, e) => { Report(e.Exception, "Hintergrundfehler"); e.SetObserved(); };
        AddVectoredExceptionHandler(0, native);
    }

    // A fault inside a native library never reaches the managed handlers above: the
    // runtime treats it as corrupted state and ends the process without unwinding.
    // The vectored handler runs first, so the log records what the managed side
    // never sees. It only writes — showing UI from here is not safe.
    static readonly VectoredHandler native = Native;

    static int Native(IntPtr pointers)
    {
        var record = Marshal.ReadIntPtr(pointers);
        var code = (uint) Marshal.ReadInt32(record);
        if (code is not (0xC0000005 or 0xC000001D or 0xC00000FD)) return ExceptionContinueSearch;
        var address = Marshal.ReadIntPtr(record, 16);
        var kind = code == 0xC0000005 ? Marshal.ReadInt64(record, 32) switch { 0 => "Lesen", 1 => "Schreiben", 8 => "Ausführen", _ => "Zugriff" } : "";
        var target = code == 0xC0000005 ? $" {kind} 0x{Marshal.ReadInt64(record, 40):X}" : "";
        var known = GetModuleHandleEx(FromAddress | UnchangedRefcount, address, out var module);
        var where = known ? $"{Module(module)}+0x{address.ToInt64() - module.ToInt64():X}" : "unbekanntem Modul";
        Write($"0x{code:X8} bei 0x{address.ToInt64():X} in {where}{target}{(known ? Dump(pointers) : "")}");
        return ExceptionContinueSearch;
    }

    static string Module(IntPtr module)
    {
        var name = new StringBuilder(260);
        return GetModuleFileName(module, name, name.Capacity) > 0 ? name.ToString() : "unbekanntem Modul";
    }

    static int dumped;

    static string Dump(IntPtr pointers)
    {
        if (Interlocked.Exchange(ref dumped, 1) != 0) return "";
        var path = System.IO.Path.ChangeExtension(Path, $"{DateTimeOffset.Now:yyyyMMdd-HHmmss}.dmp");
        try
        {
            using var file = File.Create(path);
            var info = new MinidumpException { ThreadId = GetCurrentThreadId(), Pointers = pointers };
            var ok = MiniDumpWriteDump(System.Diagnostics.Process.GetCurrentProcess().Handle, Environment.ProcessId,
                file.SafeFileHandle, WithIndirectlyReferencedMemory, ref info, IntPtr.Zero, IntPtr.Zero);
            return ok ? $"\nAbbild: {path}" : $"\nAbbild fehlgeschlagen: {Marshal.GetLastWin32Error()}";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return $"\nAbbild fehlgeschlagen: {e.Message}"; }
    }

    public static void Report(Exception? ex, string title)
    {
        var detail = ex?.ToString() ?? "unbekannter Fehler";
        var written = Write(detail);
        var message = (ex?.Message ?? detail) + (written ? $"\n\nDetails: {Path}" : "");
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    static bool Write(string detail)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.AppendAllText(Path, $"{DateTimeOffset.Now:O}\n{detail}\n\n");
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    const int ExceptionContinueSearch = 0;
    const uint FromAddress = 0x4;
    const uint UnchangedRefcount = 0x2;

    const uint WithIndirectlyReferencedMemory = 0x40;

    delegate int VectoredHandler(IntPtr pointers);

    [StructLayout(LayoutKind.Sequential)]
    struct MinidumpException
    {
        public uint ThreadId;
        public IntPtr Pointers;
        public int ClientPointers;
    }

    [DllImport("kernel32")]
    static extern uint GetCurrentThreadId();

    [DllImport("dbghelp", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool MiniDumpWriteDump(IntPtr process, int processId, Microsoft.Win32.SafeHandles.SafeFileHandle file,
        uint type, ref MinidumpException exception, IntPtr streams, IntPtr callback);

    [DllImport("kernel32")]
    static extern IntPtr AddVectoredExceptionHandler(uint first, VectoredHandler handler);

    [DllImport("kernel32", EntryPoint = "GetModuleHandleExW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetModuleHandleEx(uint flags, IntPtr address, out IntPtr module);

    [DllImport("kernel32", EntryPoint = "GetModuleFileNameW", CharSet = CharSet.Unicode)]
    static extern int GetModuleFileName(IntPtr module, StringBuilder name, int size);
}
