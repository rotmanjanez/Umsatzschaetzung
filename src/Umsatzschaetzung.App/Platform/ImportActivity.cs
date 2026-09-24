using System.Runtime.InteropServices;

namespace Umsatzschaetzung.App.Platform;

// Ohne Fokus drosseln macOS (App Nap) und Windows (EcoQoS) den Prozess und schieben die
// Erkennung auf die Effizienzkerne. Solange ein Import läuft, gilt er als vom Benutzer
// angestoßen: volle Leistung, kein Ruhezustand.
sealed class ImportActivity : IDisposable
{
    readonly nint token;

    ImportActivity(nint token) => this.token = token;

    public static ImportActivity Begin()
    {
        if (OperatingSystem.IsMacOS()) return new(Mac.Begin());
        if (OperatingSystem.IsWindows()) Win.Throttle(false);
        return new(0);
    }

    public void Dispose()
    {
        if (OperatingSystem.IsMacOS()) Mac.End(token);
        else if (OperatingSystem.IsWindows()) Win.Throttle(true);
    }

    static class Mac
    {
        const string Lib = "/usr/lib/libobjc.A.dylib";
        const ulong UserInitiated = 0x00FFFFFF | (1UL << 20);

        [DllImport(Lib)] static extern nint objc_getClass(string name);
        [DllImport(Lib)] static extern nint sel_registerName(string name);
        [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern nint Send(nint target, nint selector);
        [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern nint Send(nint target, nint selector, string arg);
        [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern nint Send(nint target, nint selector, ulong options, nint reason);
        [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern void Send(nint target, nint selector, nint arg);

        static nint Info => Send(objc_getClass("NSProcessInfo"), sel_registerName("processInfo"));

        public static nint Begin()
        {
            var reason = Send(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), "Import");
            var activity = Send(Info, sel_registerName("beginActivityWithOptions:reason:"), UserInitiated, reason);
            return Send(activity, sel_registerName("retain"));
        }

        public static void End(nint activity)
        {
            if (activity == 0) return;
            Send(Info, sel_registerName("endActivity:"), activity);
            Send(activity, sel_registerName("release"));
        }
    }

    static class Win
    {
        const int ProcessPowerThrottling = 4;
        const uint ExecutionSpeed = 1;
        const uint Continuous = 0x80000000, SystemRequired = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        struct PowerThrottling
        {
            public uint Version, ControlMask, StateMask;
        }

        [DllImport("kernel32.dll")] static extern nint GetCurrentProcess();
        [DllImport("kernel32.dll")] static extern bool SetProcessInformation(nint process, int infoClass, ref PowerThrottling info, int size);
        [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint flags);

        public static void Throttle(bool allowed)
        {
            var state = new PowerThrottling { Version = 1, ControlMask = allowed ? 0 : ExecutionSpeed };
            SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, ref state, Marshal.SizeOf<PowerThrottling>());
            SetThreadExecutionState(allowed ? Continuous : Continuous | SystemRequired);
        }
    }
}
