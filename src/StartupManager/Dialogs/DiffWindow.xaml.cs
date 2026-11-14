using System.Windows;
using System.Windows.Input;
using StartupManager.Localization;
using StartupManager.Models;
using StartupManager.Services;

namespace StartupManager.Dialogs;

public partial class DiffWindow : Window
{
    private readonly SnapshotFile _snapshot;
    private readonly SnapshotService _snapshots = new();

    public DiffWindow(SnapshotFile snapshot, SnapshotDiff diff)
    {
        InitializeComponent();
        _snapshot = snapshot;
        Diff = diff;
        DataContext = this;
    }

    public SnapshotDiff Diff { get; }

    public bool BaselineApplied { get; private set; }

    public string SummaryText => Diff.BaselineEstablished
        ? L.F("基线已建立 · {0} 项", Diff.UnchangedCount)
        : Diff.HasChanges
            ? L.F("新增 {0} · 移除 {1} · 修改 {2}", Diff.Added.Count, Diff.Removed.Count, Diff.Modified.Count)
            : L.T("没有变更");

    public string UnchangedText => Diff.BaselineEstablished
        ? L.F("当前快照共 {0} 项。", Diff.UnchangedCount)
        : L.F("未变化 {0} 项。", Diff.UnchangedCount);

    public Visibility BaselineVisibility
        => Diff.BaselineEstablished ? Visibility.Visible : Visibility.Collapsed;

    public Visibility NoChangesVisibility
        => !Diff.BaselineEstablished && !Diff.HasChanges ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AddedVisibility
        => Diff.Added.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility RemovedVisibility
        => Diff.Removed.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ModifiedVisibility
        => Diff.Modified.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    private void ApplyBaseline_Click(object sender, RoutedEventArgs e)
    {
        _snapshots.EstablishBaseline(_snapshot);
        BaselineApplied = true;
        DialogResult = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
