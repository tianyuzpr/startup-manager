using System.Windows;
using System.Windows.Input;
using StartupManager.Localization;
using StartupManager.Services;
using StartupManager.Themes;

namespace StartupManager.Dialogs;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        LoadCurrentSettings();
    }

    private void LoadCurrentSettings()
    {
        if (LocalizationManager.CurrentLanguage == AppLanguage.ChineseSimplified)
        {
            LanguageChinese.IsChecked = true;
        }
        else
        {
            LanguageEnglish.IsChecked = true;
        }

        if (ThemeManager.Current == StartupManager.Themes.ThemeMode.Dark)
        {
            ThemeDark.IsChecked = true;
        }
        else if (ThemeManager.Current == StartupManager.Themes.ThemeMode.System)
        {
            ThemeSystem.IsChecked = true;
        }
        else
        {
            ThemeLight.IsChecked = true;
        }

        RefuseDeleteCheck.IsChecked = SystemServiceProtection.RefuseDeletingSystemServices;
        ProtectedServicesBox.Text = string.Join(
            Environment.NewLine,
            SystemServiceProtection.ProtectedServices);
    }

    private void DangerHeader_Click(object sender, RoutedEventArgs e)
    {
        var expanded = DangerContent.Visibility != Visibility.Visible;
        DangerContent.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        DangerChevron.Text = expanded ? "\uE70E" : "\uE70D";
    }

    private void RestoreDefaults_Click(object sender, RoutedEventArgs e)
    {
        ProtectedServicesBox.Text = string.Join(
            Environment.NewLine,
            SystemServiceProtection.DefaultServiceList);
        RefuseDeleteCheck.IsChecked = true;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var language = LanguageEnglish.IsChecked == true
                ? AppLanguage.EnglishUnitedKingdom
                : AppLanguage.ChineseSimplified;

            var theme = ThemeDark.IsChecked == true
                ? StartupManager.Themes.ThemeMode.Dark
                : ThemeSystem.IsChecked == true
                    ? StartupManager.Themes.ThemeMode.System
                    : StartupManager.Themes.ThemeMode.Light;

            var services = ProtectedServicesBox.Text
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            SystemServiceProtection.Save(services, RefuseDeleteCheck.IsChecked == true);
            LocalizationManager.SetLanguage(language);
            ThemeManager.Apply(theme);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ErrorText.Text = L.F("设置保存失败：{0}", ex.Message);
        }
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
}
