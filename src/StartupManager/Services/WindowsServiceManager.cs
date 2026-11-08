using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using StartupManager.Interop;
using StartupManager.Localization;

namespace StartupManager.Services;

public sealed record WindowsServiceInfo(
    string Name,
    string DisplayName,
    string Description,
    string ImagePath,
    uint StartType,
    bool DelayedAutoStart,
    uint State,
    int ProcessId,
    string? ProcessName);

public static class WindowsServiceManager
{
    public static IReadOnlyList<WindowsServiceInfo> Enumerate()
    {
        var scm = NativeMethods.OpenSCManager(null, null, NativeMethods.SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法打开服务控制管理器。");
        }

        var results = new Dictionary<string, WindowsServiceInfo>(StringComparer.OrdinalIgnoreCase);
        var processNames = new Dictionary<int, string?>();

        try
        {
            using var servicesKey = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services", writable: false);
            if (servicesKey is null)
            {
                throw new InvalidOperationException("无法读取 Windows 服务注册表。");
            }

            foreach (var name in servicesKey.GetSubKeyNames())
            {
                try
                {
                    var metadata = ReadServiceMetadata(name);
                    if ((metadata.ServiceType & 0x30) == 0) continue; // Not a Win32 service.

                    uint state = 0;
                    var processId = 0;
                    var service = NativeMethods.OpenService(
                        scm, name, NativeMethods.SERVICE_QUERY_STATUS);
                    if (service != IntPtr.Zero)
                    {
                        try
                        {
                            var status = QueryStatus(service);
                            state = status.dwCurrentState;
                            processId = (int)status.dwProcessId;
                        }
                        finally
                        {
                            NativeMethods.CloseServiceHandle(service);
                        }
                    }

                    var display = NativeMethods.ResolveIndirectString(metadata.DisplayName);
                    if (string.IsNullOrWhiteSpace(display) || display.StartsWith('@')) display = name;
                    var description = NativeMethods.ResolveIndirectString(metadata.Description);
                    if (description.StartsWith('@')) description = string.Empty;

                    string? processName = null;
                    if (processId > 0)
                    {
                        if (!processNames.TryGetValue(processId, out processName))
                        {
                            try
                            {
                                using var process = Process.GetProcessById(processId);
                                processName = process.ProcessName;
                            }
                            catch
                            {
                                processName = null;
                            }

                            processNames[processId] = processName;
                        }
                    }

                    results[name] = new WindowsServiceInfo(
                        name,
                        display.Trim(),
                        description.Trim(),
                        metadata.ImagePath,
                        metadata.StartType,
                        metadata.DelayedAutoStart,
                        state,
                        processId,
                        processName);
                }
                catch
                {
                    // A service may disappear or be inaccessible while enumerating.
                }
            }
        }
        finally
        {
            NativeMethods.CloseServiceHandle(scm);
        }

        return results.Values
            .OrderBy(s => s.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static string StartModeLabel(uint startType, bool delayed)
    {
        return startType switch
        {
            0 => L.T("引导启动"),
            1 => L.T("系统启动"),
            2 when delayed => L.T("自动（延迟）"),
            2 => L.T("自动"),
            3 => L.T("手动"),
            4 => L.T("已禁用"),
            _ => L.T("未知")
        };
    }

    public static bool IsRunning(uint state) => state == NativeMethods.SERVICE_RUNNING;

    public static bool IsProtected(string serviceName)
        => SystemServiceProtection.IsProtected(serviceName);

    public static string ProtectedReason(string serviceName)
        => SystemServiceProtection.ProtectedReason(serviceName);

    public static void StartService(string serviceName)
    {
        WithService(serviceName, NativeMethods.SERVICE_QUERY_STATUS | NativeMethods.SERVICE_START, service =>
        {
            var status = QueryStatus(service);
            if (status.dwCurrentState == NativeMethods.SERVICE_RUNNING) return;

            if (!NativeMethods.StartService(service, 0, null))
            {
                var error = Marshal.GetLastWin32Error();
                if (error == 1056) return; // already running
                throw new Win32Exception(error, $"启动服务“{serviceName}”失败。");
            }

            var final = WaitForState(service, NativeMethods.SERVICE_RUNNING, TimeSpan.FromSeconds(20));
            if (final.dwCurrentState != NativeMethods.SERVICE_RUNNING)
            {
                throw new InvalidOperationException(
                    $"服务“{serviceName}”已接受启动请求，但未能进入运行状态（当前状态：{StateLabel(final.dwCurrentState)}）。");
            }
        });
    }

    public static void StopService(string serviceName)
    {
        WithService(serviceName, NativeMethods.SERVICE_QUERY_STATUS | NativeMethods.SERVICE_STOP, service =>
        {
            var status = QueryStatus(service);
            if (status.dwCurrentState == NativeMethods.SERVICE_STOPPED) return;

            if (!SendStopControl(service, status))
            {
                var error = Marshal.GetLastWin32Error();
                if (error == 1062) return; // not running
                throw new Win32Exception(error, $"停止服务“{serviceName}”失败。");
            }

            var final = WaitForState(service, NativeMethods.SERVICE_STOPPED, TimeSpan.FromSeconds(25));
            if (final.dwCurrentState != NativeMethods.SERVICE_STOPPED)
            {
                throw new InvalidOperationException(
                    $"服务“{serviceName}”未能停止（当前状态：{StateLabel(final.dwCurrentState)}）。可能需要重启系统。");
            }
        });
    }

    public static void RestartService(string serviceName)
    {
        WithService(
            serviceName,
            NativeMethods.SERVICE_QUERY_STATUS | NativeMethods.SERVICE_START | NativeMethods.SERVICE_STOP,
            service =>
            {
                var status = QueryStatus(service);
                if (status.dwCurrentState != NativeMethods.SERVICE_STOPPED)
                {
                    if (!SendStopControl(service, status))
                    {
                        var error = Marshal.GetLastWin32Error();
                        if (error != 1062)
                        {
                            throw new Win32Exception(error, $"停止服务“{serviceName}”失败。");
                        }
                    }
                    else
                    {
                        WaitForState(service, NativeMethods.SERVICE_STOPPED, TimeSpan.FromSeconds(25));
                    }
                }

                if (!NativeMethods.StartService(service, 0, null))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error != 1056)
                    {
                        throw new Win32Exception(error, $"重启服务“{serviceName}”时启动失败。");
                    }
                }

                WaitForState(service, NativeMethods.SERVICE_RUNNING, TimeSpan.FromSeconds(20));
            });
    }

    public static void DeleteService(string serviceName)
    {
        WithService(
            serviceName,
            NativeMethods.SERVICE_QUERY_STATUS | NativeMethods.SERVICE_STOP | NativeMethods.SERVICE_DELETE,
            service =>
            {
                var status = QueryStatus(service);
                if (status.dwCurrentState != NativeMethods.SERVICE_STOPPED)
                {
                    if (SendStopControl(service, status))
                    {
                        WaitForState(service, NativeMethods.SERVICE_STOPPED, TimeSpan.FromSeconds(25));
                    }
                }

                if (!NativeMethods.DeleteService(service))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == NativeMethods.ERROR_SERVICE_MARKED_FOR_DELETE) return;
                    throw new Win32Exception(error, $"删除服务“{serviceName}”失败。");
                }
            });
    }

    public static uint GetStartType(string serviceName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Services\{serviceName}", writable: false);
            return key?.GetValue("Start") is int start ? (uint)start : NativeMethods.SERVICE_DEMAND_START;
        }
        catch
        {
            return NativeMethods.SERVICE_DEMAND_START;
        }
    }

    public static (uint StartType, bool UsedFallback) SetEnabled(string serviceName, bool enabled)
    {
        if (enabled)
        {
            var current = GetStartType(serviceName);
            if (current != NativeMethods.SERVICE_DISABLED)
            {
                return (current, false);
            }

            var remembered = ServiceStartTypeStore.Get(serviceName);
            var startType = remembered ?? NativeMethods.SERVICE_DEMAND_START;
            if (startType == NativeMethods.SERVICE_DISABLED)
            {
                startType = NativeMethods.SERVICE_DEMAND_START;
            }

            SetStartType(serviceName, startType);
            ServiceStartTypeStore.Forget(serviceName);
            return (startType, remembered is null);
        }

        var existingStartType = GetStartType(serviceName);
        if (existingStartType == NativeMethods.SERVICE_DISABLED)
        {
            return (NativeMethods.SERVICE_DISABLED, false);
        }

        ServiceStartTypeStore.Remember(serviceName, existingStartType);
        try
        {
            SetStartType(serviceName, NativeMethods.SERVICE_DISABLED);
        }
        catch
        {
            ServiceStartTypeStore.Forget(serviceName);
            throw;
        }

        return (NativeMethods.SERVICE_DISABLED, false);
    }

    public static void SetStartType(string serviceName, uint startType)
    {
        if (startType > NativeMethods.SERVICE_DISABLED)
        {
            throw new ArgumentOutOfRangeException(nameof(startType));
        }

        WithService(serviceName, NativeMethods.SERVICE_CHANGE_CONFIG, service =>
        {
            if (!NativeMethods.ChangeServiceConfig(
                    service,
                    NativeMethods.SERVICE_NO_CHANGE,
                    startType,
                    NativeMethods.SERVICE_NO_CHANGE,
                    null,
                    null,
                    IntPtr.Zero,
                    null,
                    null,
                    null,
                    null))
            {
                var error = Marshal.GetLastWin32Error();
                throw new Win32Exception(
                    error,
                    $"修改服务“{serviceName}”的启动类型失败。该操作需要管理员权限。");
            }
        });
    }

    public static void CreateService(
        string serviceName,
        string displayName,
        string executablePath,
        string arguments,
        string description,
        bool startImmediately)
    {
        if (string.IsNullOrWhiteSpace(serviceName) || serviceName.Length > 256)
        {
            throw new ArgumentException("服务名称无效。", nameof(serviceName));
        }

        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException("可执行文件不存在。", executablePath);
        }

        var scm = NativeMethods.OpenSCManager(
            null, null, NativeMethods.SC_MANAGER_CREATE_SERVICE | NativeMethods.SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法打开服务控制管理器，请以管理员身份运行。");
        }

        try
        {
            var existing = NativeMethods.OpenService(scm, serviceName, NativeMethods.SERVICE_QUERY_STATUS);
            if (existing != IntPtr.Zero)
            {
                NativeMethods.CloseServiceHandle(existing);
                throw new InvalidOperationException($"服务名称“{serviceName}”已存在。");
            }

            var service = NativeMethods.CreateService(
                scm,
                serviceName,
                displayName,
                NativeMethods.SERVICE_ALL_ACCESS,
                NativeMethods.SERVICE_WIN32_OWN_PROCESS,
                NativeMethods.SERVICE_AUTO_START,
                NativeMethods.SERVICE_ERROR_NORMAL,
                CommandLineParser.BuildCommandLine(executablePath, arguments),
                null,
                IntPtr.Zero,
                null,
                "LocalSystem",
                null);

            if (service == IntPtr.Zero)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "创建 Windows 服务失败。普通桌面程序不能直接作为服务运行，请确认该程序实现了服务接口。");
            }

            try
            {
                var desc = new NativeMethods.SERVICE_DESCRIPTION
                {
                    lpDescription = string.IsNullOrWhiteSpace(description) ? displayName : description
                };
                NativeMethods.ChangeServiceConfig2(service, NativeMethods.SERVICE_CONFIG_DESCRIPTION, ref desc);

                if (startImmediately)
                {
                    if (!NativeMethods.StartService(service, 0, null))
                    {
                        var error = Marshal.GetLastWin32Error();
                        if (error != 1056)
                        {
                            throw new Win32Exception(
                                error,
                                $"服务已安装，但启动失败（错误 {error}）。若该程序不是标准 Windows 服务，请改用“系统启动任务”。");
                        }
                    }
                }
            }
            finally
            {
                NativeMethods.CloseServiceHandle(service);
            }
        }
        finally
        {
            NativeMethods.CloseServiceHandle(scm);
        }
    }

    public static bool ServiceExists(string serviceName)
    {
        var scm = NativeMethods.OpenSCManager(null, null, NativeMethods.SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero) return false;

        try
        {
            var service = NativeMethods.OpenService(scm, serviceName, NativeMethods.SERVICE_QUERY_STATUS);
            if (service == IntPtr.Zero) return false;
            NativeMethods.CloseServiceHandle(service);
            return true;
        }
        finally
        {
            NativeMethods.CloseServiceHandle(scm);
        }
    }

    public static string StateLabel(uint state) => state switch
    {
        NativeMethods.SERVICE_STOPPED => L.T("已停止"),
        NativeMethods.SERVICE_START_PENDING => L.T("正在启动"),
        NativeMethods.SERVICE_STOP_PENDING => L.T("正在停止"),
        NativeMethods.SERVICE_RUNNING => L.T("运行中"),
        NativeMethods.SERVICE_CONTINUE_PENDING => L.T("正在继续"),
        NativeMethods.SERVICE_PAUSE_PENDING => L.T("正在暂停"),
        NativeMethods.SERVICE_PAUSED => L.T("已暂停"),
        _ => L.T("未知")
    };

    private static (string DisplayName, string Description, string ImagePath, uint StartType,
        bool DelayedAutoStart, uint ServiceType) ReadServiceMetadata(string serviceName)
    {
        var displayName = serviceName;
        var description = string.Empty;
        var imagePath = string.Empty;
        uint startType = 3;
        var delayed = false;
        uint serviceType = 0;

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Services\{serviceName}", writable: false);
            if (key is null) return (displayName, description, imagePath, startType, delayed, serviceType);

            if (key.GetValue("DisplayName") is string display && display.Length > 0) displayName = display;
            if (key.GetValue("Description") is string desc) description = desc;
            if (key.GetValue("ImagePath") is string image) imagePath = image;
            if (key.GetValue("Start") is int start) startType = (uint)start;
            if (key.GetValue("DelayedAutoStart") is int delay) delayed = delay != 0;
            if (key.GetValue("Type") is int type) serviceType = (uint)type;
        }
        catch
        {
            // Metadata is best-effort.
        }

        return (displayName, description, imagePath, startType, delayed, serviceType);
    }

    private static void WithService(string serviceName, uint access, Action<IntPtr> action)
    {
        var scm = NativeMethods.OpenSCManager(null, null, NativeMethods.SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法打开服务控制管理器。");
        }

        try
        {
            var service = NativeMethods.OpenService(scm, serviceName, access);
            if (service == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"无法打开服务“{serviceName}”。");
            }

            try
            {
                action(service);
            }
            finally
            {
                NativeMethods.CloseServiceHandle(service);
            }
        }
        finally
        {
            NativeMethods.CloseServiceHandle(scm);
        }
    }

    private static bool SendStopControl(IntPtr service, NativeMethods.SERVICE_STATUS_PROCESS status)
    {
        var control = new NativeMethods.SERVICE_STATUS
        {
            dwServiceType = status.dwServiceType,
            dwCurrentState = status.dwCurrentState,
            dwControlsAccepted = status.dwControlsAccepted,
            dwWin32ExitCode = status.dwWin32ExitCode,
            dwServiceSpecificExitCode = status.dwServiceSpecificExitCode,
            dwCheckPoint = status.dwCheckPoint,
            dwWaitHint = status.dwWaitHint
        };
        return NativeMethods.ControlService(service, NativeMethods.SERVICE_CONTROL_STOP, ref control);
    }

    private static NativeMethods.SERVICE_STATUS_PROCESS QueryStatus(IntPtr service)
    {
        var size = Marshal.SizeOf<NativeMethods.SERVICE_STATUS_PROCESS>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!NativeMethods.QueryServiceStatusEx(
                    service,
                    NativeMethods.SC_STATUS_PROCESS_INFO,
                    buffer,
                    (uint)size,
                    out _))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "查询服务状态失败。");
            }

            return Marshal.PtrToStructure<NativeMethods.SERVICE_STATUS_PROCESS>(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static NativeMethods.SERVICE_STATUS_PROCESS WaitForState(IntPtr service, uint desired, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var status = QueryStatus(service);
        while (DateTime.UtcNow < deadline)
        {
            if (status.dwCurrentState == desired) return status;

            if (status.dwCurrentState == NativeMethods.SERVICE_STOPPED && desired == NativeMethods.SERVICE_RUNNING)
            {
                return status;
            }

            Thread.Sleep(250);
            status = QueryStatus(service);
        }

        return status;
    }
}
