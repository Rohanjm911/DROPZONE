using System.Reflection;
using System.Runtime.InteropServices;

namespace DropZone.Engine.Interop;

internal static class NativeMethods
{
    private const string LibName = "dropzone_engine";

    static NativeMethods()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeMethods).Assembly, ResolveDll);
    }

    private static IntPtr ResolveDll(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != LibName)
            return IntPtr.Zero;

        var osName = OperatingSystem.IsWindows() ? "dropzone_engine.dll"
            : OperatingSystem.IsMacOS() ? "libdropzone_engine.dylib"
            : OperatingSystem.IsAndroid() ? "libdropzone_engine.so"
            : OperatingSystem.IsIOS() ? "libdropzone_engine.dylib"
            : "libdropzone_engine.so";

        // Try system / container linker first on mobile platforms
        if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS())
        {
            if (NativeLibrary.TryLoad(LibName, assembly, searchPath, out var sysHandle))
                return sysHandle;
            if (NativeLibrary.TryLoad(osName, assembly, searchPath, out var sysNamedHandle))
                return sysNamedHandle;
        }

        // Try local app directory
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, osName),
            Path.Combine(baseDir, "..", "..", "..", "..", "..", "engine", "target", "release", osName),
            Path.Combine(baseDir, "..", "..", "..", "..", "..", "engine", "target", "debug", osName),
            Path.Combine(baseDir, "runtimes", "win-x64", "native", osName),
            Path.Combine(baseDir, "runtimes", "android-arm64", "native", osName),
            Path.Combine(baseDir, "runtimes", "ios-arm64", "native", osName),
            osName
        };

        foreach (var path in candidates)
        {
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath) && NativeLibrary.TryLoad(fullPath, out var handle))
            {
                return handle;
            }
        }

        return IntPtr.Zero;
    }

    public const int DZ_EVENT_SERVER_STARTED = 1;
    public const int DZ_EVENT_PEER_DISCOVERED = 2;
    public const int DZ_EVENT_TRANSFER_REQUEST = 10;
    public const int DZ_EVENT_TRANSFER_ACCEPTED = 11;
    public const int DZ_EVENT_TRANSFER_REJECTED = 12;
    public const int DZ_EVENT_PROGRESS = 20;
    public const int DZ_EVENT_TELEMETRY = 25;
    public const int DZ_EVENT_PAUSED = 30;
    public const int DZ_EVENT_RESUMED = 31;
    public const int DZ_EVENT_CANCELLED = 32;
    public const int DZ_EVENT_COMPLETED = 40;
    public const int DZ_EVENT_PAIRING_REQUEST = 50;
    public const int DZ_EVENT_PAIRING_PIN_DISPLAY = 51;
    public const int DZ_EVENT_PAIRING_COMPLETED = 52;
    public const int DZ_EVENT_PAIRING_FAILED = 53;
    public const int DZ_EVENT_NAT_CANDIDATE_DISCOVERED = 60;
    public const int DZ_EVENT_ERROR = 99;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void EventCallback(int eventType, IntPtr payloadJsonUtf8, IntPtr userData);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "dropzone_engine_init")]
    public static extern int DropZoneEngineInit([MarshalAs(UnmanagedType.LPUTF8Str)] string configJson);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "dropzone_engine_set_event_callback")]
    public static extern int DropZoneEngineSetEventCallback(EventCallback callback, IntPtr userData);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "dropzone_engine_start_server")]
    public static extern int DropZoneEngineStartServer([MarshalAs(UnmanagedType.LPUTF8Str)] string bindIp, ushort port);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "dropzone_engine_stop_server")]
    public static extern int DropZoneEngineStopServer();

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "dropzone_engine_start_discovery")]
    public static extern int DropZoneEngineStartDiscovery([MarshalAs(UnmanagedType.LPUTF8Str)] string deviceName, ushort port);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "dropzone_engine_stop_discovery")]
    public static extern int DropZoneEngineStopDiscovery();

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "dropzone_engine_send_job")]
    public static extern int DropZoneEngineSendJob(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string peerIp,
        ushort peerPort,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string manifestJson);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "dropzone_engine_accept_job")]
    public static extern int DropZoneEngineAcceptJob([MarshalAs(UnmanagedType.LPUTF8Str)] string jobId, ulong resumeOffset);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "dropzone_engine_reject_job")]
    public static extern int DropZoneEngineRejectJob(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string jobId,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string reason);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "dropzone_engine_pause_job")]
    public static extern int DropZoneEnginePauseJob([MarshalAs(UnmanagedType.LPUTF8Str)] string jobId);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "dropzone_engine_resume_job")]
    public static extern int DropZoneEngineResumeJob([MarshalAs(UnmanagedType.LPUTF8Str)] string jobId);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "dropzone_engine_cancel_job")]
    public static extern int DropZoneEngineCancelJob([MarshalAs(UnmanagedType.LPUTF8Str)] string jobId);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "dropzone_engine_generate_identity_key")]
    public static extern int DropZoneEngineGenerateIdentityKey(IntPtr outJson, nuint maxLen);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "dropzone_engine_start_pairing")]
    public static extern int DropZoneEngineStartPairing([MarshalAs(UnmanagedType.LPUTF8Str)] string peerIp, ushort peerPort);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "dropzone_engine_confirm_pairing")]
    public static extern int DropZoneEngineConfirmPairing(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string peerDeviceId,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string pinCode,
        [MarshalAs(UnmanagedType.I1)] bool isAccepted);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "dropzone_engine_test_stun")]
    public static extern int DropZoneEngineTestStun([MarshalAs(UnmanagedType.LPUTF8Str)] string stunServer, IntPtr outJson, nuint maxLen);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "dropzone_engine_gather_nat_candidates")]
    public static extern int DropZoneEngineGatherNatCandidates([MarshalAs(UnmanagedType.LPUTF8Str)] string stunServer);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "dropzone_engine_ping_peer")]
    public static extern int DropZoneEnginePingPeer([MarshalAs(UnmanagedType.LPUTF8Str)] string peerIp, ushort peerPort);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "dropzone_engine_shutdown")]
    public static extern int DropZoneEngineShutdown();

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "dropzone_engine_get_last_error")]
    public static extern IntPtr DropZoneEngineGetLastError();
}

