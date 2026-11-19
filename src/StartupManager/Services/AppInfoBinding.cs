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
using System.ComponentModel;
using StartupManager.Localization;

namespace StartupManager.Services;

/// <summary>
/// Language-aware binding source for values that come from app.props.
/// The singleton subscribes to LocalizationManager.LanguageChanged so window
/// title, sidebar brand, footer and add-dialog tagline update immediately.
/// </summary>
public sealed class AppInfoBinding : INotifyPropertyChanged
{
    public static AppInfoBinding Current { get; } = new();

    private AppInfoBinding()
    {
        LocalizationManager.LanguageChanged += OnLanguageChanged;
    }

    public string DisplayName => AppInfo.DisplayName;

    public string ProductName => AppInfo.ProductName;

    public string Footer => LocalizationManager.IsEnglish
        ? AppInfo.FooterTextEn
        : AppInfo.FooterTextZh;

    public string AddTagline => AppInfo.AddTagline;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(ProductName));
        OnPropertyChanged(nameof(Footer));
        OnPropertyChanged(nameof(AddTagline));
    }

    private void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
