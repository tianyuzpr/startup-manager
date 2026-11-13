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
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using StartupManager.Localization;
using StartupManager.Models;
using StartupManager.Services;

namespace StartupManager.Dialogs;

public partial class AddEntryWindow : Window
{
    private readonly List<PowerShellHostOption> _powerShellHosts;
    private bool _initialized;
    private bool _creating;
    private string? _selectedPowerShellHostPath;

    public AddEntryWindow()
    {
        InitializeComponent();
        _powerShellHosts = PowerShellHostDetector.Detect(includeUserScoped: true).ToList();
        _selectedPowerShellHostPath = _powerShellHosts.FirstOrDefault()?.Path;
        _initialized = true;
        UpdateTypeUI();
    }

    public string? CreatedId { get; private set; }
    public string CreatedMessage { get; private set; } = string.Empty;
    public string? Warning { get; private set; }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        NameBox.Focus();
    }

    private void Type_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        UpdateTypeUI();
    }

    private void Mode_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        UpdateModeUI();
    }

    private void UpdateTypeUI()
    {
        var isExe = TypeExe.IsChecked == true;
        var isPowerShell = TypePowerShell.IsChecked == true;
        var isCmd = TypeCmd.IsChecked == true;

        ScriptSection.Visibility = isExe ? Visibility.Collapsed : Visibility.Visible;
        ExecutableSection.Visibility = isExe ? Visibility.Visible : Visibility.Collapsed;
        RunHiddenCheck.Visibility = isPowerShell ? Visibility.Visible : Visibility.Collapsed;
        ScriptHint.Text = isPowerShell
            ? L.T("脚本会保存到本地，并以隐藏窗口方式运行。")
            : L.T("CMD 脚本可能会短暂显示控制台窗口，建议在脚本内使用 start /min。");

        ModeService.IsEnabled = isExe;
        if (!isExe && ModeService.IsChecked == true)
        {
            ModeCurrentUser.IsChecked = true;
        }

        if (isCmd && ScriptBox.Text.Length == 0)
        {
            ScriptHint.Text = L.T("CMD 脚本可能会短暂显示控制台窗口，建议在脚本内使用 start /min。");
        }

        UpdateModeUI();
    }

    private void UpdateModeUI()
    {
        var isService = ModeService.IsChecked == true;
        StartServiceCheck.Visibility = isService ? Visibility.Visible : Visibility.Collapsed;

        if (ModeAllUsers.IsChecked == true)
        {
            ModeWarning.Visibility = Visibility.Visible;
            ModeWarningText.Text = L.T("该方式会写入 HKEY_LOCAL_MACHINE，需要管理员权限。普通权限下创建会被拒绝。");
        }
        else if (ModeSystemTask.IsChecked == true)
        {
            ModeWarning.Visibility = Visibility.Visible;
            ModeWarningText.Text = L.T("任务会以 SYSTEM 身份在开机时运行，无法与桌面交互。若程序需要显示窗口，请选择“登录时启动”。");
        }
        else if (isService)
        {
            ModeWarning.Visibility = Visibility.Visible;
            ModeWarningText.Text = L.T("Windows 服务只适用于实现了服务接口的 EXE。普通桌面程序大概率启动失败（错误 1053），建议优先选择“系统启动任务”。");
        }
        else
        {
            ModeWarning.Visibility = Visibility.Collapsed;
        }

        RebuildPowerShellHostOptions();
    }

    private void RebuildPowerShellHostOptions()
    {
        if (PowerShellHostSection is null) return;

        if (TypePowerShell.IsChecked != true)
        {
            PowerShellHostSection.Visibility = Visibility.Collapsed;
            return;
        }

        var includeUserScoped = ModeCurrentUser.IsChecked == true;
        var options = _powerShellHosts
            .Where(host => includeUserScoped || !host.IsUserScoped)
            .ToList();

        if (options.Count == 0)
        {
            PowerShellHostSection.Visibility = Visibility.Collapsed;
            _selectedPowerShellHostPath = null;
            return;
        }

        if (options.All(host => !string.Equals(
                host.Path, _selectedPowerShellHostPath, StringComparison.OrdinalIgnoreCase)))
        {
            _selectedPowerShellHostPath = options[0].Path;
        }

        if (options.Count == 1)
        {
            _selectedPowerShellHostPath = options[0].Path;
            PowerShellHostSection.Visibility = Visibility.Collapsed;
            return;
        }

        PowerShellHostSection.Visibility = Visibility.Visible;
        PowerShellHostPanel.Children.Clear();

        foreach (var option in options)
        {
            var card = new RadioButton
            {
                GroupName = "PowerShellHost",
                Style = (Style)FindResource("ChoiceCard"),
                Width = 220,
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 8, 8),
                Tag = option.Path,
                IsChecked = string.Equals(
                    option.Path, _selectedPowerShellHostPath, StringComparison.OrdinalIgnoreCase),
                Content = BuildPowerShellHostContent(option)
            };
            AutomationProperties.SetName(card, option.DisplayName);
            card.Checked += (_, _) => _selectedPowerShellHostPath = option.Path;
            PowerShellHostPanel.Children.Add(card);
        }
    }

    private FrameworkElement BuildPowerShellHostContent(PowerShellHostOption option)
    {
        var panel = new StackPanel();
        var title = new TextBlock
        {
            Text = option.DisplayName,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");
        panel.Children.Add(title);

        var source = new TextBlock
        {
            Text = option.SourceLabel,
            FontSize = 9.5,
            Margin = new Thickness(0, 3, 0, 0)
        };
        source.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        panel.Children.Add(source);

        var path = new TextBlock
        {
            Text = option.Path,
            FontSize = 9,
            Margin = new Thickness(0, 4, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 188,
            ToolTip = option.Path
        };
        path.SetResourceReference(TextBlock.ForegroundProperty, "SubtleBrush");
        panel.Children.Add(path);
        return panel;
    }

    private void InsertSample_Click(object sender, RoutedEventArgs e)
    {
        if (TypePowerShell.IsChecked == true)
        {
            ScriptBox.Text = L.IsEnglish
                ? """
# Example: wait 3 seconds, then start a program
Start-Sleep -Seconds 3
# Start-Process "C:\Path\To\YourApp.exe"
"""
                : """
# 示例：等待 3 秒后启动一个程序
Start-Sleep -Seconds 3
# Start-Process "C:\Path\To\YourApp.exe"
""";
        }
        else
        {
            ScriptBox.Text = L.IsEnglish
                ? """
@echo off
rem Example: wait 3 seconds, then start a program
timeout /t 3 /nobreak >nul
rem start "" "C:\Path\To\YourApp.exe"
"""
                : """
@echo off
rem 示例：等待 3 秒后启动一个程序
timeout /t 3 /nobreak >nul
rem start "" "C:\Path\To\YourApp.exe"
""";
        }

        ScriptBox.Focus();
        ScriptBox.CaretIndex = ScriptBox.Text.Length;
    }

    private void ImportScript_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = L.T("选择脚本文档"),
            Filter = TypePowerShell.IsChecked == true
                ? L.T("PowerShell 脚本 (*.ps1)|*.ps1|所有文件 (*.*)|*.*")
                : L.T("CMD 脚本 (*.cmd;*.bat)|*.cmd;*.bat|所有文件 (*.*)|*.*")
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            ScriptBox.Text = File.ReadAllText(dialog.FileName);
            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                NameBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
            }
        }
        catch (Exception ex)
        {
            ShowError(L.F("读取脚本失败：{0}", ex.Message));
        }
    }

    private void BrowseExe_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = L.T("选择可执行文件"),
            Filter = L.T("可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*"),
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;

        ExePathBox.Text = dialog.FileName;
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            NameBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
        }
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        if (_creating) return;
        HideError();

        var type = TypePowerShell.IsChecked == true
            ? AddEntryType.PowerShell
            : TypeCmd.IsChecked == true
                ? AddEntryType.CommandPrompt
                : AddEntryType.Executable;
        var mode = ModeAllUsers.IsChecked == true
            ? AddStartupMode.AllUsersLogon
            : ModeSystemTask.IsChecked == true
                ? AddStartupMode.SystemBootTask
                : ModeService.IsChecked == true
                    ? AddStartupMode.WindowsService
                    : AddStartupMode.CurrentUserLogon;

        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            ShowError(L.T("请填写项目名称。"));
            NameBox.Focus();
            return;
        }

        if (mode != AddStartupMode.CurrentUserLogon && !AdminService.IsAdministrator())
        {
            ShowError(L.T("该启动方式需要管理员权限。请先关闭本窗口，点击左下角“以管理员身份重启”，然后再创建。"));
            return;
        }

        if (type == AddEntryType.Executable)
        {
            if (string.IsNullOrWhiteSpace(ExePathBox.Text) || !File.Exists(ExePathBox.Text))
            {
                ShowError(L.T("请选择一个存在的可执行文件。"));
                return;
            }
        }
        else if (string.IsNullOrWhiteSpace(ScriptBox.Text))
        {
            ShowError(L.T("请输入脚本内容，或点击“从文件导入”。"));
            ScriptBox.Focus();
            return;
        }

        if (mode == AddStartupMode.WindowsService && type != AddEntryType.Executable)
        {
            ShowError(L.T("Windows 服务只能由可执行文件创建。请切换到“系统启动任务”来运行脚本。"));
            return;
        }

        var request = new AddEntryRequest
        {
            Name = NameBox.Text.Trim(),
            Type = type,
            Mode = mode,
            ScriptContent = ScriptBox.Text,
            ExecutablePath = ExePathBox.Text.Trim(),
            Arguments = ArgumentsBox.Text.Trim(),
            PowerShellHostPath = type == AddEntryType.PowerShell ? _selectedPowerShellHostPath : null,
            RunHidden = RunHiddenCheck.IsChecked == true,
            StartServiceImmediately = StartServiceCheck.IsChecked == true
        };

        _creating = true;
        CreateButton.IsEnabled = false;
        CreateButton.Content = L.T("正在创建…");

        try
        {
            var result = await Task.Run(() => new StartupWriter().Add(request));
            if (!result.Success)
            {
                ShowError(result.Message);
                return;
            }

            CreatedId = result.CreatedId;
            CreatedMessage = result.Message;
            Warning = result.Warning;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            _creating = false;
            CreateButton.IsEnabled = true;
            CreateButton.Content = L.T("创建启动项");
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorBanner.Visibility = Visibility.Visible;
    }

    private void HideError()
    {
        ErrorBanner.Visibility = Visibility.Collapsed;
        ErrorText.Text = string.Empty;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
