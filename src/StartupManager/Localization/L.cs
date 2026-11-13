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
namespace StartupManager.Localization;

/// <summary>Short alias used by code-behind and services.</summary>
public static class L
{
    public static bool IsEnglish => LocalizationManager.IsEnglish;
    public static string T(string key) => LocalizationManager.T(key);
    public static string F(string key, params object?[] args) => LocalizationManager.F(key, args);
}
