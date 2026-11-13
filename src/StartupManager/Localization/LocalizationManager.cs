using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using StartupManager.Services;

namespace StartupManager.Localization;

public enum AppLanguage
{
    ChineseSimplified,
    EnglishUnitedKingdom
}

/// <summary>
/// Lightweight runtime localisation. Chinese source strings are the keys, so
/// every XAML literal can be wrapped with {loc:Loc ...} and every code message
/// can call L.T/L.F without maintaining a second set of key names.
/// </summary>
public sealed class LocalizationManager : INotifyPropertyChanged
{
    private static readonly string FilePath = Path.Combine(AppPaths.Root, "language.json");
    private static readonly Dictionary<string, string> English = new(StringComparer.Ordinal)
    {
        // Main window
        ["启航 · 开机启动管理器"] = "Qihang · Startup Manager",
        ["启航"] = "Qihang",
        ["分类"] = "Categories",
        ["全部项目"] = "All items",
        ["Windows 服务"] = "Windows services",
        ["登录启动项"] = "Logon items",
        ["计划任务"] = "Scheduled tasks",
        ["已禁用项目"] = "Disabled items",
        ["管理员模式"] = "Administrator mode",
        ["标准权限"] = "Standard user",
        ["全部功能已解锁"] = "All system actions unlocked",
        ["部分系统级操作需要提权"] = "Some actions need admin",
        ["全部系统级功能已解锁"] = "All system-level actions are unlocked",
        ["以管理员身份重启"] = "Restart as administrator",
        ["启动项目"] = "Startup items",
        ["搜索启动项"] = "Search startup items",
        ["搜索名称、命令、路径..."] = "Search name, command or path…",
        ["刷新列表"] = "Refresh list",
        ["刷新列表（会重新读取所有启动项）"] = "Refresh list (re-reads every startup item)",
        ["导出"] = "Export",
        ["导出当前列表为 CSV"] = "Export current list as CSV",
        ["把当前筛选结果导出为 CSV 文件"] = "Export the current filtered results to a CSV file",
        ["新增启动项"] = "Add startup item",
        ["设置"] = "Settings",
        ["打开设置"] = "Open settings",
        ["切换深色或浅色主题"] = "Switch light or dark theme",
        ["切换到深色模式"] = "Switch to dark mode",
        ["切换到浅色模式"] = "Switch to light mode",
        ["已切换到深色模式"] = "Switched to dark mode",
        ["已切换到浅色模式"] = "Switched to light mode",
        ["最小化"] = "Minimise",
        ["最大化"] = "Maximise",
        ["关闭窗口"] = "Close window",
        ["关闭"] = "Close",
        ["全部"] = "All",
        ["运行中"] = "Running",
        ["未运行"] = "Not running",
        ["已禁用"] = "Disabled",
        ["需要管理员"] = "Administrator",
        ["重启"] = "Restart",
        ["重启当前项目"] = "Restart current item",
        ["停止或启动当前项目"] = "Stop or start current item",
        ["禁用或启用当前项目"] = "Disable or enable current item",
        ["重启此项目"] = "Restart this item",
        ["停止或启动此项目"] = "Stop or start this item",
        ["禁用或启用此项目"] = "Disable or enable this item",
        ["删除此项目"] = "Delete this item",
        ["重启 / 重新触发"] = "Restart / trigger again",
        ["删除"] = "Delete",
        ["启动方式"] = "Startup type",
        ["作用范围"] = "Scope",
        ["触发时机"] = "Trigger",
        ["进程"] = "Process",
        ["启动命令"] = "Startup command",
        ["来源位置"] = "Source location",
        ["打开文件位置"] = "Open file location",
        ["删除项目"] = "Delete item",
        ["删除前会自动保存一份项目快照到本地备份目录。"] = "A snapshot is saved to the local backup folder before deletion.",
        ["选择一个启动项目"] = "Select a startup item",
        ["在左侧点击项目，查看详细信息并执行操作。"] = "Choose an item on the left to view details and actions.",
        ["需提权"] = "Elevated",
        ["正在准备扫描…"] = "Preparing scan…",
        ["正在扫描启动项目…"] = "Scanning startup items…",
        ["尚未扫描"] = "Not scanned yet",
        ["尚未发现启动项目"] = "No startup items found",
        ["没有符合条件的项目"] = "No matching items",
        ["点击右上角“新增启动项”创建第一个项目。"] = "Choose “Add startup item” in the top right to create one.",
        ["试试更换分类、清除搜索，或切换状态筛选。"] = "Try another category, clear the search, or change the status filter.",
        ["已取消管理员提权。"] = "Administrator elevation was cancelled.",
        ["提权失败：{0}"] = "Elevation failed: {0}",
        ["打开文件位置失败：{0}"] = "Could not open the file location: {0}",
        ["找不到可打开的文件位置，可能该项目已经不存在。"] = "No file location to open; the item may no longer exist.",
        ["导出启动项为 CSV"] = "Export startup items as CSV",
        ["CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*"] = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
        ["已导出 {0} 项到 {1}"] = "Exported {0} items to {1}",
        ["导出失败：{0}"] = "Export failed: {0}",
        ["扫描启动项目失败：{0}"] = "Could not scan startup items: {0}",
        ["需要管理员权限"] = "Administrator rights required",
        ["“{0}”属于系统级项目。要执行此操作，需要以管理员身份重新启动启航。"] = "“{0}” is a system-level item. Restart Qihang as administrator to perform this action.",
        ["重新启动后，请再次执行刚才的操作。当前窗口会被关闭。"] = "Run the action again after the restart. This window will be closed.",
        ["以管理员身份重启？"] = "Restart as administrator?",
        ["启航会关闭当前窗口并请求 Windows 提权。提权后即可管理 Windows 服务、系统启动任务和所有用户启动项。"] = "Qihang will close this window and request Windows elevation. You can then manage Windows services, system startup tasks and all-user startup items.",
        ["重启并提权"] = "Restart and elevate",
        ["重启“{0}”？"] = "Restart “{0}”?",
        ["将先停止该服务，然后立即重新启动。正在使用该服务的程序可能会短暂中断。"] = "The service will be stopped and started again. Programs using it may be briefly interrupted.",
        ["将先结束正在运行的任务实例，然后重新触发该计划任务。"] = "The running task instance will be ended, then the scheduled task will be triggered again.",
        ["正在重启…"] = "Restarting…",
        ["停止“{0}”？"] = "Stop “{0}”?",
        ["将向 Windows 服务控制管理器发送停止请求。正在使用该服务的程序可能会中断。"] = "A stop request will be sent to Windows Service Control Manager. Programs using the service may be interrupted.",
        ["将结束该计划任务当前正在运行的实例。"] = "The currently running instance of the scheduled task will be ended.",
        ["将结束该启动项关联的正在运行进程。未保存的数据可能会丢失。"] = "The processes associated with this startup item will be ended. Unsaved data may be lost.",
        ["停止"] = "Stop",
        ["正在停止…"] = "Stopping…",
        ["正在启动…"] = "Starting…",
        ["服务启动类型会被修改；当前运行中的服务进程不会被立即结束。"] = "The service startup type will be changed; a running service process will not be ended immediately.",
        ["计划任务的触发器会被关闭或恢复，当前运行中的实例状态不变。"] = "The scheduled task trigger will be disabled or restored; a running instance is left unchanged.",
        ["只改变下次登录时是否自动运行，不会结束当前正在运行的进程。"] = "Only whether it starts automatically at the next logon changes; running processes continue.",
        ["禁用“{0}”？"] = "Disable “{0}”?",
        ["启用“{0}”？"] = "Enable “{0}”?",
        ["禁用后 Windows 将不再自动启动该项目。当前正在运行的进程不会被结束。"] = "Windows will no longer start this item automatically. Running processes will not be ended.",
        ["启用后该项目将在下次登录或开机时恢复自动启动。"] = "The item will start automatically again at the next logon or boot.",
        ["禁用启动"] = "Disable startup",
        ["启用启动"] = "Enable startup",
        ["正在禁用启动…"] = "Disabling startup…",
        ["正在启用启动…"] = "Enabling startup…",
        ["该项目受到保护，不能删除。"] = "This item is protected and cannot be deleted.",
        ["删除“{0}”？"] = "Delete “{0}”?",
        ["将从服务控制管理器中删除该 Windows 服务。该操作不可撤销，服务将从服务列表中移除。"] = "This Windows service will be deleted from Service Control Manager. This cannot be undone.",
        ["将删除对应的注册表启动值，下次登录不会再自动运行。"] = "The registry startup value will be deleted; it will no longer start at logon.",
        ["将删除启动文件夹中的该项目文件。"] = "The item file will be deleted from the Startup folder.",
        ["将从任务计划程序中删除该项目。"] = "The item will be deleted from Task Scheduler.",
        ["将删除该项目。"] = "This item will be deleted.",
        ["正在删除…"] = "Deleting…",
        ["(默认值)"] = "(Default)",
        ["正在创建…"] = "Creating…",
        ["创建启动项"] = "Create startup item",
        ["检查设置…"] = "Checking settings…",
        ["该项目不支持禁用自动启动。"] = "This item does not support disabling auto-start.",
        ["系统关键服务受保护，不能禁用。"] = "This protected system service cannot be disabled.",
        ["禁用自动启动"] = "Disable auto-start",
        ["启用自动启动"] = "Enable auto-start",
        ["启用"] = "Enable",
        ["禁用"] = "Disable",
        ["启动"] = "Start",

        // Entry kinds and states
        ["注册表启动项"] = "Registry startup item",
        ["启动文件夹"] = "Startup folder",
        ["启动项"] = "Startup item",
        ["服务"] = "Service",
        ["注册表"] = "Registry",
        ["就绪"] = "Ready",
        ["已停止"] = "Stopped",
        ["未知"] = "Unknown",
        ["系统级"] = "System-wide",
        ["当前用户"] = "Current user",
        ["所有用户"] = "All users",
        ["策略启动项"] = "Policy startup item",
        ["注册表 RunOnce"] = "Registry RunOnce",
        ["注册表 Run"] = "Registry Run",
        ["下次登录时运行一次"] = "Runs once at next logon",
        ["登录时自动运行"] = "Runs automatically at logon",
        ["下次登录一次"] = "Once at next logon",
        ["用户登录时"] = "At user logon",
        ["所有用户登录时"] = "At any user logon",
        ["任务计划程序"] = "Task Scheduler",
        ["开机时（延迟）"] = "At boot (delayed)",
        ["开机时"] = "At boot",
        ["手动启动"] = "Manual",
        ["引导启动"] = "Boot start",
        ["系统启动"] = "System start",
        ["策略启动项由组策略控制，不能在此禁用。"] = "Policy startup items are controlled by Group Policy and cannot be disabled here.",
        ["RunOnce 项目会在下次登录后自动移除，不支持禁用。"] = "RunOnce items are removed after the next logon and do not support disabling.",
        ["已在任务管理器或系统设置中禁用"] = "Disabled in Task Manager or Windows Settings",
        ["登录时未运行"] = "Not running at logon",
        ["任务状态：{0}"] = "Task state: {0}",
        ["COM 处理程序（无命令行）"] = "COM handler (no command line)",
        ["{0}触发的计划任务"] = "Scheduled task triggered by {0}",

        // Settings
        ["语言"] = "Language",
        ["界面语言"] = "Interface language",
        ["显示模式"] = "Appearance",
        ["浅色模式"] = "Light",
        ["暗黑模式"] = "Dark",
        ["跟随系统"] = "Follow system",
        ["亮色模式"] = "Light",
        ["危险模式"] = "Danger mode",
        ["危险模式说明"] = "Danger mode",
        ["展开危险设置"] = "Expand danger settings",
        ["收起危险设置"] = "Collapse danger settings",
        ["以下设置会降低系统安全性，请仅在明确了解后果时修改。"] = "The following settings reduce system safety. Only change them if you understand the consequences.",
        ["拒绝删除系统服务"] = "Block deletion of system services",
        ["开启后，受保护的系统服务不能被删除。"] = "When enabled, protected system services cannot be deleted.",
        ["关闭后，仍会保留受保护标记，但允许删除受保护服务。"] = "When disabled, protected services keep their protected marker but can be deleted.",
        ["受保护的系统服务"] = "Protected system services",
        ["每行一个服务名（不是显示名）。"] = "One service name per line (not the display name).",
        ["恢复默认名单"] = "Restore default list",
        ["此名单只控制系统服务的删除保护，不会停止或禁用任何服务。"] = "This list only controls deletion protection for services; it does not stop or disable them.",
        ["保存"] = "Save",
        ["取消"] = "Cancel",
        ["设置已保存。"] = "Settings saved.",
        ["危险模式已折叠"] = "Danger mode is collapsed",
        ["简体中文"] = "简体中文",
        ["English (United Kingdom)"] = "English (United Kingdom)",
        ["跟随 Windows 的亮色/暗黑设置。"] = "Follow the Windows light/dark setting.",
        ["重启应用后，语言会完全生效。"] = "Language changes apply immediately; newly generated messages use the new language.",
        ["受保护服务名单已恢复默认。"] = "The protected service list has been restored to defaults.",
        ["服务名列表不能为空。"] = "The service name list cannot be empty.",
        ["设置保存失败：{0}"] = "Could not save settings: {0}",

        // Add dialog
        ["脚本、程序或标准 Windows 服务，都可以交给启航托管。"] = "Scripts, programs and standard Windows services can all be managed by Qihang.",
        ["项目名称"] = "Item name",
        ["例如：家庭 NAS 同步助手"] = "For example: Home NAS sync helper",
        ["用于在列表中识别该项目，也会作为计划任务和注册表值的名称。"] = "Used to identify the item and as the task or registry value name.",
        ["启动类型"] = "Startup type",
        ["编写 .ps1 脚本"] = "Write a .ps1 script",
        ["CMD 脚本"] = "CMD script",
        ["编写 .cmd 脚本"] = "Write a .cmd script",
        ["可执行文件"] = "Executable",
        ["运行 .exe 程序"] = "Run an .exe program",
        ["脚本内容"] = "Script content",
        ["插入示例"] = "Insert example",
        ["从文件导入"] = "Import from file",
        ["PowerShell 版本"] = "PowerShell version",
        ["检测到多个 PowerShell，请选择启动脚本时使用的版本。"] = "Multiple PowerShell installations found. Choose the one used to run this script.",
        ["在这里编写脚本…"] = "Write your script here…",
        ["脚本会保存到本地，并以隐藏窗口方式运行。"] = "The script is saved locally and runs in a hidden window.",
        ["CMD 脚本可能会短暂显示控制台窗口，建议在脚本内使用 start /min。"] = "A CMD script may briefly show a console window; use start /min inside the script.",
        ["隐藏窗口"] = "Hide window",
        ["尚未选择文件"] = "No file selected",
        ["浏览…"] = "Browse…",
        ["启动参数（可选）"] = "Startup arguments (optional)",
        ["例如：--background --profile work"] = "For example: --background --profile work",
        ["创建后立即启动服务"] = "Start the service immediately after creation",
        ["登录时启动 · 当前用户"] = "Start at logon · Current user",
        ["写入当前用户的 Run 注册表项，不需要管理员权限。"] = "Writes to the current user's Run key; no administrator rights required.",
        ["推荐"] = "Recommended",
        ["登录时启动 · 所有用户"] = "Start at logon · All users",
        ["写入 HKLM Run，需要管理员权限；任意用户登录都会启动。"] = "Writes to HKLM Run; requires administrator rights. Starts for every user.",
        ["需管理员"] = "Administrator required",
        ["系统启动任务 · SYSTEM"] = "System startup task · SYSTEM",
        ["创建开机触发的计划任务，在用户登录前以 SYSTEM 身份运行。"] = "Creates a boot-triggered task that runs as SYSTEM before user logon.",
        ["安装为 Windows 服务"] = "Install as a Windows service",
        ["仅支持 EXE。普通桌面程序通常不能作为服务运行，请谨慎选择。"] = "EXE only. Ordinary desktop programs usually cannot run as services; choose carefully.",
        ["仅 EXE"] = "EXE only",
        ["所有操作均在本地完成"] = "Everything runs locally",
        ["该方式会写入 HKEY_LOCAL_MACHINE，需要管理员权限。普通权限下创建会被拒绝。"] = "This writes to HKEY_LOCAL_MACHINE and requires administrator rights. Creation is refused without elevation.",
        ["任务会以 SYSTEM 身份在开机时运行，无法与桌面交互。若程序需要显示窗口，请选择“登录时启动”。"] = "The task runs as SYSTEM at boot and cannot interact with the desktop. If it needs a window, choose a logon startup mode.",
        ["Windows 服务只适用于实现了服务接口的 EXE。普通桌面程序大概率启动失败（错误 1053），建议优先选择“系统启动任务”。"] = "Windows services must be EXEs that implement the service interface. Ordinary desktop programs usually fail with error 1053; prefer a system startup task.",
        ["选择脚本文档"] = "Choose a script file",
        ["PowerShell 脚本 (*.ps1)|*.ps1|所有文件 (*.*)|*.*"] = "PowerShell scripts (*.ps1)|*.ps1|All files (*.*)|*.*",
        ["CMD 脚本 (*.cmd;*.bat)|*.cmd;*.bat|所有文件 (*.*)|*.*"] = "CMD scripts (*.cmd;*.bat)|*.cmd;*.bat|All files (*.*)|*.*",
        ["选择可执行文件"] = "Choose an executable",
        ["可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*"] = "Executables (*.exe)|*.exe|All files (*.*)|*.*",
        ["读取脚本失败：{0}"] = "Could not read the script: {0}",
        ["请填写项目名称。"] = "Enter an item name.",
        ["该启动方式需要管理员权限。请先关闭本窗口，点击左下角“以管理员身份重启”，然后再创建。"] = "This startup mode requires administrator rights. Close this window, choose “Restart as administrator” at the bottom left, then try again.",
        ["请选择一个存在的可执行文件。"] = "Choose an existing executable.",
        ["请输入脚本内容，或点击“从文件导入”。"] = "Enter script content or choose “Import from file”.",
        ["Windows 服务只能由可执行文件创建。请切换到“系统启动任务”来运行脚本。"] = "Windows services can only be created from an executable. Use a system startup task for scripts.",

        // Confirm dialog
        ["确认操作"] = "Confirm action",
        ["操作确认"] = "Confirm action",
        ["确认"] = "Confirm",
        ["为了防止误操作，请输入 {0} 以确认删除："] = "To prevent mistakes, type {0} to confirm deletion:",
        ["输入的内容与项目名称不一致。"] = "The text does not match the item name.",

        // Status/notification messages
        ["已禁用服务“{0}”的自动启动。当前运行中的进程不会被结束。"] = "Disabled automatic startup for service “{0}”. Running processes were not ended.",
        ["已启用服务“{0}”，启动类型：{1}{2}。"] = "Enabled service “{0}”; startup type: {1}{2}.",
        ["（未找到原启动类型，已恢复为手动启动）"] = " (original startup type not found; restored to Manual)",
        ["已启用计划任务“{0}”。"] = "Enabled scheduled task “{0}”.",
        ["已禁用计划任务“{0}”的触发器。"] = "Disabled triggers for scheduled task “{0}”.",
        ["已启用注册表启动项“{0}”。"] = "Enabled registry startup item “{0}”.",
        ["已禁用注册表启动项“{0}”，下次登录不再自动运行。"] = "Disabled registry startup item “{0}”; it will not start at the next logon.",
        ["已启用启动文件夹项目“{0}”。"] = "Enabled Startup-folder item “{0}”.",
        ["已禁用启动文件夹项目“{0}”，下次登录不再自动运行。"] = "Disabled Startup-folder item “{0}”; it will not start at the next logon.",
        ["已停止服务“{0}”。"] = "Stopped service “{0}”.",
        ["已结束计划任务“{0}”。"] = "Ended scheduled task “{0}”.",
        ["已结束 {0} 个关联进程。"] = "Ended {0} associated processes.",
        ["已启动“{0}”。"] = "Started “{0}”.",
        ["服务“{0}”已启动。"] = "Service “{0}” started.",
        ["已触发计划任务“{0}”。"] = "Triggered scheduled task “{0}”.",
        ["服务“{0}”已重启。"] = "Service “{0}” restarted.",
        ["计划任务“{0}”已重新触发。"] = "Scheduled task “{0}” triggered again.",
        ["已结束 {0} 个进程并重新启动“{1}”。"] = "Ended {0} processes and restarted “{1}”.",
        ["“{0}”之前未在运行，已直接启动。"] = "“{0}” was not running; started directly.",
        ["服务“{0}”已删除。该服务将从下次启动时消失。"] = "Service “{0}” deleted. It will disappear after the next restart.",
        ["项目“{0}”已删除。"] = "Item “{0}” deleted.",
        ["该项目当前已禁用，请先启用后再启动。"] = "This item is disabled; enable it before starting.",
        ["该项目当前已禁用，请先启用后再重启。"] = "This item is disabled; enable it before restarting.",
        ["没有发现正在运行的关联进程，可能它已经停止。"] = "No associated running processes were found; it may already have stopped.",
        ["该系统服务正在运行，删除前会先尝试停止。"] = "This system service is running; it will be stopped before deletion.",
        ["该项目当前已禁用，请先启用后再禁用。"] = "This item is disabled already.",
        ["该项目当前已启用。"] = "This item is already enabled.",
        ["{0} 个启动项目 · {1} 个正在运行"] = "{0} startup items · {1} running",
        ["{0} 项"] = "{0} items",
        ["{0} item"] = "{0} item",
        ["{0} items"] = "{0} items",
        ["更新于 {0}"] = "Updated at {0}",
        ["扫描完成，共 {0} 项"] = "Scan complete: {0} items",
        ["正在并行读取系统服务与启动项…"] = "Reading services and startup items in parallel…",
        ["进程信息读取失败：{0}"] = "Could not read process information: {0}",
        ["Windows 服务读取失败：{0}"] = "Could not read Windows services: {0}",
        ["注册表启动项读取失败：{0}"] = "Could not read registry startup items: {0}",
        ["启动文件夹读取失败：{0}"] = "Could not read the Startup folder: {0}",
        ["计划任务读取失败：{0}"] = "Could not read scheduled tasks: {0}",
        ["当前用户登录时自动运行"] = "Runs automatically when the current user logs on",
        ["所有用户登录时自动运行"] = "Runs automatically when any user logs on",
        ["系统服务保护名单中的服务"] = "Service is on the system protection list.",
        ["编辑受保护服务名单，并控制系统服务删除保护。"] = "Edit the protected-service list and control deletion protection.",
        ["恢复默认名单会重新保护 Windows 核心服务。"] = "Restoring the default list protects core Windows services again.",
        ["选择窗口的亮色、暗黑或跟随系统外观。"] = "Choose a light, dark or system-following appearance.",
        ["语言、外观和系统服务保护。"] = "Language, appearance and system-service protection.",
        ["禁用此启动项"] = "Disable this startup item",
        ["启用此启动项"] = "Enable this startup item",
        ["切换深色模式"] = "Switch to dark mode",
        ["v1.4.1  ·  本地运行，数据不上传"] = "v1.4.1  ·  Local only, data stays on this PC",
        ["；"] = "; ",
        ["自动（延迟）"] = "Automatic (delayed)",
        ["自动"] = "Automatic",
        ["手动"] = "Manual",
        ["正在启动"] = "Starting",
        ["正在停止"] = "Stopping",
        ["正在继续"] = "Resuming",
        ["正在暂停"] = "Pausing",
        ["已暂停"] = "Paused",
        ["排队中"] = "Queued",
        ["登录时"] = "At logon",
        ["当前用户安装"] = "Current user installation",
        ["系统安装"] = "System installation",
        ["该启动项受到保护，不能禁用。"] = "This startup item is protected and cannot be disabled.",
        ["该项目已经处于启用状态。"] = "This item is already enabled.",
        ["该项目已经处于禁用状态。"] = "This item is already disabled.",
        ["名称不能超过 80 个字符。"] = "The name cannot exceed 80 characters.",
        ["启动文件不存在，可能已被移动或删除。"] = "The startup file does not exist; it may have been moved or deleted.",
        ["请输入脚本内容。"] = "Enter script content.",
        ["请填写启动项名称。"] = "Enter a startup item name.",
        ["请选择可执行文件。"] = "Choose an executable.",
        ["不支持的操作类型。"] = "This operation is not supported.",
        ["未知的启动方式。"] = "Unknown startup mode.",
        ["无法从该启动项中解析出可执行文件。"] = "Could not resolve an executable from this startup item.",
        ["无法结束关联进程。{0}"] = "Could not end the associated processes. {0}",
        ["PID {0} 未在 5 秒内退出"] = "PID {0} did not exit within 5 seconds",
        ["PID {0}：{1}"] = "PID {0}: {1}",
        ["已安装 Windows 服务“{0}”（服务名：{1}）。"] = "Installed Windows service “{0}” (service name: {1}).",
        ["已创建系统启动任务“{0}”。"] = "Created system startup task “{0}”.",
        ["已删除计划任务“{0}”。"] = "Deleted scheduled task “{0}”.",
        ["已删除启动文件“{0}”。"] = "Deleted startup file “{0}”.",
        ["已删除注册表启动项“{0}”。"] = "Deleted registry startup item “{0}”.",
        ["已添加到当前用户登录启动项“{0}”。"] = "Added to the current user's logon startup items as “{0}”.",
        ["已添加到所有用户登录启动项“{0}”。"] = "Added to all users' logon startup items as “{0}”.",
        ["由“启航”创建：{0}"] = "Created by Qihang: {0}",
        ["由“启航”创建的系统启动任务：{0}"] = "System startup task created by Qihang: {0}",
        ["该启动方式需要管理员权限。请点击左下角的“以管理员身份重启”，然后再添加。"] = "This startup mode requires administrator rights. Choose “Restart as administrator” at the bottom left, then try again.",
        ["“{0}”需要管理员权限。请点击左下角的“以管理员身份重启”，然后再试一次。"] = "“{0}” requires administrator rights. Choose “Restart as administrator” at the bottom left, then try again.",
        ["服务已安装，但后续操作失败：{0}"] = "The service was installed, but a later step failed: {0}",
        ["服务已安装但未立即启动。普通桌面程序通常不能作为 Windows 服务运行；如果启动失败，请改用“系统启动任务”。"] = "The service was installed but not started. Ordinary desktop programs usually cannot run as Windows services; if it fails to start, use a system startup task instead.",
        ["Windows 服务只能由可执行文件创建；脚本请选择“系统启动任务”。"] = "Windows services can only be created from an executable; use a system startup task for scripts.",
        ["程序遇到未处理的错误：\n\n{0}\n\n详细信息已写入本地日志。"] = "An unhandled error occurred:\n\n{0}\n\nDetails have been written to the local log.",
        ["启航 · 发生错误"] = "Qihang · Error",
        ["所选 PowerShell 版本不适用于当前启动方式，请重新选择。"] = "The selected PowerShell version is not valid for this startup mode. Choose another one.",
        ["可执行文件不存在，请重新选择。"] = "The executable does not exist. Choose it again.",
        ["脚本文本不能为空。"] = "The script cannot be empty."
    };

    private static readonly LocalizationManager Shared = new();

    public static LocalizationManager Instance => Shared;
    public static AppLanguage CurrentLanguage { get; private set; } = AppLanguage.ChineseSimplified;
    public static bool IsEnglish => CurrentLanguage == AppLanguage.EnglishUnitedKingdom;
    public static event EventHandler? LanguageChanged;

    private LocalizationManager()
    {
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => Get(key);

    public static void Initialize()
    {
        var language = Load();
        SetLanguage(language, persist: false);
    }

    public static void SetLanguage(AppLanguage language, bool persist = true)
    {
        CurrentLanguage = language;
        var cultureName = language == AppLanguage.EnglishUnitedKingdom ? "en-GB" : "zh-CN";
        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
        catch
        {
            // Culture data is optional; text still switches.
        }

        if (persist)
        {
            Save(language);
        }

        Shared.PropertyChanged?.Invoke(
            Shared,
            new PropertyChangedEventArgs("Item[]"));
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    public static string T(string key) => Get(key);

    public static string F(string key, params object?[] args)
    {
        var template = Get(key);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    private static string Get(string key)
    {
        if (key is null) return string.Empty;
        if (!IsEnglish) return key;
        return English.TryGetValue(key, out var value) ? value : key;
    }

    private static AppLanguage Load()
    {
        try
        {
            var path = Path.Combine(AppPaths.Root, "language.json");
            if (!File.Exists(path)) return AppLanguage.ChineseSimplified;
            var settings = JsonSerializer.Deserialize<LanguageSettings>(File.ReadAllText(path));
            return settings?.Language == "en-GB"
                ? AppLanguage.EnglishUnitedKingdom
                : AppLanguage.ChineseSimplified;
        }
        catch
        {
            return AppLanguage.ChineseSimplified;
        }
    }

    private static void Save(AppLanguage language)
    {
        try
        {
            AppPaths.EnsureDirectory(AppPaths.Root);
            var path = Path.Combine(AppPaths.Root, "language.json");
            File.WriteAllText(
                path,
                JsonSerializer.Serialize(
                    new LanguageSettings
                    {
                        Language = language == AppLanguage.EnglishUnitedKingdom ? "en-GB" : "zh-CN"
                    },
                    new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Language persistence is best effort.
        }
    }

    private sealed class LanguageSettings
    {
        public string Language { get; set; } = "zh-CN";
    }
}
