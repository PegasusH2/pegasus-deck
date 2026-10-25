using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace PegasusDeck.Core.Native;

/// <summary>
/// Identidad del proceso al otro lado de un named pipe: ruta de su ejecutable y sesión de
/// Windows (ADR-016). En producción el servicio corre en la sesión 0, donde un usuario sin
/// privilegios no puede crear procesos; el agente corre en la sesión de la consola activa.
/// </summary>
public static partial class ProcessIdentity
{
    private const uint ProcessQueryLimitedInformation = 0x1000;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeClientSessionId(SafePipeHandle pipe, out uint sessionId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeServerSessionId(SafePipeHandle pipe, out uint sessionId);

    [LibraryImport("kernel32.dll")]
    private static partial uint WTSGetActiveConsoleSessionId();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ProcessIdToSessionId(uint processId, out uint sessionId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, char* buffer, ref uint size);

    public static int? ClientProcessId(PipeStream pipe) =>
        GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var pid) ? (int)pid : null;

    public static int? ServerProcessId(PipeStream pipe) =>
        GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var pid) ? (int)pid : null;

    public static int? ClientSessionId(PipeStream pipe) =>
        GetNamedPipeClientSessionId(pipe.SafePipeHandle, out var id) ? (int)id : null;

    public static int? ServerSessionId(PipeStream pipe) =>
        GetNamedPipeServerSessionId(pipe.SafePipeHandle, out var id) ? (int)id : null;

    /// <summary>Sesión de la consola física (0xFFFFFFFF si no hay ninguna).</summary>
    public static int ActiveConsoleSessionId() => unchecked((int)WTSGetActiveConsoleSessionId());

    public static int? SessionOf(int processId) => ProcessIdToSessionId((uint)processId, out var id) ? (int)id : null;

    /// <summary>Ruta completa del ejecutable del proceso, o null si no se puede consultar.</summary>
    public static unsafe string? ImagePath(int processId)
    {
        using var handle = OpenProcess(ProcessQueryLimitedInformation, false, (uint)processId);
        if (handle.IsInvalid) return null;
        var buffer = new char[1024];
        var size = (uint)buffer.Length;
        fixed (char* p = buffer)
        {
            return QueryFullProcessImageName(handle, 0, p, ref size) ? new string(buffer, 0, (int)size) : null;
        }
    }

    /// <summary>Propietario del objeto pipe (lo fija quien lo crea; un usuario normal no puede poner SYSTEM).</summary>
    public static SecurityIdentifier? PipeOwner(PipeStream pipe)
    {
        try
        {
            return pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Cuentas con las que puede correr el servicio de Windows. Un usuario sin privilegios no puede
    /// crear objetos cuyo propietario sea una de ellas.
    /// </summary>
    public static bool IsServiceAccount(SecurityIdentifier? sid) =>
        sid is not null && (
            sid.IsWellKnown(WellKnownSidType.LocalSystemSid) ||
            sid.IsWellKnown(WellKnownSidType.LocalServiceSid) ||
            sid.IsWellKnown(WellKnownSidType.NetworkServiceSid) ||
            sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid) ||
            sid.Value.StartsWith("S-1-5-80-", StringComparison.Ordinal)); // NT SERVICE\<servicio>

    public static bool SamePath(string? a, string? b) =>
        a is not null && b is not null &&
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}

/// <summary>Qué proceso escucha en un puerto TCP local (para no hablar con un OBS falso).</summary>
public static partial class TcpListeners
{
    private const int AfInet = 2;
    private const int TcpTableOwnerPidListener = 3;

    [LibraryImport("iphlpapi.dll")]
    private static partial uint GetExtendedTcpTable(IntPtr table, ref int size, [MarshalAs(UnmanagedType.Bool)] bool order, int af, int tableClass, uint reserved);

    /// <summary>PID del proceso que escucha en 127.0.0.1/0.0.0.0:port (IPv4), o null.</summary>
    public static int? OwnerOf(int port)
    {
        var size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, AfInet, TcpTableOwnerPidListener, 0);
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buffer, ref size, false, AfInet, TcpTableOwnerPidListener, 0) != 0) return null;
            var count = Marshal.ReadInt32(buffer);
            // MIB_TCPROW_OWNER_PID: state, localAddr, localPort, remoteAddr, remotePort, owningPid (6 × 4 bytes)
            for (var i = 0; i < count; i++)
            {
                var row = buffer + 4 + (i * 24);
                var rawPort = Marshal.ReadInt32(row, 8);
                var localPort = ((rawPort & 0xFF) << 8) | ((rawPort >> 8) & 0xFF);
                if (localPort == port) return Marshal.ReadInt32(row, 20);
            }
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
