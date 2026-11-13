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
using System.Windows.Threading;
using StartupManager.Localization;
using StartupManager.Services;
using StartupManager.Themes;
namespace StartupManager;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        LocalizationManager.Initialize();
        SystemServiceProtection.Initialize();
        ThemeManager.Initialize();

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Services.AuditLog.Write("UNHANDLED", e.Exception.ToString());
        MessageBox.Show(
            L.F("程序遇到未处理的错误：\n\n{0}\n\n详细信息已写入本地日志。", e.Exception.Message),
            L.T("启航 · 发生错误"),
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            Services.AuditLog.Write("FATAL", exception.ToString());
        }
    }
}
