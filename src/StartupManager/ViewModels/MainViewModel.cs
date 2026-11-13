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
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using StartupManager.Localization;
using StartupManager.Models;

namespace StartupManager.ViewModels;

public enum EntryCategory
{
    All,
    WindowsServices,
    LoginStartup,
    ScheduledTasks,
    Disabled
}

public enum EntryStatusFilter
{
    All,
    Running,
    Stopped,
    Disabled
}

public sealed class MainViewModel : INotifyPropertyChanged
{
    private string _searchText = string.Empty;
    private EntryCategory _category = EntryCategory.All;
    private EntryStatusFilter _statusFilter = EntryStatusFilter.All;
    private bool _isBusy;
    private string _busyText = L.T("正在扫描启动项目…");
    private DateTime? _lastScanAt;
    private string _warningText = string.Empty;
    private ManagedEntry? _selectedEntry;
    private bool _isAdmin;
    private int _filteredCount;

    public MainViewModel()
    {
        EntriesView = CollectionViewSource.GetDefaultView(AllEntries);
        EntriesView.Filter = FilterEntry;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ManagedEntry> AllEntries { get; } = new();
    public ICollectionView EntriesView { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (Set(ref _searchText, value))
            {
                RefreshView();
            }
        }
    }

    public EntryCategory Category
    {
        get => _category;
        set
        {
            if (Set(ref _category, value))
            {
                RefreshView();
            }
        }
    }

    public EntryStatusFilter StatusFilter
    {
        get => _statusFilter;
        set
        {
            if (Set(ref _statusFilter, value))
            {
                RefreshView();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => Set(ref _isBusy, value);
    }

    public string BusyText
    {
        get => _busyText;
        set => Set(ref _busyText, value);
    }

    public DateTime? LastScanAt
    {
        get => _lastScanAt;
        set
        {
            if (Set(ref _lastScanAt, value))
            {
                OnPropertyChanged(nameof(LastScanText));
            }
        }
    }

    public string LastScanText => LastScanAt is null
        ? L.T("尚未扫描")
        : L.F("更新于 {0}", LastScanAt.Value.ToString("HH:mm:ss"));

    public string WarningText
    {
        get => _warningText;
        set
        {
            if (Set(ref _warningText, value))
            {
                OnPropertyChanged(nameof(HasWarnings));
            }
        }
    }

    public bool HasWarnings => !string.IsNullOrWhiteSpace(WarningText);

    public ManagedEntry? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (Set(ref _selectedEntry, value))
            {
                OnPropertyChanged(nameof(HasSelection));
            }
        }
    }

    public bool HasSelection => SelectedEntry is not null;

    public bool IsAdmin
    {
        get => _isAdmin;
        set
        {
            if (Set(ref _isAdmin, value))
            {
                OnPropertyChanged(nameof(AdminText));
                OnPropertyChanged(nameof(AdminDetail));
            }
        }
    }

    public string AdminText => IsAdmin ? L.T("管理员模式") : L.T("标准权限");
    public string AdminDetail => IsAdmin ? L.T("全部功能已解锁") : L.T("部分系统级操作需要提权");

    public int TotalCount => AllEntries.Count;
    public int RunningCount => AllEntries.Count(e => e.IsRunning);
    public int ServiceCount => AllEntries.Count(e => e.Kind == EntryKind.WindowsService);
    public int LoginCount => AllEntries.Count(e => e.Kind is EntryKind.RegistryRun or EntryKind.StartupFolder);
    public int TaskCount => AllEntries.Count(e => e.Kind == EntryKind.ScheduledTask);
    public int DisabledCount => AllEntries.Count(e => !e.IsEnabled);

    public int FilteredCount
    {
        get => _filteredCount;
        private set
        {
            if (Set(ref _filteredCount, value))
            {
                OnPropertyChanged(nameof(FilteredCountText));
                OnPropertyChanged(nameof(HasFilteredEntries));
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    public bool HasFilteredEntries => FilteredCount > 0;
    public bool IsEmpty => FilteredCount == 0;

    public string FilteredCountText => L.IsEnglish
        ? L.F(FilteredCount == 1 ? "{0} item" : "{0} items", FilteredCount)
        : L.F("{0} 项", FilteredCount);
    public string HeaderSummary => L.IsEnglish
        ? L.F("{0} startup items · {1} running", TotalCount, RunningCount)
        : L.F("{0} 个启动项目 · {1} 个正在运行", TotalCount, RunningCount);

    public string EmptyStateTitle => AllEntries.Count == 0
        ? L.T("尚未发现启动项目")
        : L.T("没有符合条件的项目");

    public string EmptyStateMessage => AllEntries.Count == 0
        ? L.T("点击右上角“新增启动项”创建第一个项目。")
        : L.T("试试更换分类、清除搜索，或切换状态筛选。");

    public void SetEntries(IReadOnlyList<ManagedEntry> entries, IReadOnlyList<string> warnings)
    {
        var selectedId = SelectedEntry?.Id;

        AllEntries.Clear();
        foreach (var entry in entries)
        {
            AllEntries.Add(entry);
        }

        LastScanAt = DateTime.Now;
        WarningText = warnings.Count > 0 ? string.Join("  ·  ", warnings) : string.Empty;
        RaiseCounts();

        EntriesView.Refresh();
        UpdateFilteredCount();

        var visible = EntriesView.Cast<ManagedEntry>().ToList();
        SelectedEntry = selectedId is null
            ? null
            : visible.FirstOrDefault(e => string.Equals(e.Id, selectedId, StringComparison.OrdinalIgnoreCase));
        if (SelectedEntry is null && visible.Count > 0)
        {
            SelectedEntry = visible[0];
        }

        OnPropertyChanged(nameof(EmptyStateTitle));
        OnPropertyChanged(nameof(EmptyStateMessage));
    }

    public void ClearSelection() => SelectedEntry = null;

    public void NotifyLanguageChanged()
    {
        OnPropertyChanged(nameof(LastScanText));
        OnPropertyChanged(nameof(AdminText));
        OnPropertyChanged(nameof(AdminDetail));
        OnPropertyChanged(nameof(FilteredCountText));
        OnPropertyChanged(nameof(HeaderSummary));
        OnPropertyChanged(nameof(EmptyStateTitle));
        OnPropertyChanged(nameof(EmptyStateMessage));
    }

    public void SelectFirstVisible()
    {
        SelectedEntry = EntriesView.Cast<ManagedEntry>().FirstOrDefault();
    }

    private bool FilterEntry(object item)
    {
        if (item is not ManagedEntry entry) return false;

        var categoryMatches = Category switch
        {
            EntryCategory.All => true,
            EntryCategory.WindowsServices => entry.Kind == EntryKind.WindowsService,
            EntryCategory.LoginStartup => entry.Kind is EntryKind.RegistryRun or EntryKind.StartupFolder,
            EntryCategory.ScheduledTasks => entry.Kind == EntryKind.ScheduledTask,
            EntryCategory.Disabled => !entry.IsEnabled,
            _ => true
        };
        if (!categoryMatches) return false;

        var statusMatches = StatusFilter switch
        {
            EntryStatusFilter.All => true,
            EntryStatusFilter.Running => entry.IsRunning,
            EntryStatusFilter.Stopped => !entry.IsRunning && entry.IsEnabled,
            EntryStatusFilter.Disabled => !entry.IsEnabled,
            _ => true
        };
        if (!statusMatches) return false;

        if (string.IsNullOrWhiteSpace(SearchText)) return true;

        var haystack = string.Join('\n', entry.Name, entry.Description, entry.CommandLine,
            entry.SourcePath, entry.ScopeLabel, entry.TriggerLabel, entry.ServiceName, entry.TaskPath);
        foreach (var token in SearchText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!haystack.Contains(token, StringComparison.CurrentCultureIgnoreCase)) return false;
        }

        return true;
    }

    private void RefreshView()
    {
        EntriesView.Refresh();
        UpdateFilteredCount();

        if (SelectedEntry is not null && !EntriesView.Cast<ManagedEntry>().Contains(SelectedEntry))
        {
            SelectedEntry = null;
        }

        OnPropertyChanged(nameof(EmptyStateTitle));
        OnPropertyChanged(nameof(EmptyStateMessage));
    }

    private void UpdateFilteredCount()
    {
        FilteredCount = EntriesView.Cast<object>().Count();
    }

    private void RaiseCounts()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(RunningCount));
        OnPropertyChanged(nameof(ServiceCount));
        OnPropertyChanged(nameof(LoginCount));
        OnPropertyChanged(nameof(TaskCount));
        OnPropertyChanged(nameof(DisabledCount));
        OnPropertyChanged(nameof(HeaderSummary));
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
