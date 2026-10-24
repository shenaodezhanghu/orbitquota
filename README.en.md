<p align="center"><img src="docs/assets/logo.svg" width="88" alt="OrbitQuota" /></p>
<h1 align="center">OrbitQuota</h1>
<p align="center"><strong>Your quota, in orbit.</strong><br/>A native Windows desktop companion for Codex limits, tokens, and API-equivalent usage.</p>
<p align="center"><a href="README.md">简体中文</a> · <a href="https://github.com/shenaodezhanghu/orbitquota/releases/latest">Download</a> · <a href="LICENSE">MIT</a></p>
<img src="docs/assets/hero.svg" alt="OrbitQuota" width="100%" />

## A little observatory

Earth stays in the center. The Sun follows the outer weekly orbit; the Moon follows the inner five-hour orbit. Both move clockwise according to real quota reset timestamps.

- **Position means time.** Rays show weekly quota remaining; the Moon's lit area shows five-hour quota remaining.
- **Day and night.** A flat Earth illustration lights its sun-facing half. This is a quota metaphor, not an astronomical simulation.
- **Daily usage.** Fixed hourly output, average tokens per minute, and automatic per-request model pricing.
- **Native desktop.** Drag, remember position, choose a monitor, hide to tray, and reduce motion.
- **Local processing.** No Electron, browser UI, always-on Python service, or third-party NuGet packages.

<table><tr><th>Observatory</th><th>Quotas</th><th>Daily usage</th></tr><tr><td><img src="docs/assets/compact.png" width="210" alt="Demo observatory" /></td><td><img src="docs/assets/quota.png" width="270" alt="Demo quotas" /></td><td><img src="docs/assets/usage.png" width="270" alt="Demo usage" /></td></tr></table>

Screenshots are actual application renders in **Demo mode**, with sample data only.

## Run

1. Install [.NET 10 Desktop Runtime for Windows x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).
2. Install and sign in to the official Codex CLI. No additional API key is needed.
3. Download `OrbitQuota-win-x64.zip` from [Releases](https://github.com/shenaodezhanghu/orbitquota/releases/latest).
4. Extract the entire archive and open `OrbitQuota.exe`; keep the DLL and runtime JSON files beside it.

This is an unsigned Windows x64 preview without an installer or auto-updater. Source currently targets `net7.0-windows`, with `LatestMajor` runtime roll-forward tested on .NET 10 Desktop Runtime.

In a source checkout, double-click `启动.vbs`, or run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\start.ps1
```

The initial source build needs a .NET SDK. Hover for a peek, click for Quota / Usage, drag blank space to move, or right-click for settings and Exit. The tray reopens a hidden window. Only one normal instance runs per user.

## Data and privacy

Account quotas come from the official local `codex app-server` every 120 seconds; positions are computed locally between queries. Missing data stays unavailable; expired windows do not invent a reset.

Tokens come from local logs, including subagents, on the local calendar day. The chart has fixed 00–24 hourly slots. Daily average output divides today's output tokens by elapsed minutes since midnight, including idle time. The scene's rate instead counts the last 60 seconds of output.

**USD is API-equivalent value, not a subscription charge.** Each request uses its own model. Unknown prices are explicitly excluded: partial totals use `≥`, wholly unpriced periods use `—`. Prices were verified on **2026-10-22**, not continuously updated. This is not a complete invoice for tools, special service tiers, or regional rates. TTFT is not displayed without reliable timing.

The app does not directly read/display `auth.json`, send prompts, create chats, upload transcript text, expose HTTP, add startup entries, or modify business projects. Authentication and quota requests are delegated to the official local Codex process. Temporary errors use labeled local snapshots. Log deletion, migration, or format changes can limit coverage.

## Build

```powershell
dotnet build -c Release
$test = Start-Process .\bin\Release\net7.0-windows\CodexIsland.exe -ArgumentList '--self-test' -Wait -PassThru
if ($test.ExitCode -ne 0) { throw 'Self-test failed' }
.\package.ps1
```

The internal project/assembly retains the `CodexIsland` name for launcher compatibility; the public portable executable is `OrbitQuota.exe`. Tagged builds create the ZIP and SHA-256 checksum. See [technical notes](docs/TECHNICAL.md).

Presentation inspired by [CodexBar](https://github.com/steipete/CodexBar), [Boring Notch](https://github.com/TheBoredTeam/boring.notch), and [CodexIsland](https://github.com/ericjypark/codex-island). Independent, not affiliated with OpenAI. [MIT license](LICENSE).
