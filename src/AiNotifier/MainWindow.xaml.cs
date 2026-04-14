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
    private WinForms.ToolStripMenuItem? _trayShowItem;
    private WinForms.ToolStripMenuItem? _trayExitItem;

    // Storyboards
    private Storyboard? _eyeBlinkStory;
    private Storyboard? _antennaGlowStory;
    private Storyboard? _wiggleStory;
    private Storyboard? _rippleStory;
    private Storyboard? _antennaFlashStory;

    // Nudge (碎碎念)
    private NudgeWindow? _nudgeWindow;
    private DateTime _lastNudgeTime = DateTime.MinValue;
    private readonly Random _nudgeRandom = new();
    private int _nudgeMessageIndex;
    private DispatcherTimer? _nudgeCooldownTimer;

    // Project notification bubble
    private NotificationBubbleWindow? _notificationNudgeWindow;

    private static LocalizationService L => LocalizationService.Instance;

    private string[] DefaultNudgeMessages =>
    [
        L.Get("DefaultNudge_DrinkWater"),
        L.Get("DefaultNudge_TakeWalk"),
        L.Get("DefaultNudge_LookAway"),
    ];

    // Drag vs click tracking
    private System.Windows.Point _mouseDownPos;
    private bool _isDragging;


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
        _server.StopRequested += (cwd, isDup) => { StartAlert(AlertType.Stop); if (!isDup) ShowProjectBubble(cwd, AlertType.Stop); };
        _server.NotifyRequested += (cwd, isDup) => { StartAlert(AlertType.Notification); if (!isDup) ShowProjectBubble(cwd, AlertType.Notification); };
        _server.NudgeRequested += ShowNudge;

        _timeoutTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Clamp(_settings.AlertTimeoutSeconds, 15, 60))
        };
        _timeoutTimer.Tick += (_, _) => OnTimerStop();

        _shortTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _shortTimer.Tick += (_, _) => OnTimerStop();

        // Determine initial state from master switch
        if (!_settings.MasterEnabled)
            _state = AppState.Disabled;

        UpdateNudgeTimer();
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

        _trayIcon.Text = L.Get("Tray_Listening");
        _trayIcon.Visible = true;

        _trayIcon.DoubleClick += (_, _) =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        };

        var menu = new WinForms.ContextMenuStrip();

        _trayShowItem = new WinForms.ToolStripMenuItem(L.Get("Tray_ShowBall"));
        _trayShowItem.Click += (_, _) =>
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
        menu.Items.Add(_trayShowItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());

        _trayExitItem = new WinForms.ToolStripMenuItem(L.Get("Tray_Exit"));
        _trayExitItem.Click += (_, _) => Dispatcher.Invoke(() => Application.Current.Shutdown());
        menu.Items.Add(_trayExitItem);

        _trayIcon.ContextMenuStrip = menu;
    }

    private void UpdateTrayTooltip()
    {
        if (_trayIcon == null) return;
        _trayIcon.Text = _state switch
        {
            AppState.Enabled => L.Get("Tray_Listening"),
            AppState.Disabled => L.Get("Tray_Disabled"),
            AppState.RingingStop => L.Get("Tray_StopAlert"),
            AppState.RingingNotification => L.Get("Tray_NotificationAlert"),
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
        RefreshStaticUI();

        try
        {
            _server.Start();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(L.Get("Error_HttpServer", ex.Message),
                "AI Notifier", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // Force recreate render surface after sleep/wake or display changes —
        // these are the usual triggers for UCEERR_RENDERTHREADFAILURE, so we
        // preemptively reset instead of waiting for the exception.
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    private void OnPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode == Microsoft.Win32.PowerModes.Resume)
            Dispatcher.BeginInvoke(new Action(RecreateRenderSurface), DispatcherPriority.Background);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(RecreateRenderSurface), DispatcherPriority.Background);
    }

    private void RecreateRenderSurface()
    {
        try
        {
            if (!IsLoaded) return;
            Hide();
            Show();
            EnsureOnScreen();
        }
        catch { }
    }

    private void EnsureOnScreen()
    {
        var workArea = SystemParameters.WorkArea;
        if (Left < workArea.Left - Width || Left > workArea.Right || Top < workArea.Top - Height || Top > workArea.Bottom)
        {
            Left = workArea.Right - Width - 20;
            Top = workArea.Height * 2 / 3;
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
                BallTooltip.Content = L.Get("Tooltip_Listening");
                try { _eyeBlinkStory?.Begin(this, true); _antennaGlowStory?.Begin(this, true); } catch { }
                break;

            case AppState.RingingStop:
                ApplyRingingVisuals(
                    StopRingingInner, StopRingingOuter, ShadowStopRinging,
                    DotStopRinging1, DotStopRinging2,
                    AlertFaceStop, AlertAntennaStop, StopRingingInner,
                    L.Get("Tooltip_StopAlert"));
                break;

            case AppState.RingingNotification:
                ApplyRingingVisuals(
                    NotificationRingingInner, NotificationRingingOuter, ShadowNotificationRinging,
                    DotNotificationRinging1, DotNotificationRinging2,
                    AlertFaceNotification, AlertAntennaNotification, NotificationRingingInner,
                    L.Get("Tooltip_NotificationAlert"));
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
                BallTooltip.Content = L.Get("Tooltip_Disabled");
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
        if (!_settings.MasterEnabled) return;
        // Check if this specific alert type is enabled
        if (type == AlertType.Stop && !_settings.StopAlertEnabled) return;
        if (type == AlertType.Notification && !_settings.NotificationAlertEnabled) return;

        if (IsRinging)
            StopAlert();

        _pendingStopAfterPlay = false;

        // Select sound based on alert type
        var soundId = type == AlertType.Stop ? _settings.StopSoundId : _settings.NotificationSoundId;
        var customPath = type == AlertType.Stop ? _settings.StopCustomSoundPath : _settings.NotificationCustomSoundPath;
        _sound.SetSound(soundId, customPath);

        _state = type == AlertType.Stop ? AppState.RingingStop : AppState.RingingNotification;
        ApplyVisualState(_state);

        // Bind detector lifecycle to visual state: start immediately so we still
        // catch user activity even if media Open fails / never fires MediaOpened.
        _activityDetector.Start(UserActivityDetector.GetLastInputTick());

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
    }

    private void OnPlaybackStarted()
    {
        if (!IsRinging) return;
        // Refresh baseline so input that happened between StartAlert and MediaOpened
        // (a ~100ms window) isn't mistaken for a dismiss gesture.
        _activityDetector.Start(UserActivityDetector.GetLastInputTick());
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
            StopVisual();
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

        _settings.MasterEnabled = !_settings.MasterEnabled;
        RecalculateEnabledState();
        SaveSettings();
        UpdateNudgeTimer();
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

        if (_settings.MasterEnabled)
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

    #region Nudge

    private void ShowNudge()
    {
        if (!_settings.MasterEnabled || !_settings.NudgeEnabled)
            return;

        // 冷却到期模式下，忽略 HTTP 触发的碎碎念（由定时器驱动）
        if (_settings.NudgeTriggerMode == 1)
            return;

        // 日志气泡显示时不显示碎碎念
        if (_notificationNudgeWindow is { IsVisible: true })
            return;

        if (_settings.NudgeCooldownMinutes > 0 &&
            (DateTime.Now - _lastNudgeTime).TotalMinutes < _settings.NudgeCooldownMinutes)
            return;

        ShowNudgeCore();
    }

    private void ShowNudgeAuto()
    {
        if (!_settings.MasterEnabled || !_settings.NudgeEnabled)
            return;

        if (_notificationNudgeWindow is { IsVisible: true })
            return;

        ShowNudgeCore();
    }

    private void ShowNudgeCore()
    {
        var messages = _settings.CustomNudgeMessages is { Count: > 0 }
            ? _settings.CustomNudgeMessages
            : DefaultNudgeMessages.ToList();

        var message = PickNudgeMessage(messages);

        _nudgeWindow?.Close();
        _sound.PlayNudge(_settings.Volume);
        _nudgeWindow = new NudgeWindow(message, _settings.NudgeStaySeconds);
        _nudgeWindow.ShowNear(this);
        _lastNudgeTime = DateTime.Now;
    }

    private string PickNudgeMessage(List<string> messages)
    {
        if (messages.Count == 0) return "";

        if (_settings.NudgeOrderMode == 1)
        {
            if (_nudgeMessageIndex >= messages.Count)
                _nudgeMessageIndex = 0;
            return messages[_nudgeMessageIndex++];
        }

        return messages[_nudgeRandom.Next(messages.Count)];
    }

    private void UpdateNudgeTimer()
    {
        _nudgeCooldownTimer?.Stop();
        _nudgeCooldownTimer = null;

        if (_settings.NudgeTriggerMode != 1)
            return;
        if (_settings.NudgeCooldownMinutes <= 0)
            return;
        if (!_settings.MasterEnabled || !_settings.NudgeEnabled)
            return;

        _nudgeCooldownTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(_settings.NudgeCooldownMinutes)
        };
        _nudgeCooldownTimer.Tick += (_, _) => ShowNudgeAuto();
        _nudgeCooldownTimer.Start();
    }

    #endregion

    #region Project Notification Bubble

    private void ShowProjectBubble(string? cwd, AlertType type)
    {
        if (!_settings.MasterEnabled || !_settings.ProjectBubbleEnabled || string.IsNullOrWhiteSpace(cwd))
            return;

        // Don't show bubble if the corresponding alert type is disabled
        if (type == AlertType.Stop && !_settings.StopAlertEnabled) return;
        if (type == AlertType.Notification && !_settings.NotificationAlertEnabled) return;

        // 日志气泡出现时关闭碎碎念
        _nudgeWindow?.Close();

        var projectName = Path.GetFileName(cwd.TrimEnd('/', '\\'));
        if (string.IsNullOrEmpty(projectName))
            projectName = cwd;

        var message = type == AlertType.Stop
            ? L.Get("ProjectBubble_StopFormat", projectName)
            : L.Get("ProjectBubble_NotifyFormat", projectName);

        if (_notificationNudgeWindow is { IsVisible: true })
        {
            _notificationNudgeWindow.AddMessage(message);
        }
        else
        {
            _notificationNudgeWindow?.Close();
            _notificationNudgeWindow = new NotificationBubbleWindow();
            _notificationNudgeWindow.AddMessage(message);
            _notificationNudgeWindow.ShowNear(this);
            _notificationNudgeWindow.StartActivityWatch();
            _notificationNudgeWindow.Closed += (_, _) => _notificationNudgeWindow = null;
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
        _nudgeWindow?.Close();
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
        _nudgeWindow?.Close();
        _notificationNudgeWindow?.SuppressTopmost(true);

        // Master toggle
        MenuMasterToggle.Header = _settings.MasterEnabled ? L.Get("Menu_MasterClose") : L.Get("Menu_MasterOpen");

        // AI Alert section
        bool hasAlertTrigger = _settings.StopAlertEnabled || _settings.NotificationAlertEnabled;
        MenuProjectBubble.IsChecked = _settings.ProjectBubbleEnabled;
        MenuProjectBubble.IsEnabled = hasAlertTrigger;
        MenuShortMode.IsChecked = _settings.ShortMode;
        MenuAlertTimeout.Visibility = _settings.ShortMode ? Visibility.Collapsed : Visibility.Visible;

        // Nudge section
        MenuEnableNudge.IsChecked = _settings.NudgeEnabled;

        // Other
        MenuAutoStart.IsChecked = AutoStartManager.IsEnabled;
        MenuBindClaude.IsChecked = HookManager.IsClaudeCodeBound();

        // Build dynamic submenus
        BuildTriggerTimingMenu();
        BuildAlertTimeoutMenu();
        BuildNudgeStayMenu();
        BuildNudgeCooldownMenu();
        BuildNudgeTriggerMenu();
        BuildNudgeOrderMenu();
        BuildLanguageMenu();

        // When menu closes, restore saved sound if preview wasn't confirmed
        if (sender is ContextMenu cm)
        {
            cm.Closed -= ContextMenu_Closed;
            cm.Closed += ContextMenu_Closed;
        }
    }

    private void ContextMenu_Closed(object sender, RoutedEventArgs e)
    {
        _nudgeWindow?.SuppressTopmost(false);
        _notificationNudgeWindow?.SuppressTopmost(false);
        _sound.Stop();
    }

    private void MenuMasterToggle_Click(object sender, RoutedEventArgs e)
    {
        _settings.MasterEnabled = !_settings.MasterEnabled;
        if (!_settings.MasterEnabled && IsRinging) StopAlert();
        RecalculateEnabledState();
        SaveSettings();
        UpdateNudgeTimer();
    }

    private void BuildTriggerTimingMenu()
    {
        MenuTriggerTiming.Items.Clear();

        var notifItem = new MenuItem
        {
            Header = L.Get("Menu_AlertNotification"),
            IsCheckable = true,
            IsChecked = _settings.NotificationAlertEnabled,
            Style = (Style)FindResource("LightMenuItem"),
            StaysOpenOnClick = true,
        };
        notifItem.Click += (_, _) =>
        {
            _settings.NotificationAlertEnabled = notifItem.IsChecked;
            SaveSettings();
        };
        MenuTriggerTiming.Items.Add(notifItem);

        var stopItem = new MenuItem
        {
            Header = L.Get("Menu_AlertStop"),
            IsCheckable = true,
            IsChecked = _settings.StopAlertEnabled,
            Style = (Style)FindResource("LightMenuItem"),
            StaysOpenOnClick = true,
        };
        stopItem.Click += (_, _) =>
        {
            _settings.StopAlertEnabled = stopItem.IsChecked;
            SaveSettings();
        };
        MenuTriggerTiming.Items.Add(stopItem);
    }

    private void MenuEnableNudge_Click(object sender, RoutedEventArgs e)
    {
        _settings.NudgeEnabled = MenuEnableNudge.IsChecked;
        SaveSettings();
        UpdateNudgeTimer();
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
            SaveSettings();
        }
        _sound.Volume = _settings.Volume;
        _sound.Stop();
    }

    private void MenuVolume_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new VolumeWindow(_settings, _sound) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            _settings.Volume = dlg.ResultVolume;
            _settings.GradualVolume = dlg.ResultGradualVolume;
            _sound.Volume = _settings.Volume;
            SaveSettings();
        }
        _sound.Stop();
    }

    private void BuildNudgeStayMenu()
    {
        MenuNudgeStay.Items.Clear();
        var options = new (string Label, int Seconds)[]
        {
            (L.Get("Menu_SecondsFormat", 5), 5),
            (L.Get("Menu_SecondsFormat", 10), 10),
            (L.Get("Menu_SecondsFormat", 15), 15),
            (L.Get("Menu_SecondsFormat", 20), 20),
        };
        foreach (var (label, seconds) in options)
        {
            var opt = new MenuItem
            {
                Header = label,
                IsCheckable = true,
                IsChecked = _settings.NudgeStaySeconds == seconds,
                Style = (Style)FindResource("LightMenuItem"),
                StaysOpenOnClick = true,
            };
            var s = seconds;
            opt.Click += (_, _) =>
            {
                _settings.NudgeStaySeconds = s;
                SaveSettings();
                foreach (var child in MenuNudgeStay.Items)
                {
                    if (child is MenuItem mi) mi.IsChecked = false;
                }
                opt.IsChecked = true;
            };
            MenuNudgeStay.Items.Add(opt);
        }
    }

    private void BuildNudgeCooldownMenu()
    {
        MenuNudgeCooldown.Items.Clear();
        var options = new (string Label, int Minutes)[]
        {
            (L.Get("Menu_NoCooldown"), 0),
            (L.Get("Menu_MinutesFormat", 5), 5),
            (L.Get("Menu_MinutesFormat", 10), 10),
            (L.Get("Menu_MinutesFormat", 15), 15),
            (L.Get("Menu_MinutesFormat", 20), 20),
            (L.Get("Menu_MinutesFormat", 25), 25),
            (L.Get("Menu_MinutesFormat", 30), 30),
            (L.Get("Menu_MinutesFormat", 35), 35),
            (L.Get("Menu_MinutesFormat", 40), 40),
        };
        foreach (var (label, minutes) in options)
        {
            var opt = new MenuItem
            {
                Header = label,
                IsCheckable = true,
                IsChecked = _settings.NudgeCooldownMinutes == minutes,
                Style = (Style)FindResource("LightMenuItem"),
                StaysOpenOnClick = true,
            };
            var m = minutes;
            opt.Click += (_, _) =>
            {
                _settings.NudgeCooldownMinutes = m;
                SaveSettings();
                UpdateNudgeTimer();
                foreach (var child in MenuNudgeCooldown.Items)
                {
                    if (child is MenuItem mi) mi.IsChecked = false;
                }
                opt.IsChecked = true;
            };
            MenuNudgeCooldown.Items.Add(opt);
        }
    }

    private void BuildNudgeTriggerMenu()
    {
        MenuNudgeTrigger.Items.Clear();
        var options = new (string Label, int Mode)[]
        {
            (L.Get("Menu_TriggerOnRequest"), 0),
            (L.Get("Menu_TriggerOnCooldown"), 1),
        };
        foreach (var (label, mode) in options)
        {
            var opt = new MenuItem
            {
                Header = label,
                IsCheckable = true,
                IsChecked = _settings.NudgeTriggerMode == mode,
                Style = (Style)FindResource("LightMenuItem"),
                StaysOpenOnClick = true,
            };
            var m = mode;
            opt.Click += (_, _) =>
            {
                _settings.NudgeTriggerMode = m;
                SaveSettings();
                UpdateNudgeTimer();
                foreach (var child in MenuNudgeTrigger.Items)
                {
                    if (child is MenuItem mi) mi.IsChecked = false;
                }
                opt.IsChecked = true;
            };
            MenuNudgeTrigger.Items.Add(opt);
        }
    }

    private void BuildNudgeOrderMenu()
    {
        MenuNudgeOrder.Items.Clear();
        var options = new (string Label, int Mode)[]
        {
            (L.Get("Menu_OrderRandom"), 0),
            (L.Get("Menu_OrderSequential"), 1),
        };
        foreach (var (label, mode) in options)
        {
            var opt = new MenuItem
            {
                Header = label,
                IsCheckable = true,
                IsChecked = _settings.NudgeOrderMode == mode,
                Style = (Style)FindResource("LightMenuItem"),
                StaysOpenOnClick = true,
            };
            var m = mode;
            opt.Click += (_, _) =>
            {
                _settings.NudgeOrderMode = m;
                if (m == 0) _nudgeMessageIndex = 0;
                SaveSettings();
                foreach (var child in MenuNudgeOrder.Items)
                {
                    if (child is MenuItem mi) mi.IsChecked = false;
                }
                opt.IsChecked = true;
            };
            MenuNudgeOrder.Items.Add(opt);
        }
    }

    private void MenuNudgeEdit_Click(object sender, RoutedEventArgs e)
    {
        var messages = _settings.CustomNudgeMessages is { Count: > 0 }
            ? _settings.CustomNudgeMessages
            : DefaultNudgeMessages.ToList();

        var editor = new MessageEditorWindow(messages, DefaultNudgeMessages)
        {
            Owner = this,
        };
        if (editor.ShowDialog() == true)
        {
            _settings.CustomNudgeMessages = editor.ResultMessages;
            SaveSettings();
        }
    }

    private void MenuShortMode_Click(object sender, RoutedEventArgs e)
    {
        _settings.ShortMode = MenuShortMode.IsChecked;
        MenuAlertTimeout.Visibility = _settings.ShortMode ? Visibility.Collapsed : Visibility.Visible;
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
            (L.Get("Menu_SecondsFormat", 15), 15),
            (L.Get("Menu_SecondsFormat", 30), 30),
            (L.Get("Menu_SecondsFormat", 45), 45),
            (L.Get("Menu_SecondsFormat", 60), 60),
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

    private void BuildLanguageMenu()
    {
        MenuLanguage.Items.Clear();

        var zhItem = new MenuItem
        {
            Header = "中文",
            IsCheckable = true,
            IsChecked = L.IsChinese,
            Style = (Style)FindResource("LightMenuItem"),
        };
        zhItem.Click += (_, _) => SwitchLanguage("zh-CN");
        MenuLanguage.Items.Add(zhItem);

        var enItem = new MenuItem
        {
            Header = "English",
            IsCheckable = true,
            IsChecked = !L.IsChinese,
            Style = (Style)FindResource("LightMenuItem"),
        };
        enItem.Click += (_, _) => SwitchLanguage("en");
        MenuLanguage.Items.Add(enItem);
    }

    private void SwitchLanguage(string cultureCode)
    {
        L.SwitchLanguage(cultureCode);
        _settings.Language = cultureCode;
        SaveSettings();
        RefreshStaticUI();
    }

    private void RefreshStaticUI()
    {
        // Master toggle
        MenuMasterToggle.Header = _settings.MasterEnabled ? L.Get("Menu_MasterClose") : L.Get("Menu_MasterOpen");

        // AI Alert section
        SectionAiAlert.Header = L.Get("Menu_SectionAiAlert");
        MenuTriggerTiming.Header = L.Get("Menu_AlertTriggerTiming");
        MenuProjectBubble.Header = L.Get("Menu_NotificationLog");
        MenuShortMode.Header = L.Get("Menu_ShortMode");
        MenuAlertTimeout.Header = L.Get("Menu_LongAlertDuration");

        // Nudge section
        SectionNudge.Header = L.Get("Menu_SectionNudge");
        MenuEnableNudge.Header = L.Get("Menu_EnableNudge");
        MenuNudgeStay.Header = L.Get("Menu_NudgeStay");
        MenuNudgeCooldown.Header = L.Get("Menu_Cooldown");
        MenuNudgeTrigger.Header = L.Get("Menu_TriggerTiming");
        MenuNudgeOrder.Header = L.Get("Menu_MessageOrder");
        MenuNudgeEdit.Header = L.Get("Menu_EditNudgeContent");

        // Sound & other
        MenuSoundSettings.Header = L.Get("Menu_SoundSettings");
        MenuVolume.Header = L.Get("Menu_Volume");
        MenuBindClaude.Header = L.Get("Menu_BindClaude");
        MenuAutoStart.Header = L.Get("Menu_AutoStart");
        MenuLanguage.Header = L.Get("Menu_Language");
        MenuExit.Header = L.Get("Menu_Exit");

        // Update tooltip and tray
        ApplyVisualState(_state);

        // Update tray menu items
        if (_trayShowItem != null) _trayShowItem.Text = L.Get("Tray_ShowBall");
        if (_trayExitItem != null) _trayExitItem.Text = L.Get("Tray_Exit");
    }

    #endregion

    private void SaveSettings()
    {
        SettingsManager.Save(_settings);
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;

        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _nudgeWindow?.Close();
        _notificationNudgeWindow?.Close();
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
