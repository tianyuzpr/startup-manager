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
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using StartupManager.Dialogs;
using StartupManager.Localization;
using StartupManager.Models;
using StartupManager.Services;
using StartupManager.Themes;
using StartupManager.ViewModels;

namespace StartupManager;

public partial class MainWindow : Window
{
    private readonly StartupScanner _scanner = new();
    private readonly StartupActionService _actions = new();
    private readonly DispatcherTimer _toastTimer;
    private bool _isRefreshing;
    private string? _pendingSelectId;

    public MainWindow()
    {
        InitializeComponent();
        ViewModel = new MainViewModel { IsAdmin = AdminService.IsAdministrator() };
        DataContext = ViewModel;

        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(Width, Math.Max(MinWidth, workArea.Width - 32));
        Height = Math.Min(Height, Math.Max(MinHeight, workArea.Height - 32));

        _toastTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(4.5)
        };
        _toastTimer.Tick += ToastTimer_Tick;

        UpdateThemeButton();
    }

    public MainViewModel ViewModel { get; }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshAsync(selectFirst: true);
    }

    private async Task RefreshAsync(bool selectFirst = false)
    {
        if (_isRefreshing) return;

        _isRefreshing = true;
        ViewModel.IsBusy = true;
        ViewModel.BusyText = L.T("正在准备扫描…");

        try
        {
            var progress = new Progress<string>(message => ViewModel.BusyText = message);
            var result = await Task.Run(() => _scanner.Scan(progress));

            ViewModel.SetEntries(result.Entries, result.Warnings);
            ViewModel.IsAdmin = AdminService.IsAdministrator();

            if (!string.IsNullOrWhiteSpace(_pendingSelectId))
            {
                var created = ViewModel.AllEntries.FirstOrDefault(
                    e => string.Equals(e.Id, _pendingSelectId, StringComparison.OrdinalIgnoreCase));
                if (created is not null)
                {
                    ViewModel.SelectedEntry = created;
                    EntriesList.ScrollIntoView(created);
                }

                _pendingSelectId = null;
            }
            else if (selectFirst)
            {
                ViewModel.SelectFirstVisible();
            }
        }
        catch (Exception ex)
        {
            ShowToast(L.F("扫描启动项目失败：{0}", ex.Message), error: true);
        }
        finally
        {
            _isRefreshing = false;
            ViewModel.IsBusy = false;
        }
    }

    private async Task ExecuteEntryActionAsync(
        ManagedEntry entry,
        string busyText,
        Func<ManagedEntry, string> action)
    {
        if (ViewModel.IsBusy) return;

        ViewModel.IsBusy = true;
        ViewModel.BusyText = busyText;

        try
        {
            var message = await Task.Run(() => action(entry));
            ShowToast(message, error: false);
        }
        catch (Exception ex)
        {
            ShowToast(ex.Message, error: true);
        }
        finally
        {
            ViewModel.IsBusy = false;
        }

        await RefreshAsync();
    }

    private bool EnsureAdminFor(ManagedEntry entry)
    {
        if (ViewModel.IsAdmin || !entry.RequiresAdmin) return true;

        var dialog = new ConfirmDialog(
            L.T("需要管理员权限"),
            L.F("“{0}”属于系统级项目。要执行此操作，需要以管理员身份重新启动启航。", entry.Name),
            confirmText: L.T("以管理员身份重启"),
            danger: false,
            detail: L.T("重新启动后，请再次执行刚才的操作。当前窗口会被关闭。"));

        if (dialog.ShowDialog() == true)
        {
            ElevateAndClose();
        }

        return false;
    }

    private async void RestartEntry_Click(object sender, RoutedEventArgs e)
    {
        var entry = GetEntry(sender);
        if (entry is null || !EnsureAdminFor(entry)) return;

        if (entry.Kind is EntryKind.WindowsService or EntryKind.ScheduledTask)
        {
            var dialog = new ConfirmDialog(
                L.F("重启“{0}”？", entry.Name),
                entry.Kind == EntryKind.WindowsService
                    ? L.T("将先停止该服务，然后立即重新启动。正在使用该服务的程序可能会短暂中断。")
                    : L.T("将先结束正在运行的任务实例，然后重新触发该计划任务。"),
                confirmText: L.T("重启"),
                danger: false);
            if (dialog.ShowDialog() != true) return;
        }

        await ExecuteEntryActionAsync(entry, L.T("正在重启…"), _actions.Restart);
    }

    private async void PrimaryEntry_Click(object sender, RoutedEventArgs e)
    {
        var entry = GetEntry(sender);
        if (entry is null || !EnsureAdminFor(entry)) return;

        if (entry.IsRunning)
        {
            var dialog = new ConfirmDialog(
                L.F("停止“{0}”？", entry.Name),
                entry.Kind switch
                {
                    EntryKind.WindowsService => L.T("将向 Windows 服务控制管理器发送停止请求。正在使用该服务的程序可能会中断。"),
                    EntryKind.ScheduledTask => L.T("将结束该计划任务当前正在运行的实例。"),
                    _ => L.T("将结束该启动项关联的正在运行进程。未保存的数据可能会丢失。")
                },
                confirmText: L.T("停止"),
                danger: true);
            if (dialog.ShowDialog() != true) return;

            await ExecuteEntryActionAsync(entry, L.T("正在停止…"), _actions.Terminate);
        }
        else
        {
            await ExecuteEntryActionAsync(entry, L.T("正在启动…"), _actions.Start);
        }
    }

    private async void ToggleEnabled_Click(object sender, RoutedEventArgs e)
    {
        var entry = GetEntry(sender);
        if (entry is null) return;

        if (!entry.CanToggleEnabled)
        {
            ShowToast(entry.ToggleEnabledToolTip, error: true);
            return;
        }

        if (!EnsureAdminFor(entry)) return;

        var disabling = entry.IsEnabled;
        var targetEnabled = !disabling;
        var detail = entry.Kind switch
        {
            EntryKind.WindowsService => L.T("服务启动类型会被修改；当前运行中的服务进程不会被立即结束。"),
            EntryKind.ScheduledTask => L.T("计划任务的触发器会被关闭或恢复，当前运行中的实例状态不变。"),
            _ => L.T("只改变下次登录时是否自动运行，不会结束当前正在运行的进程。")
        };

        var dialog = new ConfirmDialog(
            disabling ? L.F("禁用“{0}”？", entry.Name) : L.F("启用“{0}”？", entry.Name),
            disabling
                ? L.T("禁用后 Windows 将不再自动启动该项目。当前正在运行的进程不会被结束。")
                : L.T("启用后该项目将在下次登录或开机时恢复自动启动。"),
            confirmText: disabling ? L.T("禁用启动") : L.T("启用启动"),
            danger: disabling,
            detail: detail);

        if (dialog.ShowDialog() != true) return;

        await ExecuteEntryActionAsync(
            entry,
            disabling ? L.T("正在禁用启动…") : L.T("正在启用启动…"),
            target => _actions.SetEnabled(target, targetEnabled));
    }

    private async void DeleteEntry_Click(object sender, RoutedEventArgs e)
    {
        var entry = GetEntry(sender);
        if (entry is null) return;

        if (entry.IsProtected && SystemServiceProtection.RefuseDeletingSystemServices)
        {
            ShowToast(entry.ProtectedReason ?? L.T("该项目受到保护，不能删除。"), error: true);
            return;
        }

        if (!EnsureAdminFor(entry)) return;

        var message = entry.Kind switch
        {
            EntryKind.WindowsService => L.T("将从服务控制管理器中删除该 Windows 服务。该操作不可撤销，服务将从服务列表中移除。"),
            EntryKind.RegistryRun => L.T("将删除对应的注册表启动值，下次登录不会再自动运行。"),
            EntryKind.StartupFolder => L.T("将删除启动文件夹中的该项目文件。"),
            EntryKind.ScheduledTask => L.T("将从任务计划程序中删除该项目。"),
            _ => L.T("将删除该项目。")
        };

        var requiredPhrase = entry.RequiresTypedConfirmation
            ? entry.ConfirmationPhrase ?? entry.Name
            : null;

        var dialog = new ConfirmDialog(
            L.F("删除“{0}”？", entry.Name),
            message,
            confirmText: L.T("删除项目"),
            danger: true,
            requiredPhrase: requiredPhrase,
            detail: entry.CommandDisplay);

        if (dialog.ShowDialog() != true) return;

        await ExecuteEntryActionAsync(entry, L.T("正在删除…"), _actions.Delete);
        ViewModel.SelectedEntry = null;
    }

    private void OpenLocation_Click(object sender, RoutedEventArgs e)
    {
        var entry = GetEntry(sender) ?? ViewModel.SelectedEntry;
        if (entry is null) return;

        var path = !string.IsNullOrWhiteSpace(entry.FilePath) && File.Exists(entry.FilePath)
            ? entry.FilePath
            : !string.IsNullOrWhiteSpace(entry.ExecutablePath) && File.Exists(entry.ExecutablePath)
                ? entry.ExecutablePath
                : null;

        if (path is null)
        {
            ShowToast(L.T("找不到可打开的文件位置，可能该项目已经不存在。"), error: true);
            return;
        }

        try
        {
            // Explorer lives in %WINDIR%, not %WINDIR%\System32. Using
            // Environment.SystemDirectory here produces a non-existent
            // C:\Windows\System32\explorer.exe.
            var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var explorerPath = Path.Combine(windowsDirectory, "explorer.exe");

            // Do not inherit the process current directory. If the app was
            // started from a publish folder that has since been deleted, the
            // inherited working directory no longer exists and Process.Start
            // fails before Explorer is launched.
            var workingDirectory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
            {
                workingDirectory = windowsDirectory;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = explorerPath,
                Arguments = $"/select,\"{path}\"",
                WorkingDirectory = workingDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowToast(L.F("打开文件位置失败：{0}", ex.Message), error: true);
        }
    }

    private async void AddEntry_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddEntryWindow { Owner = this };
        if (dialog.ShowDialog() != true) return;

        _pendingSelectId = dialog.CreatedId;

        // Make sure the new item is visible regardless of the current filters.
        NavAll.IsChecked = true;
        ViewModel.Category = EntryCategory.All;
        StatusAll.IsChecked = true;
        ViewModel.StatusFilter = EntryStatusFilter.All;

        ShowToast(dialog.CreatedMessage, error: false);
        if (!string.IsNullOrWhiteSpace(dialog.Warning))
        {
            ShowToast($"{dialog.CreatedMessage} {dialog.Warning}", error: true);
        }

        await RefreshAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing) return;
        await RefreshAsync();
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        AuditLog.Write("EXPORT", "button clicked");
        var dialog = new SaveFileDialog
        {
            Title = L.T("导出启动项为 CSV"),
            Filter = L.T("CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*"),
            FilterIndex = 1,
            DefaultExt = "csv",
            AddExtension = true,
            OverwritePrompt = true,
            CheckPathExists = true,
            FileName = $"StartupItems_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };

        var result = dialog.ShowDialog(this);
        AuditLog.Write("EXPORT", $"dialog result={result}");
        if (result != true) return;

        try
        {
            var entries = ViewModel.EntriesView.Cast<ManagedEntry>().ToList();
            var count = CsvExporter.Export(dialog.FileName, entries);
            ShowToast(L.F("已导出 {0} 项到 {1}", count, Path.GetFileName(dialog.FileName)), error: false);
            AuditLog.Write("EXPORT CSV", $"{dialog.FileName} | {count} rows");
        }
        catch (Exception ex)
        {
            AuditLog.Write("EXPORT FAILED", ex.ToString());
            ShowToast(L.F("导出失败：{0}", ex.Message), error: true);
        }
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow { Owner = this };
        if (dialog.ShowDialog() != true) return;

        UpdateThemeButton();
        ViewModel.NotifyLanguageChanged();
        await RefreshAsync();
        ShowToast(L.T("设置已保存。"), error: false);
    }

    private void ToggleTheme_Click(object sender, RoutedEventArgs e)
    {
        ThemeManager.Toggle();
        UpdateThemeButton();
        ShowToast(
            ThemeManager.IsDark ? L.T("已切换到深色模式") : L.T("已切换到浅色模式"),
            error: false);
    }

    private void UpdateThemeButton()
    {
        if (ThemeIconGlyph is null) return;

        ThemeIconGlyph.Text = ThemeManager.IsDark ? "\uE706" : "\uE708";
        var description = ThemeManager.IsDark ? L.T("切换到浅色模式") : L.T("切换到深色模式");
        ThemeToggleButton.ToolTip = description;
        AutomationProperties.SetName(ThemeToggleButton, description);
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag }) return;
        ViewModel.Category = tag switch
        {
            "WindowsServices" => EntryCategory.WindowsServices,
            "LoginStartup" => EntryCategory.LoginStartup,
            "ScheduledTasks" => EntryCategory.ScheduledTasks,
            "Disabled" => EntryCategory.Disabled,
            _ => EntryCategory.All
        };
        ViewModel.SelectFirstVisible();
    }

    private void Status_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag }) return;
        ViewModel.StatusFilter = tag switch
        {
            "Running" => EntryStatusFilter.Running,
            "Stopped" => EntryStatusFilter.Stopped,
            "Disabled" => EntryStatusFilter.Disabled,
            _ => EntryStatusFilter.All
        };
        ViewModel.SelectFirstVisible();
    }

    private void Elevate_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ConfirmDialog(
            L.T("以管理员身份重启？"),
            L.T("启航会关闭当前窗口并请求 Windows 提权。提权后即可管理 Windows 服务、系统启动任务和所有用户启动项。"),
            confirmText: L.T("重启并提权"),
            danger: false);
        if (dialog.ShowDialog() == true)
        {
            ElevateAndClose();
        }
    }

    private void ElevateAndClose()
    {
        try
        {
            if (AdminService.RelaunchElevated())
            {
                Application.Current.Shutdown();
            }
            else
            {
                ShowToast(L.T("已取消管理员提权。"), error: true);
            }
        }
        catch (Exception ex)
        {
            ShowToast(L.F("提权失败：{0}", ex.Message), error: true);
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (IsInteractiveSource(e.OriginalSource)) return;

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (e.ButtonState != MouseButtonState.Pressed) return;

        try
        {
            DragMove();
        }
        catch
        {
            // Ignore drag errors when the window state is changing.
        }
    }

    private bool IsInteractiveSource(object? source)
    {
        var current = source as DependencyObject;
        while (current is not null && !ReferenceEquals(current, HeaderBar))
        {
            if (current is Button or TextBox or ScrollBar or Thumb) return true;
            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void ToggleMaximize()
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowBorder.BorderThickness = new Thickness(0);
            MaximizeGlyph.Text = "\uE923";
        }
        else
        {
            WindowBorder.BorderThickness = new Thickness(1);
            MaximizeGlyph.Text = "\uE922";
        }
    }

    private ManagedEntry? GetEntry(object sender)
        => (sender as FrameworkElement)?.DataContext as ManagedEntry ?? ViewModel.SelectedEntry;

    private void ShowToast(string message, bool error)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        ToastText.Text = message;
        ToastIcon.Text = error ? "\uE7BA" : "\uE73E";
        ToastIcon.SetResourceReference(
            TextBlock.ForegroundProperty,
            error ? "ToastErrorIconBrush" : "ToastSuccessIconBrush");
        ToastBorder.SetResourceReference(
            Border.BackgroundProperty,
            error ? "ToastErrorBackgroundBrush" : "ToastBackgroundBrush");

        ToastBorder.Visibility = Visibility.Visible;
        ToastBorder.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));

        _toastTimer.Stop();
        _toastTimer.Interval = TimeSpan.FromSeconds(error ? 8 : 4.5);
        _toastTimer.Start();
    }

    private void ToastTimer_Tick(object? sender, EventArgs e)
    {
        _toastTimer.Stop();
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) =>
        {
            ToastBorder.Visibility = Visibility.Collapsed;
            ToastBorder.Opacity = 0;
        };
        ToastBorder.BeginAnimation(OpacityProperty, fade);
    }
}
