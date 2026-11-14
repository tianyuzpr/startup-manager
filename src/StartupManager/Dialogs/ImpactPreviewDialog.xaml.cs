using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using StartupManager.Localization;
using StartupManager.Models;

namespace StartupManager.Dialogs;

public partial class ImpactPreviewDialog : Window, INotifyPropertyChanged
{
    private Visibility _blockedVisibility = Visibility.Collapsed;

    public ImpactPreviewDialog(ImpactPreview preview)
    {
        InitializeComponent();
        Preview = preview;
        Items = preview.Items;
        Warnings = preview.Warnings;
        DataContext = this;

        foreach (var item in Items)
        {
            item.PropertyChanged += Item_PropertyChanged;
        }

        UpdateConfirmationState();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ImpactPreview Preview { get; }
    public IReadOnlyList<ImpactItem> Items { get; }
    public IReadOnlyList<string> Warnings { get; }
    public IReadOnlyList<ImpactItem> SelectedItems { get; private set; } = Array.Empty<ImpactItem>();
    public bool Confirmed { get; private set; }

    public string OperationName => Preview.OperationName;

    public string SummaryText => L.F("将影响 {0} 项。", Items.Count);

    public bool HasWarnings => Warnings.Count > 0;

    public Visibility TypedVisibility
        => Preview.RequiresTypedConfirmation ? Visibility.Visible : Visibility.Collapsed;

    public Visibility SnapshotVisibility
        => Preview.SnapshotWillBeCreated ? Visibility.Visible : Visibility.Collapsed;

    public string TypedPrompt => L.F(
        "为了防止误操作，请输入 {0} 以确认：",
        Preview.TypedConfirmationTarget ?? string.Empty);

    public Visibility BlockedVisibility
    {
        get => _blockedVisibility;
        private set
        {
            if (_blockedVisibility == value) return;
            _blockedVisibility = value;
            OnPropertyChanged();
        }
    }

    public string BlockedMessage => L.T("选中的项目包含受保护或不可执行项目，当前设置拒绝执行。");

    private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ImpactItem.IsSelected))
        {
            UpdateConfirmationState();
        }
    }

    private void TypedBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        UpdateConfirmationState();
    }

    private void UpdateConfirmationState()
    {
        if (ConfirmButton is null) return;

        var selected = Items.Where(item => item.IsSelected).ToList();
        var blocked = selected.Any(item => !item.CanExecute);
        BlockedVisibility = blocked ? Visibility.Visible : Visibility.Collapsed;

        var typedOk = !Preview.RequiresTypedConfirmation
                      || string.Equals(
                          TypedBox.Text.Trim(),
                          Preview.TypedConfirmationTarget,
                          StringComparison.OrdinalIgnoreCase);

        ConfirmButton.IsEnabled = selected.Count > 0 && !blocked && typedOk;
        if (ErrorText is not null)
        {
            ErrorText.Text = !typedOk && TypedBox.Text.Length > 0
                ? L.T("输入的内容与确认目标不一致。")
                : string.Empty;
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        var selected = Items.Where(item => item.IsSelected).ToList();
        if (selected.Count == 0)
        {
            ErrorText.Text = L.T("请至少选择一个项目。");
            return;
        }

        if (selected.Any(item => !item.CanExecute))
        {
            ErrorText.Text = BlockedMessage;
            return;
        }

        if (Preview.RequiresTypedConfirmation
            && !string.Equals(
                TypedBox.Text.Trim(),
                Preview.TypedConfirmationTarget,
                StringComparison.OrdinalIgnoreCase))
        {
            ErrorText.Text = L.T("输入的内容与确认目标不一致。");
            return;
        }

        SelectedItems = selected;
        Confirmed = true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
