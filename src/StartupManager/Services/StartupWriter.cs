/*
 * Copyright 2026 Xiayue
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using StartupManager.Localization;
using StartupManager.Models;

namespace StartupManager.Services;

public sealed class StartupWriter
{
    private static readonly Regex InvalidFileChars = new(@"[<>:""/\\|?*\x00-\x1F]", RegexOptions.Compiled);
    private static readonly Regex ServiceSlugChars = new(@"[^A-Za-z0-9_.-]", RegexOptions.Compiled);

    public AddEntryResult Add(AddEntryRequest request)
    {
        string? generatedScript = null;
        try
        {
            Validate(request);
            if (request.Mode != AddStartupMode.CurrentUserLogon && !AdminService.IsAdministrator())
            {
                throw new UnauthorizedAccessException(
                    L.T("该启动方式需要管理员权限。请点击左下角的“以管理员身份重启”，然后再添加。"));
            }

            if (request.Mode == AddStartupMode.WindowsService && request.Type != AddEntryType.Executable)
            {
                throw new InvalidOperationException(L.T("Windows 服务只能由可执行文件创建；脚本请选择“系统启动任务”。"));
            }

            var command = BuildCommand(request, out generatedScript);
            AddEntryResult result;
            switch (request.Mode)
            {
                case AddStartupMode.CurrentUserLogon:
                {
                    var added = StartupRegistryManager.AddRunEntry(request.Name, command, allUsers: false);
                    result = new AddEntryResult
                    {
                        Success = true,
                        Message = L.F("已添加到当前用户登录启动项“{0}”。", added.ValueName),
                        CreatedId = $"run:{added.Hive}:{added.KeyPath}:{added.ValueName}"
                    };
                    break;
                }

                case AddStartupMode.AllUsersLogon:
                {
                    var added = StartupRegistryManager.AddRunEntry(request.Name, command, allUsers: true);
                    result = new AddEntryResult
                    {
                        Success = true,
                        Message = L.F("已添加到所有用户登录启动项“{0}”。", added.ValueName),
                        CreatedId = $"run:{added.Hive}:{added.KeyPath}:{added.ValueName}"
                    };
                    break;
                }

                case AddStartupMode.SystemBootTask:
                {
                    var host = request.Type == AddEntryType.Executable
                        ? request.ExecutablePath
                        : ResolveHost(request.Type, allowUserScoped: false, request.PowerShellHostPath);
                    var arguments = request.Type == AddEntryType.Executable
                        ? request.Arguments
                        : BuildScriptArguments(generatedScript!, request.Type, request.RunHidden);
                    var workingDirectory = request.Type == AddEntryType.Executable
                        ? Path.GetDirectoryName(request.ExecutablePath) ?? string.Empty
                        : Path.GetDirectoryName(generatedScript!) ?? string.Empty;

                    var taskPath = ScheduledTaskManager.CreateBootTask(
                        request.Name,
                        host,
                        arguments,
                        workingDirectory,
                        L.F("由“{1}”创建的系统启动任务：{0}", request.Name, AppInfo.ProductName));
                    result = new AddEntryResult
                    {
                        Success = true,
                        Message = L.F("已创建系统启动任务“{0}”。", taskPath),
                        CreatedId = $"task:{taskPath}"
                    };
                    break;
                }

                case AddStartupMode.WindowsService:
                {
                    var serviceName = CreateUniqueServiceName(request.Name);
                    try
                    {
                        WindowsServiceManager.CreateService(
                            serviceName,
                            request.Name,
                            request.ExecutablePath,
                            request.Arguments,
                            L.F("由“{0}”创建：{1}", AppInfo.ProductName, request.Name),
                            request.StartServiceImmediately);

                        result = new AddEntryResult
                        {
                            Success = true,
                            Message = L.F("已安装 Windows 服务“{0}”（服务名：{1}）。", request.Name, serviceName),
                            CreatedId = $"service:{serviceName}",
                            Warning = request.StartServiceImmediately
                                ? null
                                : L.T("服务已安装但未立即启动。普通桌面程序通常不能作为 Windows 服务运行；如果启动失败，请改用“系统启动任务”。")
                        };
                    }
                    catch (Exception ex) when (WindowsServiceManager.ServiceExists(serviceName))
                    {
                        // The service was created, but starting or describing it failed.
                        // Keep the installed service visible and surface the warning.
                        result = new AddEntryResult
                        {
                            Success = true,
                            Message = L.F("已安装 Windows 服务“{0}”（服务名：{1}）。", request.Name, serviceName),
                            CreatedId = $"service:{serviceName}",
                            Warning = L.F("服务已安装，但后续操作失败：{0}", ex.Message)
                        };
                    }

                    break;
                }

                default:
                    throw new NotSupportedException(L.T("未知的启动方式。"));
            }

            AuditLog.Write("ADD", $"{request.Mode} | {request.Name} | {command}");
            generatedScript = null; // Ownership transferred to the startup entry.
            return result;
        }
        catch (Exception ex)
        {
            if (generatedScript is not null)
            {
                TryDelete(generatedScript);
            }

            AuditLog.Write("ADD FAILED", $"{request.Mode} | {request.Name} | {ex.Message}");
            return new AddEntryResult
            {
                Success = false,
                Message = ex.Message
            };
        }
    }

    public static string ResolvePowerShellHost(bool allowUserScoped = false)
        => PowerShellHostDetector.GetDefaultPath(allowUserScoped);

    public static string ResolveHost(
        AddEntryType type,
        bool allowUserScoped = false,
        string? requestedPowerShellHost = null)
    {
        if (type == AddEntryType.CommandPrompt)
        {
            return Path.Combine(Environment.SystemDirectory, "cmd.exe");
        }

        if (type != AddEntryType.PowerShell)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(requestedPowerShellHost))
        {
            if (!PowerShellHostDetector.IsAllowedHost(requestedPowerShellHost, allowUserScoped))
            {
                throw new InvalidOperationException(
                    L.T("所选 PowerShell 版本不适用于当前启动方式，请重新选择。"));
            }

            return requestedPowerShellHost;
        }

        return PowerShellHostDetector.GetDefaultPath(allowUserScoped);
    }

    public static string BuildScriptArguments(string scriptPath, AddEntryType type, bool hidden)
    {
        if (type == AddEntryType.PowerShell)
        {
            var hiddenArg = hidden ? " -WindowStyle Hidden" : string.Empty;
            return $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass{hiddenArg} -File \"{scriptPath}\"";
        }

        if (type == AddEntryType.CommandPrompt)
        {
            return $"/d /c \"\"{scriptPath}\"\"";
        }

        return string.Empty;
    }

    private static string BuildCommand(AddEntryRequest request, out string? generatedScript)
    {
        generatedScript = null;

        if (request.Type == AddEntryType.Executable)
        {
            if (!File.Exists(request.ExecutablePath))
            {
                throw new FileNotFoundException(L.T("可执行文件不存在，请重新选择。"), request.ExecutablePath);
            }

            return CommandLineParser.BuildCommandLine(request.ExecutablePath, request.Arguments);
        }

        if (string.IsNullOrWhiteSpace(request.ScriptContent))
        {
            throw new InvalidOperationException(L.T("脚本文本不能为空。"));
        }

        var folder = request.Mode is AddStartupMode.SystemBootTask or AddStartupMode.AllUsersLogon
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "StartupManager", "Scripts")
            : AppPaths.Scripts;
        AppPaths.EnsureDirectory(folder);

        var extension = request.Type == AddEntryType.PowerShell ? ".ps1" : ".cmd";
        var fileName = SanitizeFileName(request.Name);
        var scriptPath = Path.Combine(folder, $"{fileName}-{DateTime.Now:yyyyMMdd-HHmmss}-{RandomSuffix()}{extension}");

        if (request.Type == AddEntryType.PowerShell)
        {
            File.WriteAllText(scriptPath, request.ScriptContent, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        else
        {
            // cmd.exe does not tolerate a UTF-8 BOM before the first command.
            File.WriteAllText(scriptPath, request.ScriptContent, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        generatedScript = scriptPath;
        var host = ResolveHost(
            request.Type,
            allowUserScoped: request.Mode == AddStartupMode.CurrentUserLogon,
            requestedPowerShellHost: request.PowerShellHostPath);
        var arguments = BuildScriptArguments(scriptPath, request.Type, request.RunHidden);
        return CommandLineParser.BuildCommandLine(host, arguments);
    }

    private static string CreateUniqueServiceName(string displayName)
    {
        var slug = ServiceSlugChars.Replace(displayName, string.Empty).Trim('.', '_', '-');
        if (slug.Length > 40) slug = slug[..40];
        if (slug.Length < 2) slug = "App";

        var seed = $"{displayName}|{DateTime.UtcNow.Ticks}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed)))[..6];
        var baseName = $"StartupManager_{slug}_{hash}";
        var name = baseName;
        var suffix = 2;
        while (WindowsServiceManager.ServiceExists(name))
        {
            name = $"{baseName}_{suffix++}";
        }

        return name;
    }

    private static void Validate(AddEntryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new InvalidOperationException(L.T("请填写启动项名称。"));
        }

        if (request.Name.Length > 80)
        {
            throw new InvalidOperationException(L.T("名称不能超过 80 个字符。"));
        }

        if (request.Type == AddEntryType.Executable && string.IsNullOrWhiteSpace(request.ExecutablePath))
        {
            throw new InvalidOperationException(L.T("请选择可执行文件。"));
        }

        if (request.Type != AddEntryType.Executable && string.IsNullOrWhiteSpace(request.ScriptContent))
        {
            throw new InvalidOperationException(L.T("请输入脚本内容。"));
        }
    }

    private static string SanitizeFileName(string value)
    {
        var result = InvalidFileChars.Replace(value, "_").Trim(' ', '.', '_');
        if (result.Length == 0) result = "startup-script";
        if (result.Length > 48) result = result[..48];
        return result;
    }

    private static string RandomSuffix()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToLowerInvariant();

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Best effort cleanup.
        }
    }
}
