using System.ComponentModel;
using StartupManager.Localization;
using StartupManager.Models;

namespace StartupManager.Models;

public enum ImpactOperation
{
    Stop,
    Restart,
    Enable,
    Disable,
    Delete,
    Add
}

public sealed class ImpactPreview
{
    public ImpactOperation Operation { get; init; }
    public string OperationName { get; init; } = string.Empty;
    public List<ImpactItem> Items { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
    public bool RequiresTypedConfirmation { get; init; }
    public string? TypedConfirmationTarget { get; init; }
    public bool SnapshotWillBeCreated { get; init; }
}

public sealed class ImpactItem : INotifyPropertyChanged
{
    private bool _isSelected = true;

    public ManagedEntry? Entry { get; init; }
    public string StableId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string CategoryLabel { get; init; } = string.Empty;
    public string CurrentState { get; init; } = string.Empty;
    public string TargetState { get; init; } = string.Empty;
    public Matchability Matchability { get; init; }
    public bool IsProtected { get; init; }
    public string? ProtectedReason { get; init; }
    public bool CanExecute { get; init; } = true;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public string StateTransition => $"{CurrentState} → {TargetState}";

    public event PropertyChangedEventHandler? PropertyChanged;
}
