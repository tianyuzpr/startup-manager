using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StartupManager.Localization;

namespace StartupManager.Dialogs;

public partial class ConfirmDialog : Window
{
    private readonly string? _requiredPhrase;

    public ConfirmDialog(
        string heading,
        string message,
        string? confirmText = null,
        bool danger = true,
        string? requiredPhrase = null,
        string? detail = null)
    {
        InitializeComponent();
        _requiredPhrase = string.IsNullOrWhiteSpace(requiredPhrase) ? null : requiredPhrase;

        HeadingText.Text = heading;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText ?? L.T("确认");

        if (danger)
        {
            IconBadge.SetResourceReference(Border.BackgroundProperty, "SoftDangerBrush");
            IconGlyph.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
            IconGlyph.Text = "\uE7BA";
            ConfirmButton.Style = (Style)FindResource("PrimaryButton");
            ConfirmButton.SetResourceReference(Control.BackgroundProperty, "DangerButtonBackgroundBrush");
            ConfirmButton.SetResourceReference(Control.ForegroundProperty, "DangerButtonForegroundBrush");
        }
        else
        {
            IconBadge.SetResourceReference(Border.BackgroundProperty, "SoftAccentBrush");
            IconGlyph.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            IconGlyph.Text = "\uE946";
        }

        if (!string.IsNullOrWhiteSpace(detail))
        {
            DetailBox.Visibility = Visibility.Visible;
            DetailText.Text = detail;
        }

        if (_requiredPhrase is not null)
        {
            PhrasePanel.Visibility = Visibility.Visible;
            PhraseLabel.Text = L.F("为了防止误操作，请输入 {0} 以确认删除：", _requiredPhrase);
            ConfirmButton.IsEnabled = false;
        }
    }

    public bool Confirmed { get; private set; }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (_requiredPhrase is not null
            && !string.Equals(PhraseBox.Text.Trim(), _requiredPhrase, StringComparison.OrdinalIgnoreCase))
        {
            PhraseError.Text = L.T("输入的内容与项目名称不一致。");
            PhraseError.Visibility = Visibility.Visible;
            return;
        }

        Confirmed = true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        DialogResult = false;
    }

    private void PhraseBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_requiredPhrase is null) return;

        var matches = string.Equals(
            PhraseBox.Text.Trim(), _requiredPhrase, StringComparison.OrdinalIgnoreCase);
        ConfirmButton.IsEnabled = matches;
        PhraseError.Visibility = matches || PhraseBox.Text.Length == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
        PhraseError.Text = L.T("输入的内容与项目名称不一致。");
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
