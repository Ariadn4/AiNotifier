# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

AI Notifier is a lightweight Windows desktop floating-ball (悬浮球) app that plays a sound alert when an AI coding assistant (Claude Code, Cursor, GitHub Copilot, etc.) completes a task or sends a notification. Trigger via `curl http://localhost:19836/stop` (completion) or `curl http://localhost:19836/notify` (notification).

## Tech Stack

- **Language**: C# / WPF
- **Framework**: .NET 8 (self-contained single-file deployment)
- **Audio**: `System.Media.SoundPlayer` for looping `.wav` playback
- **HTTP**: `HttpListener` on `localhost:19836`
- **Win32**: `GetLastInputInfo` for user activity detection

## Build & Run

```bash
# Build
dotnet build src/AiNotifier/AiNotifier.csproj

# Run
dotnet run --project src/AiNotifier/AiNotifier.csproj

# Publish portable (文件夹版本，推荐用于分发)
dotnet publish src/AiNotifier/AiNotifier.csproj -c Release -r win-x64 --self-contained -o publish/portable

# Publish single-file exe (仅供本机使用)
dotnet publish src/AiNotifier/AiNotifier.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/single-file
```

## 开发调试流程

每次修改代码后，**必须**执行以下流程让用户体验最新版本：

```bash
# 1. 关闭正在运行的进程
taskkill //f //im AiNotifier.exe 2>/dev/null; sleep 2

# 2. 发布
dotnet publish src/AiNotifier/AiNotifier.csproj -c Release -r win-x64 --self-contained -o publish/portable

# 3. 启动
publish/portable/AiNotifier.exe &
```

三步合一，不要分开执行，不要询问用户是否关闭进程。

## Publish 目录结构

所有发布产物统一输出到项目根目录 `publish/` 下，按类型分子文件夹存放：

```
publish/
├── portable/       # 文件夹版本（推荐分发）
└── single-file/    # 单文件 exe 版本
```

如有其他分类需求（如不同架构 x86/x64），在 `publish/` 下新建对应子文件夹。

## 分发注意事项

**分发给他人时必须使用文件夹（portable）方式**，将整个 `publish/portable/` 目录打包为 zip 发送。不要只发单个 exe 文件 — 杀毒软件会在复制单文件 exe 时破坏其内嵌的打包结构，导致对方无法运行（报 STATUS_ILLEGAL_INSTRUCTION 错误）。

## Architecture

The app has five core modules, all under `src/AiNotifier/`:

- **MainWindow** — WPF borderless topmost window. A draggable robot ball that changes color by state (blue=on, gray=off, amber=stop-ringing, purple=notification-ringing). Left-click toggles alerts; right-click opens context menu.
- **NotifyServer** — Background `HttpListener` on port 19836. `GET /stop` triggers stop alert; `GET /notify` triggers notification alert; `GET /bubble` triggers bubble; `GET /status` returns state.
- **SoundManager** — Wraps `MediaPlayer` for alert sound playback (looping/once/preview).
- **UserActivityDetector** — Polls `GetLastInputInfo` every second during ringing; stops alert when new user input is detected.
- **AutoStartManager** — Reads/writes `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` registry key for startup toggle.
- **HookManager** — Manages Claude Code hooks in `~/.claude/settings.json` (Stop, Notification, UserPromptSubmit).
- **SoundSettingsWindow** — Dialog for configuring per-alert-type sounds, volume, and gradual volume.

## Key Flow

1. HTTP request hits `/stop` or `/notify` → NotifyServer fires event to UI thread
2. If that alert type is enabled: select matching sound, record `LastInputInfo`, start looping sound, change ball color (amber for stop, purple for notification)
3. Sound stops when: user clicks ball, new keyboard/mouse input detected, or timeout

## Constraints

- Single instance enforced via named `Mutex`
- Memory < 30MB, idle CPU ≈ 0%
- Primary language in docs/UI is Chinese (简体中文)

## Documentation Sync

When making any code changes (new features, bug fixes, refactors, architecture changes), **must** also update the corresponding documentation:

- `docs/PRD.md` — Update if the change affects product features, user-facing behavior, or non-functional requirements
- `docs/tech-design.md` — Update if the change affects architecture, modules, data flow, or project structure
