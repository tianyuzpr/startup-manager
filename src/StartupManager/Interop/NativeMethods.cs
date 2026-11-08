using System.Runtime.InteropServices;
using System.Text;

namespace StartupManager.Interop;

internal static class NativeMethods
{
    internal const uint SC_MANAGER_CONNECT = 0x0001;
    internal const uint SC_MANAGER_CREATE_SERVICE = 0x0002;
    internal const uint SC_MANAGER_ENUMERATE_SERVICE = 0x0004;
    internal const uint SERVICE_QUERY_STATUS = 0x0004;
    internal const uint SERVICE_START = 0x0010;
    internal const uint SERVICE_STOP = 0x0020;
    internal const uint SERVICE_CHANGE_CONFIG = 0x0002;
    internal const uint SERVICE_DELETE = 0x10000;
    internal const uint SERVICE_ALL_ACCESS = 0xF01FF;
    internal const uint SERVICE_NO_CHANGE = 0xFFFFFFFF;

    internal const uint SERVICE_WIN32 = 0x00000030;
    internal const uint SERVICE_STATE_ALL = 0x00000003;
    internal const int SC_ENUM_PROCESS_INFO = 0;
    internal const int SC_STATUS_PROCESS_INFO = 0;
    internal const uint SC_MANAGER_ALL_ACCESS = 0xF003F;

    internal const uint SERVICE_WIN32_OWN_PROCESS = 0x00000010;
    internal const uint SERVICE_AUTO_START = 0x00000002;
    internal const uint SERVICE_DEMAND_START = 0x00000003;
    internal const uint SERVICE_DISABLED = 0x00000004;
    internal const uint SERVICE_ERROR_NORMAL = 0x00000001;
    internal const uint SERVICE_CONFIG_DESCRIPTION = 1;
    internal const uint SERVICE_CONTROL_STOP = 0x00000001;

    internal const uint SERVICE_STOPPED = 1;
    internal const uint SERVICE_START_PENDING = 2;
    internal const uint SERVICE_STOP_PENDING = 3;
    internal const uint SERVICE_RUNNING = 4;
    internal const uint SERVICE_CONTINUE_PENDING = 5;
    internal const uint SERVICE_PAUSE_PENDING = 6;
    internal const uint SERVICE_PAUSED = 7;

    internal const int ERROR_MORE_DATA = 234;
    internal const int ERROR_SERVICE_DOES_NOT_EXIST = 1060;
    internal const int ERROR_SERVICE_MARKED_FOR_DELETE = 1072;

    [StructLayout(LayoutKind.Sequential)]
    internal struct SERVICE_STATUS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SERVICE_STATUS_PROCESS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
        public uint dwProcessId;
        public uint dwServiceFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ENUM_SERVICE_STATUS_PROCESS
    {
        public IntPtr lpServiceName;
        public IntPtr lpDisplayName;
        public SERVICE_STATUS_PROCESS ServiceStatusProcess;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct SERVICE_DESCRIPTION
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpDescription;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint access);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr OpenService(IntPtr scManager, string serviceName, uint access);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern bool CloseServiceHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern bool EnumServicesStatusEx(
        IntPtr scManager,
        int infoLevel,
        uint serviceType,
        uint serviceState,
        IntPtr buffer,
        uint bufferSize,
        out uint bytesNeeded,
        out uint servicesReturned,
        ref uint resumeHandle,
        string? groupName);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern bool QueryServiceStatusEx(
        IntPtr service,
        int infoLevel,
        IntPtr buffer,
        uint bufferSize,
        out uint bytesNeeded);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern bool ControlService(
        IntPtr service,
        uint control,
        ref SERVICE_STATUS status);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern bool StartService(IntPtr service, uint numArgs, IntPtr[]? args);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern bool DeleteService(IntPtr service);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr CreateService(
        IntPtr scManager,
        string serviceName,
        string displayName,
        uint desiredAccess,
        uint serviceType,
        uint startType,
        uint errorControl,
        string binaryPathName,
        string? loadOrderGroup,
        IntPtr tagId,
        string? dependencies,
        string? serviceStartName,
        string? password);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern bool ChangeServiceConfig2(
        IntPtr service,
        uint infoLevel,
        ref SERVICE_DESCRIPTION description);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern bool ChangeServiceConfig(
        IntPtr service,
        uint serviceType,
        uint startType,
        uint errorControl,
        string? binaryPathName,
        string? loadOrderGroup,
        IntPtr tagId,
        string? dependencies,
        string? serviceStartName,
        string? password,
        string? displayName);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern bool QueryFullProcessImageName(
        IntPtr process,
        uint flags,
        StringBuilder exeName,
        ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CloseHandle(IntPtr handle);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    internal static extern int SHLoadIndirectString(
        string source,
        StringBuilder output,
        int outputLength,
        IntPtr reserved);

    internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    internal static string? TryGetProcessPath(int processId)
    {
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (handle == IntPtr.Zero) return null;

        try
        {
            var capacity = 1024;
            var builder = new StringBuilder(capacity);
            if (QueryFullProcessImageName(handle, 0, builder, ref capacity))
            {
                return builder.ToString();
            }

            return null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    internal static string ResolveIndirectString(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        try
        {
            if (value.StartsWith('@'))
            {
                var buffer = new StringBuilder(2048);
                if (SHLoadIndirectString(value, buffer, buffer.Capacity, IntPtr.Zero) == 0)
                {
                    return buffer.ToString();
                }
            }
        }
        catch
        {
            // Fall back to the raw registry value.
        }

        return value;
    }
}
