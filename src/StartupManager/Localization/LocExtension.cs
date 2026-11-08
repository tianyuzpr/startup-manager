using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace StartupManager.Localization;

/// <summary>
/// XAML helper: Text="{loc:Loc 新增启动项}" binds to the localisation indexer
/// and updates all windows immediately when the language changes.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key)
    {
        Key = key;
    }

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding
        {
            Source = LocalizationManager.Instance,
            Path = new PropertyPath($"[{Key}]"),
            Mode = BindingMode.OneWay
        };
        return binding.ProvideValue(serviceProvider);
    }
}
