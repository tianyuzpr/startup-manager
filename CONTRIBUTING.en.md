<!--
  Copyright 2026 Xiayue

  Licensed under the Apache License, Version 2.0 (the "License");
  you may not use this file except in compliance with the License.
  You may obtain a copy of the License at

      http://www.apache.org/licenses/LICENSE-2.0

  Unless required by applicable law or agreed to in writing, software
  distributed under the License is distributed on an "AS IS" BASIS,
  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
  See the License for the specific language governing permissions and
  limitations under the License.
-->

English | [简体中文](CONTRIBUTING.md)

# Contributing

Thanks for taking the time to contribute to Qihang · StartupManager. This guide explains how to raise issues, send changes, and which conventions this codebase follows.

## Contents

- [Read these first](#read-these-first)
- [Prerequisites](#prerequisites)
- [Build and run](#build-and-run)
- [Project layout](#project-layout)
- [Code conventions](#code-conventions)
- [Text and localisation](#text-and-localisation)
- [Commit messages](#commit-messages)
- [Pull requests](#pull-requests)
- [Sign-off](#sign-off)
- [Pre-submit checklist](#pre-submit-checklist)
- [What maintainers look at](#what-maintainers-look-at)

---

## Read these first

| Document | When |
| --- | --- |
| [README.en.md](README.en.md) | Understanding features, the privilege model and known limitations |
| [SECURITY.en.md](SECURITY.en.md) | **Required when you find a vulnerability**: do not open a public issue, use private advisories |
| [CODE_OF_CONDUCT.en.md](CODE_OF_CONDUCT.en.md) | Before discussions, reviews and issue threads |
| [build/app.props](build/app.props) | When changing the version, app name, publisher or other metadata |

---

## Prerequisites

| Dependency | Version | Notes |
| --- | --- | --- |
| Windows | 10 1607 (build 14393) or later, x64 | Target platform |
| .NET SDK | 10.0 | Targets `net10.0-windows` |
| PowerShell | 7.0 or later | Build and packaging scripts use `pwsh` |
| Inno Setup | 6.x (optional) | Only needed to build the `normal` / `runtimeless` installers locally |

This is a WPF application and only builds on Windows; cross-platform development is not supported.

---

## Build and run

```powershell
# Run
dotnet run --project src/StartupManager/StartupManager.csproj

# Build
dotnet build StartupManager.slnx -c Release

# Publish a self-contained single file
dotnet publish src/StartupManager/StartupManager.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -o release
```

- Self-contained single-file publishing needs `PublishReadyToRun` enabled as well to reliably produce exactly one exe.
- If the published app shows a blank window, delete `src/StartupManager/bin`, `src/StartupManager/obj` and the old output directory, then publish again.
- `app.manifest` declares `requireAdministrator`; start it elevated when testing service and system-task behaviour.

The four-variant packaging script lives in `temp/build-startupmanager.ps1`. That directory is ignored by `.gitignore`, so **never commit anything under `temp/`**.

---

## Project layout

```text
build/app.props            # every configurable value: version, names, publisher (single source of truth)
src/StartupManager/
├─ Models/                 # startup item models, action requests/results, snapshot models
├─ Services/               # scanning, actions, registry, services, tasks, process probing, snapshots, CSV
├─ Localization/           # Chinese/English strings, LocExtension, runtime switching
├─ Themes/                 # LightTheme / DarkTheme resource dictionaries and ThemeManager
├─ Interop/                # SCM / process API P/Invoke
├─ ViewModels/             # list filtering and statistics
├─ Dialogs/                # add dialog, settings, confirmations and impact preview
└─ Converters/             # XAML value converters
```

**Changing metadata (version, names, publisher, repository URL, minimum Windows) means editing `build/app.props` only.**
Do not hard-code these values in XAML or C#, and do not add brand-name entries to `LocalizationManager`.

---

## Code conventions

- C# uses file-scoped namespaces, nullable reference types and implicit usings; follow the style of the surrounding files.
- Win32/COM interaction belongs in `Interop/` or `Services/`; the UI layer never calls P/Invoke directly.
- Risky actions (deleting services, killing processes, writing to the registry) must go through `StartupActionService` so audit logging is never bypassed.
- A new action type must also update the preview in `ImpactPreviewService` and the confirmation flow in `ConfirmDialog`.
- Every user-facing string uses `L.T` / `L.F`, and the Chinese key plus English translation are added to `LocalizationManager` together.
- Avoid adding new third-party NuGet packages unless the pull request explains why and covers the size impact.
- `dotnet build` must finish with zero warnings and zero errors.

---

## Text and localisation

`LocalizationManager` uses the Chinese string as the key and English as the translation:

```csharp
["已结束 {0} 个关联进程。"] = "Ended {0} associated processes.",
```

- Keys and translations must come in pairs, and the placeholders `{0}`, `{1}` must match in order and count.
- Never hard-code the product name in a key; brand text comes from `app.props`.
- When you add a Chinese string, review that the English reads naturally rather than literally.

---

## Commit messages

History contains both Conventional Commits and plain sentences; **use Conventional Commits for new commits**:

```text
<type>(<scope>): <summary>

<body>

<footer>
```

Common types:

| Type | Use |
| --- | --- |
| `feat` | New functionality |
| `fix` | Bug fix |
| `ci` | Workflows and pipelines |
| `docs` | Documentation |
| `refactor` | Behaviour-preserving restructuring |
| `chore` | Build, dependencies, housekeeping |

Example:

```text
fix(scanner): trust Task Scheduler for scheduled-task run state

A same-path process no longer flips a ready task to Running, which
previously misled the impact preview.
```

Wrap body lines at 72 characters, keep the subject under 72 characters and without a trailing period.

---

## Pull requests

1. Branch off `main`; suggested names: `feat/snapshot-diff`, `fix/kill-partial-failure`.
2. Keep pull requests focused: one problem per pull request, no unrelated reformatting.
3. Describe **what the problem is, how you fixed it, and how you verified it**.
4. UI changes need screenshots in both light and dark themes.
5. Changes to risky actions must explain the failure paths and how to roll back.
6. Open as a Draft while it is still in progress.

---

## Sign-off

Commits must use `git commit -s` (a `Signed-off-by` line), certifying agreement with the [Developer Certificate of Origin](https://developercertificate.org/):

```text
Signed-off-by: Your Name <you@example.com>
```

GPG signatures are optional; `Signed-off-by` on every commit is required.

---

## Pre-submit checklist

- [ ] `dotnet build StartupManager.slnx -c Release` reports zero warnings and zero errors
- [ ] Every new Chinese string has an English translation with matching placeholders
- [ ] No hard-coded version, product name or path
- [ ] Risky actions go through `ImpactPreviewService` and write audit logs
- [ ] `bin/`, `obj/`, `release/`, `temp/` and scratch screenshots are not committed
- [ ] Commit message follows Conventional Commits and carries `Signed-off-by`
- [ ] Workflow changes parse as valid YAML and every `run` script is syntactically valid

---

## What maintainers look at

Acceptance mainly depends on:

1. **Correctness**: can it delete the wrong startup item or kill the wrong process, and does failure give a clear reason?
2. **Security**: does it widen the elevation surface or execute unintended commands in an elevated context?
3. **Consistency**: does it follow the existing architecture (`Services/` layering, `L.T` localisation, `app.props` as the single source of truth)?
4. **Maintainability**: is the change readable, testable and reversible?

Review may ask for extra logging, a split pull request or a reverted implementation. If you disagree with a conclusion, argue the technical merits in the pull request.

---

## Contact

- Feature requests and defects: GitHub Issues
- Security vulnerabilities: GitHub Security Advisories (see [SECURITY.en.md](SECURITY.en.md))
- Code of Conduct matters: private advisory via Security Advisories, titled "Code of Conduct"

*Version: 2026-11 · Maintainer: Xiayue · Apache License 2.0*
