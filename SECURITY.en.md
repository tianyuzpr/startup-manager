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

English | [简体中文](SECURITY.md)

# Security Policy

Qihang · StartupManager is a fully local Windows desktop tool: it uploads no data, opens no ports and performs no background network access. The security model is therefore not about stopping remote attackers, but about **preventing this program from being abused as an elevation or supply-chain pivot**.

## Contents

- [Supported versions](#supported-versions)
- [Security boundaries](#security-boundaries)
- [Threat model](#threat-model)
- [Reporting a vulnerability](#reporting-a-vulnerability)
- [Response times and severity levels](#response-times-and-severity-levels)
- [Fixes and releases](#fixes-and-releases)
- [Known boundary cases](#known-boundary-cases)
- [Unexpected capability or execution](#unexpected-capability-or-execution)
- [Counterfeit distribution](#counterfeit-distribution)

---

## Supported versions

Security updates are provided only for builds downloaded from **GitHub Releases**.

| Line | Example | Security updates |
| --- | --- | --- |
| `v1.5.x` | `v1.5.0-BUILD2647` | ✅ Yes, all security fixes |
| `v1.4.x` | `v1.4.1-BUILD2646` | ✅ Critical / High only, until 30 days after `v1.5.0` ships |
| `< v1.4.0`, pre-releases | `v1.5.0-rc1-BUILD2647` | ❌ No; upgrade to a stable release |
| Dev / nightly builds | `26w46d6a`, `26w46d6a.NIGHTLY` | ❌ No; these are test artefacts and are never a security baseline |

Version numbering follows the [README](README.en.md#version-naming): stable builds use `vX.Y.Z-BUILDYYWW`, pre-releases use `vX.Y.0-rcZ-BUILDYYWW`, and dev/nightly builds carry no build number. Builds not listed above are unsupported by default.

All security advisories are published under **Security → Advisories** in the repository; security fixes mentioned in release notes are recorded there as well.

---

## Security boundaries

**It is not:**

- A web service: no backend, no API, no accounts.
- A sync tool: it modifies nothing outside the local startup configuration and its own configuration directory.

**It is:**

- A process that declares `requireAdministrator` (`app.manifest`), because it rewrites service start types, registry Run keys and scheduled tasks.
- A process that executes user-configured commands: “Add startup item” supports PowerShell, CMD and EXE.
- A binary built and distributed by GitHub Actions.

---

## Threat model

| Category | Risk | Notes |
| --- | --- | --- |
| **Elevation abuse / command injection** | Startup item names, paths or script content are concatenated into `Process.Start`, SCM or Task Scheduler calls, yielding arbitrary command execution | The app runs elevated; rated at least High |
| **Persistence abuse** | The tool's ability to create SYSTEM boot tasks is used to plant a persistence backdoor disguised as the app's own behaviour | Social engineering plus tooling; report it |
| **Tampered snapshots or configuration** | Deletion snapshots and startup configuration live in paths a low-privilege process can write, and can be crafted to mislead an administrator | Path and ACL issues |
| **Build supply chain** | Third-party Actions in `nightly.yml` are not pinned to a commit SHA, or release assets cannot be reconciled with CI records | Including missing checksums and mismatches with `create-release` |
| **Counterfeit distribution** | Bundled or repackaged builds from “fast download / safe download / P2P downloader” sites, or tampered builds re-signed | See [Counterfeit distribution](#counterfeit-distribution) |
| **Runtime injection** | An attacker injects a runtime or code into the app so it executes unintended commands with administrator rights | The app runs elevated; rated at least High |

**Out of scope (please file a regular issue):**

- Local privilege-escalation techniques unrelated to this app that require administrator rights up front (those are Windows patch matters).
- Suggestions about the snapshot or configuration file format (use a Feature request).
- Behaviour disputes, UI and accessibility issues, translation errors.
- The issues above in dev or nightly builds (use a regular issue).

---

## Reporting a vulnerability

### Preferred: GitHub private advisory

Open **Security → Advisories → Report a vulnerability** in the repository:

`https://github.com/tianyuzpr/startup-manager/security/advisories/new`

This is the **Private Vulnerability Reporting** channel: only maintainers can see it and nothing is published to issues. If you want credit or attribution, say so in the advisory body.

> ⚠️ **Do not describe an unfixed vulnerability in a public issue, pull request or social post.**
> Problems already public elsewhere are still welcome, but the fix schedule will be reassessed against their current impact.

### What to include

Please include as much of the following as you can (missing items do not block triage):

1. **Affected version**: the exact file name or version from the release page (e.g. `v1.5.0-BUILD2647`, `StartupManager-1.5.0-BUILD2647-x86_64-portable.exe`).
2. **Checksum**: the `SHA256SUMS.txt` value from the release page, or the output of `Get-FileHash -Algorithm SHA256` on the asset, so we can confirm your build is not repackaged.
3. **Windows version and privilege context**: the exact Windows build, whether the app was started elevated, and the UAC setting.
4. **Reproduction steps**: the minimal steps, including the original state of the relevant startup items, what you did, and expected versus actual results.
5. **Impact assessment**: if known, whether it achieves ordinary user → administrator, or only misuse that requires user interaction.
6. **Other distributions** (optional): whether it also reproduces in `normal`, `runtimeless` or `portable_runtimeless`.

If the report concerns something only verifiable through CI artefacts (missing assets, checksum mismatches), please note which fields of the release page you checked.

---

## Response times and severity levels

Timelines reflect the real cadence of a single-maintainer open-source project; there is no invented SLA:

| Severity | Definition | First response | Target fix |
| --- | --- | --- | --- |
| **Critical** | Ordinary user → administrator with no or minimal interaction; or remote poisoning impersonating an official release | Within 48 hours | Patch release within 7 days |
| **High** | The same escalation but requiring specific conditions (e.g. a malicious name or a writable config directory), or the ability to make the app run unintended commands | Within 3 days | Before the next stable release, or a pre-release fix within 30 days |
| **Medium** | Requires local administrator context, or information disclosure without escalation, or only applicable if the user weakens their own defences (disabling UAC, weak passwords) | Within 7 days | With a regular release |
| **Low** | Impact limited by local administrator context, requires deliberate configuration, or is a non-security functional issue | Within 7 days | Fixed with a regular release when practical, or explicitly declined with reasoning |
| **Not affected / invalid** | Out of scope, or not causally linked to product security | Within 7 days | No fix, with an explanation |

**How severity is judged:**

- Any escalation feasibility tied to `requireAdministrator` starts at **High**.
- Reports with a working PoC usually shorten triage and fixing noticeably.
- During triage I may ask you to confirm whether a version still reproduces; that is normal procedure.

**Deferral or downgrade**: if a report depends on unreleased functionality, or the affected area is being replaced by an architectural refactor in the next version, I will say so explicitly and give a remediation path. That is not a refusal to accept the report.

---

## Fixes and releases

1. **Fixes ship with stable releases.** Standalone security patches are only issued for long-running high-severity cases.
2. **Version numbers follow the README naming rules** and carry `BUILDYYWW` like any other release:
   - The stable release that fixes a defect keeps that week's build number; there are no separate security-only version numbers.
   - High-severity gaps may be delivered through the pre-release (`rc`) channel without changing the nightly pipeline.
3. **Verification**: every security fix's release note states the affected version range, the fixed version and a verifiable SHA-256, so users can tell whether their build is affected.
4. **Disclosure**: with the reporter's consent, their name or GitHub handle is credited in the release note; anonymous requests are honoured.

---

## Known boundary cases

The following are confirmed **by design** and are not vulnerabilities:

| Case | Assessment |
| --- | --- |
| The tool needs administrator rights and shows a UAC prompt | Intentional. Without elevation some services and tasks cannot be inventoried correctly. |
| The single-file build extracts to a temporary directory | Intentional; an unavoidable consequence of single-file plus ReadyToRun. The extraction directory is not a trust boundary—never use it for integrity checks. |

---

## Unexpected capability or execution

If you find the app has a **capability it should not have**, or it **executes something without being asked** (network access, writes outside expected paths, elevation of unexpected targets):

1. Treat it as the highest severity and file an **Advisory**, not a public issue.
2. If users may already be affected, state the recommended remediation direction (whether users should uninstall or roll back) and lead with the risk assessment.

---

## Counterfeit distribution

Reports of fake distribution channels are welcome:

| Channel | Handling |
| --- | --- |
| Fake official sites / download lures | DMCA and platform complaints first; legal action is not excluded |
| Tampered, re-signed or hash-swapped repackages | Same as above, with an announcement in the release notes and README |

---

## Final note

If a threat was missed because of a judgement call or a knowledge gap, additions are welcome. Contact channel is the same as the repository: **GitHub Security Advisories**.

*Version: 2026-11 · Maintainer: Xiayue · Apache License 2.0*
