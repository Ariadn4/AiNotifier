using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace AiNotifier;

public partial class MainWindow : Window
{
    private enum AppState { Enabled, Disabled, RingingStop, RingingNotification }
    private enum AlertType { Stop, Notification }

    private bool IsRinging => _state is AppState.RingingStop or AppState.RingingNotification;

    // Gradient color pairs per state
    private static readonly Color EnabledInner = Color.FromRgb(0x7d, 0xd3, 0xfc);
    private static readonly Color EnabledOuter = Color.FromRgb(0x02, 0x84, 0xc7);
    private static readonly Color DisabledInner = Color.FromRgb(0x6b, 0x72, 0x80);
    private static readonly Color DisabledOuter = Color.FromRgb(0x37, 0x41, 0x51);

    // Stop ringing colors (amber/orange)
    private static readonly Color StopRingingInner = Color.FromRgb(0xfb, 0xbf, 0x24);
    private static readonly Color StopRingingOuter = Color.FromRgb(0xd9, 0x77, 0x06);
    private static readonly Color ShadowStopRinging = Color.FromRgb(0xfb, 0xbf, 0x24);
    private static readonly Color DotStopRinging1 = Color.FromRgb(0xd9, 0x77, 0x06);
    private static readonly Color DotStopRinging2 = Color.FromRgb(0xfb, 0xbf, 0x24);
    private static readonly Color AlertFaceStop = Color.FromRgb(0xd9, 0x77, 0x06);
    private static readonly Color AlertAntennaStop = Color.FromRgb(0xfe, 0xf0, 0x8a);

    // Notification ringing colors (purple/violet)
    private static readonly Color NotificationRingingInner = Color.FromRgb(0xc0, 0x84, 0xfc);
    private static readonly Color NotificationRingingOuter = Color.FromRgb(0x93, 0x33, 0xea);
    private static readonly Color ShadowNotificationRinging = Color.FromRgb(0xc0, 0x84, 0xfc);
    private static readonly Color DotNotificationRinging1 = Color.FromRgb(0x93, 0x33, 0xea);
    private static readonly Color DotNotificationRinging2 = Color.FromRgb(0xc0, 0x84, 0xfc);
    private static readonly Color AlertFaceNotification = Color.FromRgb(0x93, 0x33, 0xea);
    private static readonly Color AlertAntennaNotification = Color.FromRgb(0xe9, 0xd5, 0xff);

    // Shadow colors
    private static readonly Color ShadowEnabled = Color.FromRgb(0x38, 0xbd, 0xf8);
    private static readonly Color ShadowDisabled = Color.FromRgb(0x00, 0x00, 0x00);

    // Body dot colors
    private static readonly Color DotEnabled1 = Color.FromRgb(0x02, 0x84, 0xc7);
    private static readonly Color DotEnabled2 = Color.FromRgb(0x38, 0xbd, 0xf8);

    private AppState _state = AppState.Enabled;
    private AppSettings _settings;
    private readonly NotifyServer _server;
    private readonly SoundManager _sound;
    private readonly UserActivityDetector _activityDetector;
    private readonly DispatcherTimer _timeoutTimer;
    private readonly DispatcherTimer _shortTimer;

    // Volume ramp
    private DispatcherTimer? _volumeRampTimer;
    private double _volumeRampTarget;
    private double _volumeRampStep;

    // Guarantee at least 1 full play before stopping
    private bool _pendingStopAfterPlay;

    // System tray
    private WinForms.NotifyIcon? _trayIcon;

    // Storyboards
    private Storyboard? _eyeBlinkStory;
    private Storyboard? _antennaGlowStory;
    private Storyboard? _wiggleStory;
    private Storyboard? _rippleStory;
    private Storyboard? _antennaFlashStory;

    // Bubble
    private BubbleWindow? _bubbleWindow;
    private DateTime _lastBubbleTime = DateTime.MinValue;
    private readonly Random _bubbleRandom = new();

    // Project notification bubble
    private NotificationBubbleWindow? _notificationBubbleWindow;

    private static readonly string[] DefaultBubbleMessages =
    [
        "喝杯水吧",
        "站起来走走",
        "眺望远处20秒",
    ];

    // Drag vs click tracking
    private System.Windows.Point _mouseDownPos;
    private bool _isDragging;

    // Left-click toggle: remember last enabled combination
    private bool _lastStopEnabled = true;
    private bool _lastNotificationEnabled = true;

    public MainWindow()
    {
        InitializeComponent();

        // Load settings
        _settings = SettingsManager.Load();

        _sound = new SoundManager();
        _sound.Volume = _settings.Volume;
        _sound.SetSound(_settings.StopSoundId, _settings.StopCustomSoundPath);

        _activityDetector = new UserActivityDetector();
        _activityDetector.UserActivityDetected += OnUserActivity;

        _sound.PlaybackStarted += OnPlaybackStarted;
        _sound.FirstPlayCompleted += OnFirstPlayCompleted;

        NotifyServer.CleanupLog();
        _server = new NotifyServer(GetStatusText);
        _server.StopRequested += (cwd) => { StartAlert(AlertType.Stop); ShowProjectBubble(cwd, AlertType.Stop); };
        _server.NotifyRequested += (cwd) => { StartAlert(AlertType.Notification); ShowProjectBubble(cwd, AlertType.Notification); };
        _server.BubbleRequested += ShowBubble;

        _timeoutTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Clamp(_settings.AlertTimeoutSeconds, 15, 60))
        };
        _timeoutTimer.Tick += (_, _) => OnTimerStop();

        _shortTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _shortTimer.Tick += (_, _) => OnTimerStop();

        // Initialize last enabled state from settings
        _lastStopEnabled = _settings.StopAlertEnabled;
        _lastNotificationEnabled = _settings.NotificationAlertEnabled;

        // Determine initial state
        if (!_settings.StopAlertEnabled && !_settings.NotificationAlertEnabled)
            _state = AppState.Disabled;

        InitTrayIcon();
    }

    #region System Tray

    private void InitTrayIcon()
    {
        _trayIcon = new WinForms.NotifyIcon();

        try
        {
            var iconUri = new Uri("pack://application:,,,/Resources/app.ico", UriKind.Absolute);
            var iconStream = Application.GetResourceStream(iconUri)?.Stream;
            if (iconStream != null)
                _trayIcon.Icon = new System.Drawing.Icon(iconStream);
        }
        catch
        {
            _trayIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? "");
        }

        _trayIcon.Text = "AI Ping - 监听中";
        _trayIcon.Visible = true;

        _trayIcon.DoubleClick += (_, _) =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        };

        var menu = new WinForms.ContextMenuStrip();

        var showItem = new WinForms.ToolStripMenuItem("显示悬浮球");
        showItem.Click += (_, _) =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            var workArea = SystemParameters.WorkArea;
            if (Left < workArea.Left || Left > workArea.Right || Top < workArea.Top || Top > workArea.Bottom)
            {
                Left = workArea.Right - Width - 20;
                Top = workArea.Height * 2 / 3;
            }
        };
        menu.Items.Add(showItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());

        var exitItem = new WinForms.ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => Dispatcher.Invoke(() => Application.Current.Shutdown());
        menu.Items.Add(exitItem);

        _trayIcon.ContextMenuStrip = menu;
    }

    private void UpdateTrayTooltip()
    {
        if (_trayIcon == null) return;
        _trayIcon.Text = _state switch
        {
            AppState.Enabled => "AI Ping - 监听中",
            AppState.Disabled => "AI Ping - 已关闭",
            AppState.RingingStop => "AI Ping - 回应完毕提醒中",
            AppState.RingingNotification => "AI Ping - 通知提醒中",
            _ => "AI Ping"
        };
    }

    #endregion

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _eyeBlinkStory = (Storyboard)FindResource("EyeBlinkStory");
            _antennaGlowStory = (Storyboard)FindResource("AntennaGlowStory");
            _wiggleStory = (Storyboard)FindResource("WiggleStory");
            _rippleStory = (Storyboard)FindResource("RippleStory");
            _antennaFlashStory = (Storyboard)FindResource("AntennaFlashStory");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Storyboard init error: {ex.Message}");
        }

        if (_settings.WindowLeft.HasValue && _settings.WindowTop.HasValue)
        {
            Left = _settings.WindowLeft.Value;
            Top = _settings.WindowTop.Value;
        }
        else
        {
            Left = SystemParameters.WorkArea.Right - Width - 20;
            Top = SystemParameters.WorkArea.Height / 3;
        }

        ApplyVisualState(_state);

        try
        {
            _server.Start();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"无法启动 HTTP 服务：{ex.Message}\n请检查端口 19836 是否被占用。",
                "AI Notifier", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    #region Visual State

    private void ApplyVisualState(AppState state)
    {
        try
        {
            _eyeBlinkStory?.Stop(this);
            _antennaGlowStory?.Stop(this);
            _wiggleStory?.Stop(this);
            _rippleStory?.Stop(this);
            _antennaFlashStory?.Stop(this);
        }
        catch { }

        switch (state)
        {
            case AppState.Enabled:
                GradientInner.Color = EnabledInner;
                GradientOuter.Color = EnabledOuter;
                BallShadow.Color = ShadowEnabled;
                BallShadow.BlurRadius = 20;
                BallShadow.Opacity = 0.4;
                RobotOn.Visibility = Visibility.Visible;
                RobotAlert.Visibility = Visibility.Collapsed;
                RobotOff.Visibility = Visibility.Collapsed;
                OnAntennaTip.Visibility = Visibility.Visible;
                AlertAntennaTip.Visibility = Visibility.Collapsed;
                OffAntennaTip.Visibility = Visibility.Collapsed;
                AntennaStem.Stroke = Brushes.White;
                Head.Opacity = 0.95;
                LeftEar.Opacity = 0.8;
                RightEar.Opacity = 0.8;
                Body.Opacity = 0.7;
                Dot1Brush.Color = DotEnabled1;
                Dot2Brush.Color = DotEnabled2;
                Dot3Brush.Color = DotEnabled1;
                BodyDot1.Opacity = 0.5;
                BodyDot2.Opacity = 0.5;
                BodyDot3.Opacity = 0.5;
                BallTooltip.Content = "监听中";
                try { _eyeBlinkStory?.Begin(this, true); _antennaGlowStory?.Begin(this, true); } catch { }
                break;

            case AppState.RingingStop:
                ApplyRingingVisuals(
                    StopRingingInner, StopRingingOuter, ShadowStopRinging,
                    DotStopRinging1, DotStopRinging2,
                    AlertFaceStop, AlertAntennaStop, StopRingingInner,
                    "回应完毕提醒中");
                break;

            case AppState.RingingNotification:
                ApplyRingingVisuals(
                    NotificationRingingInner, NotificationRingingOuter, ShadowNotificationRinging,
                    DotNotificationRinging1, DotNotificationRinging2,
                    AlertFaceNotification, AlertAntennaNotification, NotificationRingingInner,
                    "通知提醒中");
                break;

            case AppState.Disabled:
                GradientInner.Color = DisabledInner;
                GradientOuter.Color = DisabledOuter;
                BallShadow.Color = ShadowDisabled;
                BallShadow.BlurRadius = 4;
                BallShadow.Opacity = 0.2;
                RobotOn.Visibility = Visibility.Collapsed;
                RobotAlert.Visibility = Visibility.Collapsed;
                RobotOff.Visibility = Visibility.Visible;
                OnAntennaTip.Visibility = Visibility.Collapsed;
                AlertAntennaTip.Visibility = Visibility.Collapsed;
                OffAntennaTip.Visibility = Visibility.Visible;
                OffAntennaTip.Opacity = 0.15;
                AntennaStem.Stroke = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
                Head.Opacity = 0.4;
                LeftEar.Opacity = 0.3;
                RightEar.Opacity = 0.3;
                Body.Opacity = 0.25;
                BodyDot1.Opacity = 0;
                BodyDot2.Opacity = 0;
                BodyDot3.Opacity = 0;
                BallTooltip.Content = "已关闭";
                break;
        }

        UpdateTrayTooltip();
    }

    private void ApplyRingingVisuals(
        Color innerColor, Color outerColor, Color shadowColor,
        Color dot1Color, Color dot2Color,
        Color faceColor, Color antennaColor, Color rippleColor,
        string tooltip)
    {
        GradientInner.Color = innerColor;
        GradientOuter.Color = outerColor;
        BallShadow.Color = shadowColor;
        BallShadow.BlurRadius = 28;
        BallShadow.Opacity = 0.5;
        RobotOn.Visibility = Visibility.Collapsed;
        RobotAlert.Visibility = Visibility.Visible;
        RobotOff.Visibility = Visibility.Collapsed;
        OnAntennaTip.Visibility = Visibility.Collapsed;
        AlertAntennaTip.Visibility = Visibility.Visible;
        OffAntennaTip.Visibility = Visibility.Collapsed;
        AntennaStem.Stroke = Brushes.White;
        Head.Opacity = 0.95;
        LeftEar.Opacity = 0.8;
        RightEar.Opacity = 0.8;
        Body.Opacity = 0.7;
        Dot1Brush.Color = dot1Color;
        Dot2Brush.Color = dot2Color;
        Dot3Brush.Color = dot1Color;
        BodyDot1.Opacity = 0.7;
        BodyDot2.Opacity = 0.7;
        BodyDot3.Opacity = 0.7;

        // Dynamic alert face colors
        AlertLeftEyeBrush.Color = faceColor;
        AlertRightEyeBrush.Color = faceColor;
        AlertMouthBrush.Color = faceColor;
        AlertAntennaBrush.Color = antennaColor;

        // Dynamic ripple colors
        Ripple1Brush.Color = rippleColor;
        Ripple2Brush.Color = rippleColor;
        Ripple3Brush.Color = rippleColor;

        BallTooltip.Content = tooltip;
        try { _wiggleStory?.Begin(this, true); _rippleStory?.Begin(this, true); _antennaFlashStory?.Begin(this, true); } catch { }
    }

    #endregion

    #region Alert

    private void StartAlert(AlertType type)
    {
        // Check if this specific alert type is enabled
        if (type == AlertType.Stop && !_settings.StopAlertEnabled) return;
        if (type == AlertType.Notification && !_settings.NotificationAlertEnabled) return;
        if (_state == AppState.Disabled) return;

        if (IsRinging)
            StopAlert();

        _pendingStopAfterPlay = false;

        // Select sound based on alert type
        var soundId = type == AlertType.Stop ? _settings.StopSoundId : _settings.NotificationSoundId;
        var customPath = type == AlertType.Stop ? _settings.StopCustomSoundPath : _settings.NotificationCustomSoundPath;
        _sound.SetSound(soundId, customPath);

        _state = type == AlertType.Stop ? AppState.RingingStop : AppState.RingingNotification;
        ApplyVisualState(_state);

        if (_settings.ShortMode)
        {
            _sound.PlayOnce();
            _shortTimer.Start();
        }
        else
        {
            if (_settings.GradualVolume)
            {
                _sound.Volume = 0.01;
                _sound.PlayLooping();
                StartVolumeRamp();
            }
            else
            {
                _sound.PlayLooping();
            }
            _timeoutTimer.Start();
        }
        // Activity detector is started in OnPlaybackStarted with a fresh baseline
        // to avoid race between async media Open and user input detection
    }

    private void OnPlaybackStarted()
    {
        if (!IsRinging) return;
        var baselineTick = UserActivityDetector.GetLastInputTick();
        _activityDetector.Start(baselineTick);
    }

    private void OnUserActivity()
    {
        if (!IsRinging) return;

        if (!_sound.HasCompletedFirstPlay && _sound.IsPlaying)
        {
            // Defer stop — will be handled when first play completes
            _pendingStopAfterPlay = true;
            return;
        }
        StopSound();
        StopVisual();
    }

    private void OnTimerStop()
    {
        _timeoutTimer.Stop();
        _shortTimer.Stop();

        if (!_sound.HasCompletedFirstPlay && _sound.IsPlaying)
        {
            _pendingStopAfterPlay = true;
            return;
        }
        StopSound();
    }

    private void OnFirstPlayCompleted()
    {
        if (!IsRinging) return;

        if (_pendingStopAfterPlay)
        {
            _pendingStopAfterPlay = false;
            StopSound();
            // Re-arm activity detector for visual persistence
            var baseline = UserActivityDetector.GetLastInputTick();
            _activityDetector.Start(baseline);
        }
    }

    private void StopSound()
    {
        StopVolumeRamp();
        _sound.Stop();
        _timeoutTimer.Stop();
        _shortTimer.Stop();
    }

    private void StopVisual()
    {
        _activityDetector.Stop();
        _state = AppState.Enabled;
        ApplyVisualState(AppState.Enabled);
    }

    private void StopAlert()
    {
        if (!IsRinging)
            return;

        _pendingStopAfterPlay = false;
        StopSound();
        StopVisual();
    }

    private void StartVolumeRamp()
    {
        _volumeRampTarget = _settings.Volume;
        const double startVolume = 0.01;
        const double intervalMs = 200;
        // 以30秒从0%升到100%的固定速率，升到指定音量
        const double fullRampMs = 30000;
        var totalStepsForFull = (int)(fullRampMs / intervalMs);
        _volumeRampStep = (1.0 - startVolume) / totalStepsForFull;

        _volumeRampTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(intervalMs) };
        _volumeRampTimer.Tick += (_, _) =>
        {
            var newVol = _sound.Volume + _volumeRampStep;
            if (newVol >= _volumeRampTarget)
            {
                _sound.Volume = _volumeRampTarget;
                _volumeRampTimer!.Stop();
            }
            else
            {
                _sound.Volume = newVol;
            }
        };
        _volumeRampTimer.Start();
    }

    private void StopVolumeRamp()
    {
        _volumeRampTimer?.Stop();
        _sound.Volume = _settings.Volume;
    }

    private void OnBallClicked()
    {
        if (IsRinging)
        {
            StopAlert();
            return;
        }

        switch (_state)
        {
            case AppState.Enabled:
                // Save current combination before disabling
                _lastStopEnabled = _settings.StopAlertEnabled;
                _lastNotificationEnabled = _settings.NotificationAlertEnabled;
                _settings.StopAlertEnabled = false;
                _settings.NotificationAlertEnabled = false;
                _state = AppState.Disabled;
                ApplyVisualState(AppState.Disabled);
                SaveSettings();
                break;
            case AppState.Disabled:
                // Restore last combination (default to both if both were off)
                if (!_lastStopEnabled && !_lastNotificationEnabled)
                {
                    _lastStopEnabled = true;
                    _lastNotificationEnabled = true;
                }
                _settings.StopAlertEnabled = _lastStopEnabled;
                _settings.NotificationAlertEnabled = _lastNotificationEnabled;
                _state = AppState.Enabled;
                ApplyVisualState(AppState.Enabled);
                SaveSettings();
                break;
        }
    }

    private string GetStatusText()
    {
        return _state switch
        {
            AppState.Enabled => "enabled",
            AppState.Disabled => "disabled",
            AppState.RingingStop => "ringing",
            AppState.RingingNotification => "ringing",
            _ => "unknown"
        };
    }

    /// <summary>
    /// Recalculate app state based on current alert enable settings.
    /// </summary>
    private void RecalculateEnabledState()
    {
        if (IsRinging) return; // Don't change state while ringing

        if (_settings.StopAlertEnabled || _settings.NotificationAlertEnabled)
        {
            _state = AppState.Enabled;
            ApplyVisualState(AppState.Enabled);
        }
        else
        {
            _state = AppState.Disabled;
            ApplyVisualState(AppState.Disabled);
        }
    }

    #endregion

    #region Bubble

    private void ShowBubble()
    {
        if (!_settings.BubbleEnabled)
            return;

        // 日志气泡显示时不显示碎碎念
        if (_notificationBubbleWindow is { IsVisible: true })
            return;

        if (_settings.BubbleCooldownMinutes > 0 &&
            (DateTime.Now - _lastBubbleTime).TotalMinutes < _settings.BubbleCooldownMinutes)
            return;

        var messages = _settings.CustomBubbleMessages is { Count: > 0 }
            ? _settings.CustomBubbleMessages
            : DefaultBubbleMessages.ToList();

        var message = messages[_bubbleRandom.Next(messages.Count)];

        _bubbleWindow?.Close();
        _sound.PlayBubble(_settings.Volume);
        _bubbleWindow = new BubbleWindow(message);
        _bubbleWindow.ShowNear(this);
        _lastBubbleTime = DateTime.Now;
    }

    #endregion

    #region Project Notification Bubble

    private void ShowProjectBubble(string? cwd, AlertType type)
    {
        if (!_settings.ProjectBubbleEnabled || string.IsNullOrWhiteSpace(cwd))
            return;

        // 日志气泡出现时关闭碎碎念气泡
        _bubbleWindow?.Close();

        var projectName = Path.GetFileName(cwd.TrimEnd('/', '\\'));
        if (string.IsNullOrEmpty(projectName))
            projectName = cwd;

        var message = type == AlertType.Stop
            ? $"{projectName} 回复完毕"
            : $"{projectName} 发送通知";

        if (_notificationBubbleWindow is { IsVisible: true })
        {
            _notificationBubbleWindow.AddMessage(message);
        }
        else
        {
            _notificationBubbleWindow?.Close();
            _notificationBubbleWindow = new NotificationBubbleWindow();
            _notificationBubbleWindow.AddMessage(message);
            _notificationBubbleWindow.ShowNear(this);
            _notificationBubbleWindow.StartActivityWatch();
            _notificationBubbleWindow.Closed += (_, _) => _notificationBubbleWindow = null;
        }
    }

    #endregion

    #region Hover

    private void Ball_MouseEnter(object sender, MouseEventArgs e)
    {
        var anim = new DoubleAnimation(1.1, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BallHoverScale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        BallHoverScale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }

    private void Ball_MouseLeave(object sender, MouseEventArgs e)
    {
        var anim = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BallHoverScale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        BallHoverScale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }

    #endregion

    #region Drag vs Click

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _mouseDownPos = e.GetPosition(this);
        _isDragging = false;
        _bubbleWindow?.Close();
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && !_isDragging)
        {
            var pos = e.GetPosition(this);
            if (Math.Abs(pos.X - _mouseDownPos.X) > 3 || Math.Abs(pos.Y - _mouseDownPos.Y) > 3)
            {
                _isDragging = true;
                DragMove();
                _settings.WindowLeft = Left;
                _settings.WindowTop = Top;
                SettingsManager.Save(_settings);
            }
        }
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging)
        {
            OnBallClicked();
        }
    }

    #endregion

    #region Context Menu

    private void ContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        _bubbleWindow?.Close();
        _notificationBubbleWindow?.SuppressTopmost(true);

        MenuShortMode.IsChecked = _settings.ShortMode;
        MenuProjectBubble.IsChecked = _settings.ProjectBubbleEnabled;
        MenuAutoStart.IsChecked = AutoStartManager.IsEnabled;
        MenuBindClaude.IsChecked = HookManager.IsClaudeCodeBound();

        BuildAlertToggleMenu();
        BuildBubbleMenu();
        BuildAlertTimeoutMenu();

        // When menu closes, restore saved sound if preview wasn't confirmed
        if (sender is ContextMenu cm)
        {
            cm.Closed -= ContextMenu_Closed;
            cm.Closed += ContextMenu_Closed;
        }
    }

    private void ContextMenu_Closed(object sender, RoutedEventArgs e)
    {
        _bubbleWindow?.SuppressTopmost(false);
        _notificationBubbleWindow?.SuppressTopmost(false);
        _sound.Stop();
    }

    private void BuildAlertToggleMenu()
    {
        MenuToggleAlert.Items.Clear();

        // AI发送通知时提醒
        var notifItem = new MenuItem
        {
            Header = "AI 发送通知时提醒",
            IsCheckable = true,
            IsChecked = _settings.NotificationAlertEnabled,
            Style = (Style)FindResource("LightMenuItem"),
            StaysOpenOnClick = true,
        };
        notifItem.Click += (_, _) =>
        {
            _settings.NotificationAlertEnabled = notifItem.IsChecked;
            if (notifItem.IsChecked)
            {
                _lastNotificationEnabled = true;
            }
            SaveSettings();
            RecalculateEnabledState();
        };
        MenuToggleAlert.Items.Add(notifItem);

        // AI回应完毕时提醒
        var stopItem = new MenuItem
        {
            Header = "AI 回应完毕时提醒",
            IsCheckable = true,
            IsChecked = _settings.StopAlertEnabled,
            Style = (Style)FindResource("LightMenuItem"),
            StaysOpenOnClick = true,
        };
        stopItem.Click += (_, _) =>
        {
            _settings.StopAlertEnabled = stopItem.IsChecked;
            if (stopItem.IsChecked)
            {
                _lastStopEnabled = true;
            }
            SaveSettings();
            RecalculateEnabledState();
        };
        MenuToggleAlert.Items.Add(stopItem);

        MenuToggleAlert.Items.Add(new Separator { Style = (Style)FindResource("LightSeparator") });

        // 全部关闭
        var closeAllItem = new MenuItem
        {
            Header = "全部关闭",
            IsCheckable = true,
            IsChecked = !_settings.StopAlertEnabled && !_settings.NotificationAlertEnabled,
            Style = (Style)FindResource("LightMenuItem"),
            StaysOpenOnClick = true,
        };
        closeAllItem.Click += (_, _) =>
        {
            // Save current state before closing
            if (_settings.StopAlertEnabled || _settings.NotificationAlertEnabled)
            {
                _lastStopEnabled = _settings.StopAlertEnabled;
                _lastNotificationEnabled = _settings.NotificationAlertEnabled;
            }
            _settings.StopAlertEnabled = false;
            _settings.NotificationAlertEnabled = false;
            SaveSettings();
            RecalculateEnabledState();

            // Update checkmarks in the menu
            notifItem.IsChecked = false;
            stopItem.IsChecked = false;
            closeAllItem.IsChecked = true;
        };
        MenuToggleAlert.Items.Add(closeAllItem);
    }

    private void MenuSoundSettings_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SoundSettingsWindow(_settings, _sound)
        {
            Owner = this,
        };
        if (dlg.ShowDialog() == true)
        {
            _settings.StopSoundId = dlg.ResultStopSoundId;
            _settings.StopCustomSoundPath = dlg.ResultStopCustomPath;
            _settings.NotificationSoundId = dlg.ResultNotificationSoundId;
            _settings.NotificationCustomSoundPath = dlg.ResultNotificationCustomPath;
            _settings.Volume = dlg.ResultVolume;
            _settings.GradualVolume = dlg.ResultGradualVolume;
            _sound.Volume = _settings.Volume;
            SaveSettings();
        }
        // Stop any preview sound when dialog closes
        _sound.Stop();
    }

    private void BuildBubbleMenu()
    {
        MenuBubble.Items.Clear();

        // Enable/disable toggle
        var toggleItem = new MenuItem
        {
            Header = "启用碎碎念",
            IsCheckable = true,
            IsChecked = _settings.BubbleEnabled,
            Style = (Style)FindResource("LightMenuItem"),
        };
        toggleItem.Click += (_, _) =>
        {
            _settings.BubbleEnabled = toggleItem.IsChecked;
            SaveSettings();
        };
        MenuBubble.Items.Add(toggleItem);

        // Cooldown submenu
        var cooldownItem = new MenuItem
        {
            Header = "冷却时间",
            Style = (Style)FindResource("LightSubmenuItem"),
        };
        // Placeholder for submenu
        cooldownItem.Items.Add(new MenuItem { Header = "_", Visibility = Visibility.Collapsed });

        var cooldownOptions = new (string Label, int Minutes)[]
        {
            ("无冷却", 0),
            ("5 分钟", 5),
            ("10 分钟", 10),
            ("15 分钟", 15),
            ("30 分钟", 30),
        };

        cooldownItem.SubmenuOpened += (_, _) =>
        {
            cooldownItem.Items.Clear();
            foreach (var (label, minutes) in cooldownOptions)
            {
                var opt = new MenuItem
                {
                    Header = label,
                    IsCheckable = true,
                    IsChecked = _settings.BubbleCooldownMinutes == minutes,
                    Style = (Style)FindResource("LightMenuItem"),
                    StaysOpenOnClick = true,
                };
                var m = minutes;
                opt.Click += (_, _) =>
                {
                    _settings.BubbleCooldownMinutes = m;
                    SaveSettings();
                    // Update checkmarks
                    foreach (var child in cooldownItem.Items)
                    {
                        if (child is MenuItem mi)
                            mi.IsChecked = false;
                    }
                    opt.IsChecked = true;
                };
                cooldownItem.Items.Add(opt);
            }
        };
        MenuBubble.Items.Add(cooldownItem);

        MenuBubble.Items.Add(new Separator { Style = (Style)FindResource("LightSeparator") });

        // Edit messages
        var editItem = new MenuItem
        {
            Header = "编辑碎碎念内容…",
            Style = (Style)FindResource("LightMenuItem"),
        };
        editItem.Click += (_, _) =>
        {
            var messages = _settings.CustomBubbleMessages is { Count: > 0 }
                ? _settings.CustomBubbleMessages
                : DefaultBubbleMessages.ToList();

            var editor = new MessageEditorWindow(messages, DefaultBubbleMessages)
            {
                Owner = this,
            };
            if (editor.ShowDialog() == true)
            {
                _settings.CustomBubbleMessages = editor.ResultMessages;
                SaveSettings();
            }
        };
        MenuBubble.Items.Add(editItem);
    }

    private void MenuShortMode_Click(object sender, RoutedEventArgs e)
    {
        _settings.ShortMode = MenuShortMode.IsChecked;
        SaveSettings();
    }

    private void ApplyAlertTimeout()
    {
        _timeoutTimer.Interval = TimeSpan.FromSeconds(
            Math.Clamp(_settings.AlertTimeoutSeconds, 15, 60));
    }

    private void BuildAlertTimeoutMenu()
    {
        MenuAlertTimeout.Items.Clear();

        var options = new (string Label, int Seconds)[]
        {
            ("15 秒", 15),
            ("30 秒", 30),
            ("45 秒", 45),
            ("60 秒", 60),
        };

        foreach (var (label, seconds) in options)
        {
            var opt = new MenuItem
            {
                Header = label,
                IsCheckable = true,
                IsChecked = _settings.AlertTimeoutSeconds == seconds,
                Style = (Style)FindResource("LightMenuItem"),
                StaysOpenOnClick = true,
            };
            var s = seconds;
            opt.Click += (_, _) =>
            {
                _settings.AlertTimeoutSeconds = s;
                SaveSettings();
                ApplyAlertTimeout();
                foreach (var child in MenuAlertTimeout.Items)
                {
                    if (child is MenuItem mi)
                        mi.IsChecked = false;
                }
                opt.IsChecked = true;
            };
            MenuAlertTimeout.Items.Add(opt);
        }
    }

    private void MenuProjectBubble_Click(object sender, RoutedEventArgs e)
    {
        _settings.ProjectBubbleEnabled = MenuProjectBubble.IsChecked;
        SaveSettings();
        HookManager.RebindIfNeeded(_settings.ProjectBubbleEnabled);
    }

    private void MenuBindClaude_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (MenuBindClaude.IsChecked)
                HookManager.BindClaudeCode(_settings.ProjectBubbleEnabled);
            else
                HookManager.UnbindClaudeCode();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "AI Notifier", MessageBoxButton.OK, MessageBoxImage.Warning);
            MenuBindClaude.IsChecked = !MenuBindClaude.IsChecked;
        }
    }

    private void MenuAutoStart_Click(object sender, RoutedEventArgs e)
    {
        if (MenuAutoStart.IsChecked)
            AutoStartManager.Enable();
        else
            AutoStartManager.Disable();
    }

    private void MenuExit_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    #endregion

    private void SaveSettings()
    {
        SettingsManager.Save(_settings);
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _bubbleWindow?.Close();
        _notificationBubbleWindow?.Close();
        _server.Dispose();
        _sound.Dispose();
        _activityDetector.Stop();
        _timeoutTimer.Stop();
        _shortTimer.Stop();
        _volumeRampTimer?.Stop();

        try
        {
            _eyeBlinkStory?.Stop(this);
            _antennaGlowStory?.Stop(this);
            _wiggleStory?.Stop(this);
            _rippleStory?.Stop(this);
            _antennaFlashStory?.Stop(this);
        }
        catch { }
    }
}
