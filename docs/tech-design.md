# AI Notifier - 技术设计方案

## 项目结构

```
F:/AiNotifier/
├── docs/
│   ├── PRD.md                          # 产品需求文档
│   ├── tech-design.md                  # 技术设计（本文件）
│   └── ai-ping-skyblue-robot.html      # UI 设计稿（天蓝小机器人）
├── src/
│   └── AiNotifier/
│       ├── AiNotifier.csproj
│       ├── App.xaml / App.xaml.cs
│       ├── MainWindow.xaml / MainWindow.xaml.cs   # 悬浮球窗口（机器人 UI + 动画）
│       ├── NotifyServer.cs                        # HTTP 监听服务
│       ├── SoundManager.cs                        # 音频播放管理
│       ├── SettingsManager.cs                     # JSON 设置持久化
│       ├── AutoStartManager.cs                    # 开机自启注册表管理
│       ├── UserActivityDetector.cs                # 用户活动检测（Win32）
│       ├── HookManager.cs                         # Claude Code Hook 管理
│       ├── BubbleWindow.xaml / BubbleWindow.xaml.cs           # 碎碎念窗口
│       ├── NotificationBubbleWindow.xaml / .xaml.cs           # 项目通知气泡窗口
│       ├── MessageEditorWindow.xaml / MessageEditorWindow.xaml.cs  # 消息编辑对话框
│       ├── SoundSettingsWindow.xaml / SoundSettingsWindow.xaml.cs  # 音效设置弹窗
│       └── Resources/
│           ├── alert-1.wav ~ alert-4.wav          # 4 个内置提示音
│           ├── bubble-pop.mp3                     # 气泡音效
│           └── app.ico                            # 应用图标（天蓝机器人）
├── tools/
│   └── IconGen/                                   # ICO 图标生成工具
├── .gitignore
└── README.md
```

## 模块设计

### 1. MainWindow（悬浮球窗口）

- WPF 无边框窗口，`WindowStyle=None`，`AllowsTransparency=True`，`Topmost=True`
- 窗口尺寸 96×96 像素（72px 球体 + 光晕/脉冲波纹边距）
- 内容结构（从底到顶）：
  - **脉冲波纹层**：3 个 Ellipse，仅 ALERT 状态显示，Stroke 颜色通过命名 Brush 动态设置
  - **球体容器** Grid：包含摇摆/悬停变换
    - **球体** Ellipse (72×72)：`RadialGradientBrush` 填充（GradientOrigin 0.38,0.30），`DropShadowEffect` 光晕
    - **机器人** Viewbox (46×46) → Canvas (100×80)：SVG 转 XAML 的机器人角色
- 机器人角色分为共享元素 + 三组状态表情 Canvas：
  - **共享元素**（始终存在，Opacity 随状态变化）：天线杆 Line、头部 Rectangle (rx=14)、左右耳 Rectangle、身体 Rectangle + 3 圆点
  - **RobotOn** Canvas：椭圆眼（天蓝 #0284c7 + 白色高光）+ 粉色腮红 + 微笑 Path 曲线
  - **RobotAlert** Canvas：圆形惊喜眼 + 白色瞳孔 + 椭圆张嘴 + "!" 文字。眼睛/嘴巴颜色通过命名 Brush 动态设置（Stop 用琥珀色，Notification 用紫色）
  - **RobotOff** Canvas：水平线闭眼 + 直线嘴 + "zzZ" 文字
- 拖拽：`MouseLeftButtonDown` → `DragMove()`（3px 阈值区分拖拽和单击）
- 悬停：`ScaleTransform` 动画放大到 1.1 倍 + `ToolTip` 显示状态文字
- 右键菜单：浅色主题自定义 ControlTemplate（#FFFFFF 背景，#E2E8F0 边框，圆角 12px）

#### 动画系统（WPF Storyboard）

所有动画定义在 `Window.Resources` 中，由代码 `Begin()/Stop()` 控制：

| 动画 | 状态 | 说明 |
|------|------|------|
| `EyeBlinkStory` | ON | ScaleY 关键帧动画，~3.8s 周期，44% 处眨眼 |
| `AntennaGlowStory` | ON | 天线尖端 Opacity 0.5↔1.0，2.5s 周期 |
| `WiggleStory` | ALERT | RotateTransform ±4° + Scale 1.0↔1.04，0.35s |
| `RippleStory` | ALERT | 3 个波纹环 Scale(1→2.3) + Opacity(0.55→0)，1.2s 间隔 0.4s |
| `AntennaFlashStory` | ALERT | 天线尖端 Opacity 0.3↔1.0，0.5s 快闪 |

#### 状态管理

- `AppState` 枚举：`Enabled`、`Disabled`、`RingingStop`、`RingingNotification`
- `AlertType` 枚举：`Stop`、`Notification`
- `IsRinging` 属性：`_state is RingingStop or RingingNotification`
- `ApplyVisualState(AppState)` 统一方法：停止所有 Storyboard → 设置渐变色/阴影/表情可见性/共享元素透明度 → 启动对应 Storyboard
- 两种 Ringing 状态共用 `ApplyRingingVisuals()` 辅助方法，通过参数传入不同颜色
- 颜色方案：

| 状态 | 渐变内圈 | 渐变外圈 | 阴影色 | 面部颜色 |
|------|----------|----------|--------|----------|
| Enabled | #7dd3fc | #0284c7 | #38bdf8 | — |
| RingingStop | #fbbf24 | #d97706 | #fbbf24 | #d97706 |
| RingingNotification | #c084fc | #9333ea | #c084fc | #9333ea |
| Disabled | #6b7280 | #374151 | #000000 | — |

#### 左键点击切换逻辑

- 记录 `_lastStopEnabled` 和 `_lastNotificationEnabled` 字段
- Enabled → Disabled：保存当前开关组合，关闭所有
- Disabled → Enabled：恢复上次组合（若上次全关则默认全开）
- Ringing → StopAlert()

### 2. NotifyServer（HTTP 监听）

- 使用 `HttpListener` 监听 `http://localhost:19836/`
- 路由：
  - `GET /notify` → 触发通知提醒（AlertType.Notification）
  - `GET /stop` → 触发回应完毕提醒（AlertType.Stop）
  - `GET /bubble` → 触发气泡提醒
  - `GET /status` → 返回当前状态
- 后台线程运行，通过事件通知 UI 线程

### 3. SoundManager（音频管理）

- 使用 `MediaPlayer` 播放音效文件
- 内置 4 个提示音（alert-1 ~ alert-4）+ bubble-pop 音效
- `SetSound(soundId, customPath)` 切换音效
- `PlayLooping()` 长提醒循环播放
- `PlayOnce()` 短提醒单次播放
- `Preview(soundId, customPath)` 预览音效
- `Stop()` 停止播放
- StartAlert 根据 AlertType 调用 `SetSound` 选择对应音效后播放

### 4. SettingsManager（设置持久化）

- 存储路径：`%LOCALAPPDATA%\AiNotifier\settings.json`
- `AppSettings` 类字段：
  - `StopAlertEnabled` / `NotificationAlertEnabled`：两种提醒的独立开关
  - `StopSoundId` / `StopCustomSoundPath`：回应完毕提示音设置
  - `NotificationSoundId` / `NotificationCustomSoundPath`：通知提示音设置
  - `Volume`：共享音量（0-1）
  - `GradualVolume`：渐强音量开关
  - `ShortMode`：短提醒模式
  - `AlertTimeoutSeconds`：长提醒超时
  - `BubbleEnabled` / `BubbleCooldownMinutes` / `CustomBubbleMessages`：气泡设置
- 旧版本迁移：`SoundId` → `StopSoundId`，`CustomSoundPath` → `StopCustomSoundPath`

### 5. UserActivityDetector（用户活动检测）

- 调用 Win32 API `GetLastInputInfo` 获取最后一次输入时间
- 在响铃期间（长提醒和短提醒均启用），每秒检测一次
- 如果检测到新的用户输入 → 停止声音和视觉效果
- 使用 `DispatcherTimer` 定时轮询

### 6. AutoStartManager（开机自启）

- 读写注册表 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
- Key: `AiNotifier`，Value: 可执行文件路径
- 提供 `IsEnabled` / `Enable()` / `Disable()` 方法

### 7. HookManager（Claude Code Hook 管理）

- 读写 `~/.claude/settings.json` 中的 hooks 配置
- 绑定时同时添加三个 hook：
  - `Stop` → `curl -s http://localhost:19836/stop`（回应完毕提醒）
  - `Notification` → `curl -s http://localhost:19836/notify`（通知提醒）
  - `UserPromptSubmit` → `curl -s http://localhost:19836/bubble`（碎碎念）
- 解绑时同时移除三个 hook

### 8. BubbleWindow（碎碎念窗口）

- 独立的无边框透明 Topmost 窗口（不影响 MainWindow 布局）
- 白色圆角卡片（#FFFFFF 背景），底部与悬浮球重叠
- 定位逻辑：默认在悬浮球下方，屏幕底部时切换到上方
- 动画：淡入 300ms → 停留 10 秒 → 淡出 500ms → 自动关闭

### 8.1 NotificationBubbleWindow（项目通知气泡）

- 独立的无边框透明 Topmost 窗口，与 BubbleWindow 类似但行为不同
- 可点击关闭（`IsHitTestVisible=True`），显示 Hand 光标
- 累积显示多条项目通知（每行一条），如 "AiNotifier 回复完毕"
- 内置活动检测：轮询 `GetLastInputTick()`，检测到用户活动后启动 5 秒倒计时淡出
- 无固定超时 — 持续显示直到用户点击或活动后 5 秒
- 新消息到达时：追加到列表，重置倒计时，取消淡出动画
- 通过 HTTP 请求的 `?cwd=` 查询参数获取项目路径，提取最后一级目录名作为项目标识

### 9. SoundSettingsWindow（音效设置弹窗）

- 模态对话框，Owner 为 MainWindow
- 布局分为三个区域：
  - **回应完毕提示音**：RadioButton 选择内置音效或自定义文件，hover 预览
  - **通知提示音**：同上
  - **音量/渐强**：共享音量滑块 + 渐强音量开关
- 确定后将所有设置写回 AppSettings

### 10. MessageEditorWindow（消息编辑对话框）

- 浅色模态对话框，多行 TextBox 编辑提醒消息（一行一条）
- 确定后按换行拆分保存到 `AppSettings.CustomBubbleMessages`

## 关键流程

### 提醒触发流程

```
Claude Code 停止（等待用户响应）
    → Stop hook 触发 curl http://localhost:19836/stop
    → NotifyServer 收到请求，触发 StopRequested 事件
    → StartAlert(AlertType.Stop)
    → 检查 StopAlertEnabled？
        → 否：返回，不做任何事
        → 是：
            → SetSound(StopSoundId, StopCustomSoundPath)
            → _state = RingingStop
            → ApplyVisualState(RingingStop) — 琥珀色球体
            → 根据提醒模式播放音效

Claude Code 发送通知
    → Notification hook 触发 curl http://localhost:19836/notify
    → NotifyServer 收到请求，触发 NotifyRequested 事件
    → StartAlert(AlertType.Notification)
    → 检查 NotificationAlertEnabled？
        → 否：返回，不做任何事
        → 是：
            → SetSound(NotificationSoundId, NotificationCustomSoundPath)
            → _state = RingingNotification
            → ApplyVisualState(RingingNotification) — 紫色球体
            → 根据提醒模式播放音效
```

### 停止流程（声音与视觉分离）

提醒停止分为两个独立阶段：

**停止声音（StopSound）** — 声音/计时器停止，视觉保持：
```
触发条件：
  - 长提醒超时（可配置 15/30/45/60 秒）
  - 短提醒 4 秒计时器到期
```

**停止视觉（StopVisual）** — 悬浮球恢复监听样式：
```
触发条件：
  - UserActivityDetector 检测到新的键盘/鼠标输入
    → 停止声音（如仍在播放）+ 恢复 Enabled 状态
```

**完全停止（StopAlert）** — 用户点击悬浮球时同时停止声音和视觉。

### 渐强音量

- 仅长提醒模式下生效，通过设置开关控制
- 使用 DispatcherTimer（200ms 间隔）将音量从 0 线性递增到用户设定值
- 以 30 秒升到 100% 的固定速率渐增（200ms 步进），起始音量 1%，到达用户设定音量后停止
- 无论长提醒/短提醒，至少保证播放完 1 次完整音效（通过 `FirstPlayCompleted` 事件延迟停止）
- 声音停止时自动清理渐强定时器并恢复音量设定

## 单实例保证

使用 `Mutex` 确保只有一个实例运行：

```csharp
private static Mutex _mutex = new Mutex(true, "AiNotifier_SingleInstance");
if (!_mutex.WaitOne(TimeSpan.Zero, true)) {
    // 已有实例运行，退出
    return;
}
```

## 构建与发布

```bash
# 文件夹版本（推荐分发）
dotnet publish src/AiNotifier/AiNotifier.csproj -c Release -r win-x64 --self-contained -o publish/portable

# 单文件 exe（仅供本机使用）
dotnet publish src/AiNotifier/AiNotifier.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/single-file
```
