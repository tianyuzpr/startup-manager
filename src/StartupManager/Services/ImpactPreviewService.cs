using StartupManager.Localization;
using StartupManager.Models;

namespace StartupManager.Services;

/// <summary>
/// Pure impact-preview calculation. No file system, registry, service or UI
/// side effects are performed here.
/// </summary>
public static class ImpactPreviewService
{
    public static ImpactPreview Build(ImpactOperation operation, IReadOnlyList<ManagedEntry> entries)
    {
        var warnings = new List<string>();
        var items = new List<ImpactItem>();
        var requiresTyped = false;
        string? typedTarget = null;
        var snapshot = false;

        foreach (var entry in entries)
        {
            var canExecute = operation != ImpactOperation.Delete || entry.CanDelete;
            items.Add(new ImpactItem
            {
                Entry = entry,
                StableId = entry.Id,
                Name = entry.Name,
                CategoryLabel = entry.KindLabel,
                CurrentState = entry.StatusLabel,
                TargetState = TargetLabel(operation),
                Matchability = entry.Matchability,
                IsProtected = entry.IsProtected,
                ProtectedReason = entry.ProtectedReason,
                CanExecute = canExecute
            });

            if (entry.IsProtected)
            {
                warnings.Add(L.F("“{0}”在系统服务保护名单中。", entry.Name));
            }

            if (operation is ImpactOperation.Stop or ImpactOperation.Restart
                && entry.Matchability != Matchability.Reliable)
            {
                warnings.Add(L.F(
                    "“{0}”由启动器或脚本间接拉起，进程匹配不可靠；操作可能失败或影响其他进程。",
                    entry.Name));
            }

            if (operation == ImpactOperation.Stop)
            {
                warnings.Add(entry.Kind switch
                {
                    EntryKind.WindowsService => L.T("将向 Windows 服务控制管理器发送停止请求。"),
                    EntryKind.ScheduledTask => L.T("将结束该计划任务当前正在运行的实例。"),
                    _ => L.T("将结束该启动项关联的正在运行进程。未保存的数据可能会丢失。")
                });
            }
            else if (operation == ImpactOperation.Restart)
            {
                warnings.Add(entry.Kind switch
                {
                    EntryKind.WindowsService => L.T("将先停止该服务，然后立即重新启动。"),
                    EntryKind.ScheduledTask => L.T("将结束正在运行的实例，然后重新触发该计划任务。"),
                    _ => L.T("将结束关联进程，然后重新执行该启动项。")
                });
            }

            if (operation == ImpactOperation.Delete)
            {
                snapshot = true;
                if (entry.Kind == EntryKind.WindowsService)
                {
                    warnings.Add(L.T("删除 Windows 服务是永久操作，删除后服务会从服务列表中移除。"));
                    requiresTyped = true;
                    typedTarget ??= entry.ServiceName ?? entry.Name;
                }

                if (entry.Kind == EntryKind.ScheduledTask
                    && entry.TaskPath?.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase) == true)
                {
                    warnings.Add(L.T("这是位于 \\Microsoft\\ 下的系统计划任务。"));
                    requiresTyped = true;
                    typedTarget ??= entry.Name;
                }
            }
        }

        if (snapshot)
        {
            warnings.Add(L.T("删除前会在本地保存一份项目快照。"));
        }

        return new ImpactPreview
        {
            Operation = operation,
            OperationName = OperationLabel(operation),
            Items = items,
            Warnings = warnings.Distinct(StringComparer.Ordinal).ToList(),
            RequiresTypedConfirmation = requiresTyped,
            TypedConfirmationTarget = typedTarget,
            SnapshotWillBeCreated = snapshot
        };
    }

    public static ImpactPreview BuildForAdd(AddEntryRequest request)
    {
        var warnings = new List<string>();
        var requiresAdmin = request.Mode != AddStartupMode.CurrentUserLogon;
        if (requiresAdmin)
        {
            warnings.Add(L.T("该启动方式需要管理员权限。"));
        }

        if (request.Mode == AddStartupMode.WindowsService && request.Type != AddEntryType.Executable)
        {
            warnings.Add(L.T("Windows 服务只能由可执行文件创建。"));
        }

        if (request.Mode == AddStartupMode.SystemBootTask)
        {
            warnings.Add(L.T("任务会以 SYSTEM 身份在开机时运行，无法与桌面交互。"));
        }

        var item = new ImpactItem
        {
            StableId = "preview:add",
            Name = request.Name,
            CategoryLabel = request.Type switch
            {
                AddEntryType.PowerShell => L.T("PowerShell 脚本"),
                AddEntryType.CommandPrompt => L.T("CMD 脚本"),
                _ => L.T("可执行文件")
            },
            CurrentState = "—",
            TargetState = L.T("将创建"),
            Matchability = Matchability.Unknown,
            CanExecute = true
        };

        return new ImpactPreview
        {
            Operation = ImpactOperation.Add,
            OperationName = OperationLabel(ImpactOperation.Add),
            Items = { item },
            Warnings = warnings,
            RequiresTypedConfirmation = false,
            SnapshotWillBeCreated = false
        };
    }

    private static string OperationLabel(ImpactOperation operation) => operation switch
    {
        ImpactOperation.Stop => L.T("停止启动项"),
        ImpactOperation.Restart => L.T("重启启动项"),
        ImpactOperation.Enable => L.T("启用启动项"),
        ImpactOperation.Disable => L.T("禁用启动项"),
        ImpactOperation.Delete => L.T("删除启动项"),
        ImpactOperation.Add => L.T("新增启动项"),
        _ => L.T("操作")
    };

    private static string TargetLabel(ImpactOperation operation) => operation switch
    {
        ImpactOperation.Stop => L.T("已停止"),
        ImpactOperation.Restart => L.T("运行中"),
        ImpactOperation.Enable => L.T("已启用"),
        ImpactOperation.Disable => L.T("已禁用"),
        ImpactOperation.Delete => L.T("已删除"),
        ImpactOperation.Add => L.T("将创建"),
        _ => L.T("未知")
    };
}
