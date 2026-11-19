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

[English](CONTRIBUTING.en.md) | 简体中文

# 贡献指南 · Contributing

感谢你愿意为启航（Qihang）· StartupManager 做出贡献。本文说明如何提出问题、提交改动，以及本项目在代码层面的具体约定。

## 目录

- [先读这几份文档](#先读这几份文档)
- [环境准备](#环境准备)
- [构建与运行](#构建与运行)
- [目录结构与改动落点](#目录结构与改动落点)
- [代码约定](#代码约定)
- [文本与本地化](#文本与本地化)
- [提交信息规范](#提交信息规范)
- [提交 Pull Request](#提交-pull-request)
- [签名要求](#签名要求)
- [提交前自检清单](#提交前自检清单)
- [维护者会看什么](#维护者会看什么)

---

## 先读这几份文档

| 文档 | 什么时候看 |
| --- | --- |
| [README.md](README.md) | 了解功能、权限模型与已知限制 |
| [SECURITY.md](SECURITY.md) | **发现安全漏洞时必读**：不要开公开 Issue，走私有通告 |
| [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) | 参与讨论、评审与 Issue 互动前 |
| [build/app.props](build/app.props) | 需要改版本号、应用名、发布者等元信息时 |

---

## 环境准备

| 依赖 | 版本 | 说明 |
| --- | --- | --- |
| Windows | 10 1607（build 14393）或更高，x64 | 目标平台 |
| .NET SDK | 10.0 | 使用 `net10.0-windows` |
| PowerShell | 7.0 及以上 | 构建脚本与打包脚本使用 `pwsh` |
| Inno Setup | 6.x（可选） | 仅在本地生成 `normal` / `runtimeless` 安装包时需要 |

WPF 应用只能在 Windows 上构建，暂不支持跨平台开发。

---

## 构建与运行

```powershell
# 运行
dotnet run --project src/StartupManager/StartupManager.csproj

# 构建
dotnet build StartupManager.slnx -c Release

# 发布自包含单文件
dotnet publish src/StartupManager/StartupManager.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -o release
```

- 自包含单文件必须同时开启 `PublishReadyToRun` 才能稳定只产出一个 exe。
- 若发布后出现空白窗口，先删除 `src/StartupManager/bin`、`src/StartupManager/obj` 与旧发布目录，再重新发布。
- 程序在 `app.manifest` 中声明了 `requireAdministrator`，调试时请以管理员身份启动以便测试服务与系统任务相关功能。

本地四形态打包脚本位于 `temp/build-startupmanager.ps1`，该目录被 `.gitignore` 忽略，**请不要提交 `temp/` 下的任何内容**。

---

## 目录结构与改动落点

```text
build/app.props            # 版本、名称、发布者等全部可配置信息（唯一修改点）
src/StartupManager/
├─ Models/                 # 启动项模型、操作请求 / 结果、快照模型
├─ Services/               # 扫描、操作、注册表、服务、计划任务、进程探测、快照、CSV
├─ Localization/           # 中英文文本、LocExtension、运行时切换
├─ Themes/                 # LightTheme / DarkTheme 资源字典与 ThemeManager
├─ Interop/                # SCM / 进程 API P/Invoke
├─ ViewModels/             # 主界面筛选与统计
├─ Dialogs/                # 新增启动项、设置、确认与影响预览
└─ Converters/             # XAML 值转换器
```

**改元信息（版本、名称、发布者、仓库地址、最低系统要求）→ 只改 `build/app.props`。**
不要在 XAML 或 C# 里硬编码这些值，也不要在 `LocalizationManager` 里新增品牌名条目。

---

## 代码约定

- C# 使用文件作用域命名空间、可空引用类型与隐式 `using`；代码风格以现有文件为准。
- 与 Win32 / COM 交互的逻辑放在 `Interop/` 或 `Services/`，UI 层不直接调用 P/Invoke。
- 危险操作（删除服务、结束进程、写入注册表）必须经由 `StartupActionService`，不要绕开审计日志。
- 新增的操作类型要同时更新 `ImpactPreviewService` 的预览结果与 `ConfirmDialog` 的确认流程。
- 新增面向用户的提示必须走 `L.T` / `L.F`，并把中文键与英文译文同时加入 `LocalizationManager`。
- 不要引入新的第三方 NuGet 包，除非在 PR 描述中说明必要性与体积影响。
- 修改完成后请确保 `dotnet build` 为 0 警告 0 错误。

---

## 文本与本地化

`LocalizationManager` 以中文字符串作为键，英文为对应译文：

```csharp
["已结束 {0} 个关联进程。"] = "Ended {0} associated processes.",
```

- 键与译文必须成对出现，占位符 `{0}`、`{1}` 的顺序与数量必须一致。
- 不要在键里写死产品名，品牌文案请用 `app.props` 中的字段。
- 新增中文提示后，请同步检查英文译文是否自然，避免直译。

---

## 提交信息规范

本项目不强制单一格式，以下两种都接受：

### 自然语句式标题（与历史记录一致）

用一句祈使句说清做了什么，首字母大写，不加句号：

```text
Fix blank window after single-file publish

The self-extracting runtime was dropped because ReadyToRun was off in the
publish profile. Enable it so the output stays a single exe.
```

### Conventional Commits（可选）

当改动类型明显、或希望自动生成变更日志时使用：

```text
<type>(<scope>): <summary>

<body>

<footer>
```

常用 `type`：

| type | 用途 |
| --- | --- |
| `feat` | 新功能 |
| `fix` | 缺陷修复 |
| `ci` | 工作流与流水线 |
| `docs` | 文档 |
| `refactor` | 不改变行为的重构 |
| `chore` | 构建、依赖、杂项 |

```text
fix(scanner): trust Task Scheduler for scheduled-task run state

A same-path process no longer flips a ready task to Running, which
previously misled the impact preview.
```

### 通用要求

- 标题不超过 72 字符，末尾不加句号；正文每行控制在 72 字符以内。
- 正文说明**为什么改**，而不是重复标题里的“改了什么”。
- 一个提交只做一件事；把无关的格式化或重命名拆出去。
- 每个提交都要带 `Signed-off-by`（见 [签名要求](#签名要求)）。

---

## 提交 Pull Request

1. 从 `main` 拉出分支，命名建议：`feat/snapshot-diff`、`fix/kill-partial-failure`。
2. 保持 PR 聚焦：一个 PR 解决一个问题，不要混入无关格式化。
3. 在 PR 描述中写清：**问题是什么、怎么改的、怎么验证的**。
4. 涉及 UI 变更请附截图（浅色与深色各一张）。
5. 涉及危险操作变更，请额外说明失败路径与回滚方式。
6. 若 PR 尚未完成，请用 Draft 状态提交。

---

## 签名要求

提交必须使用 `git commit -s`（`Signed-off-by` 行），表示你同意 [Developer Certificate of Origin](https://developercertificate.org/)：

```text
Signed-off-by: Your Name <you@example.com>
```

本项目不要求 GPG 签名，但要求每个提交都带 `Signed-off-by`。

---

## 提交前自检清单

- [ ] `dotnet build StartupManager.slnx -c Release` 为 0 警告 0 错误
- [ ] 新增的中文提示都有对应英文译文，占位符一致
- [ ] 没有硬编码版本号、产品名或路径
- [ ] 危险操作经过 `ImpactPreviewService` 并记录审计日志
- [ ] 没有提交 `bin/`、`obj/`、`release/`、`temp/` 或临时截图
- [ ] 提交信息说明清楚改动与原因，并带 `Signed-off-by`
- [ ] 若涉及工作流改动，YAML 可解析且 `run` 脚本无语法错误

---

## 维护者会看什么

合并与否主要取决于：

1. **正确性**：是否会误删启动项、误杀进程，失败时是否给出明确原因。
2. **安全性**：是否扩大了提权面，或在管理员上下文中执行了未预期的命令。
3. **一致性**：是否沿用现有架构（`Services/` 分层、`L.T` 本地化、`app.props` 单一配置源）。
4. **可维护性**：改动是否可读、可测、可回滚。

评审意见可能要求补充日志、拆分 PR 或回退某段实现。若对结论有异议，请在 PR 中就事论事地说明理由。

---

## 联系方式

- 功能建议与缺陷：GitHub Issues
- 安全漏洞：GitHub Security Advisories（见 [SECURITY.md](SECURITY.md)）
- 行为准则问题：Security Advisories 私有通告，标题注明“行为准则”

*版本: 2026-11 · 维护人: Xiayue · Apache License 2.0*
