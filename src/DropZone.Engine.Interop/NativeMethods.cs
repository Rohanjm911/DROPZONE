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
            : "libdropzone_engine.so";

        // Try local app directory
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, osName),
            Path.Combine(baseDir, "..", "..", "..", "..", "..", "engine", "target", "release", osName),
            Path.Combine(baseDir, "..", "..", "..", "..", "..", "engine", "target", "debug", osName),
            Path.Combine(baseDir, "runtimes", "win-x64", "native", osName),
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

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "dropzone_engine_shutdown")]
    public static extern int DropZoneEngineShutdown();

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "dropzone_engine_get_last_error")]
    public static extern IntPtr DropZoneEngineGetLastError();
}

