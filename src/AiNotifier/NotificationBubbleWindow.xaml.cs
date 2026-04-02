using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace AiNotifier;

public partial class NotificationBubbleWindow : Window
{
    private readonly List<string> _messages = new();
    private readonly DispatcherTimer _topTimer;
    private readonly DispatcherTimer _activityPollTimer;
    private readonly DispatcherTimer _dismissTimer;
    private Window? _owner;
    private bool _suppressTop;
    private bool _isFadingOut;
    private uint _baselineTick;

    public NotificationBubbleWindow()
    {
        InitializeComponent();

        _topTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _topTimer.Tick += (_, _) =>
        {
            if (IsVisible && !_suppressTop)
            {
                Topmost = false;
                Topmost = true;
            }
        };

        _activityPollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _activityPollTimer.Tick += OnActivityPollTick;

        _dismissTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _dismissTimer.Tick += (_, _) =>
        {
            _dismissTimer.Stop();
            _activityPollTimer.Stop();
            FadeOut();
        };

        Loaded += (_, _) => PositionNearOwner();
    }

    public void AddMessage(string message)
    {
        _messages.Add(message);
        MessageText.Text = string.Join("\n", _messages);

        // If fading out, cancel and re-show
        if (_isFadingOut)
        {
            _isFadingOut = false;
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
        }

        // Restart dismiss timer if it was running (new message resets countdown)
        if (_dismissTimer.IsEnabled)
        {
            _dismissTimer.Stop();
            _dismissTimer.Start();
        }

        // Reset activity baseline so we detect *new* input after this message
        _baselineTick = UserActivityDetector.GetLastInputTick();

        // Reposition in case height changed
        UpdatePosition();
    }

    public void ShowNear(Window owner)
    {
        _owner = owner;
        _owner.LocationChanged += Owner_LocationChanged;
        Closed += (_, _) =>
        {
            _topTimer.Stop();
            _activityPollTimer.Stop();
            _dismissTimer.Stop();
            if (_owner != null)
                _owner.LocationChanged -= Owner_LocationChanged;
        };
        Show();
    }

    public void StartActivityWatch()
    {
        _baselineTick = UserActivityDetector.GetLastInputTick();
        _activityPollTimer.Start();
    }

    private void OnActivityPollTick(object? sender, EventArgs e)
    {
        var currentTick = UserActivityDetector.GetLastInputTick();
        if (currentTick != _baselineTick)
        {
            // User became active — start 5s countdown
            _activityPollTimer.Stop();
            _dismissTimer.Stop();
            _dismissTimer.Start();
        }
    }

    private void Owner_LocationChanged(object? sender, EventArgs e)
    {
        UpdatePosition();
    }

    private void PositionNearOwner()
    {
        UpdatePosition();
        FadeIn();
        _topTimer.Start();
    }

    private void UpdatePosition()
    {
        if (_owner == null) return;

        var bubbleWidth = ActualWidth;
        var bubbleHeight = ActualHeight;

        var ballCenterX = _owner.Left + _owner.Width / 2;
        var ballBottom = _owner.Top + _owner.Height;

        var workArea = SystemParameters.WorkArea;

        var targetLeft = ballCenterX - bubbleWidth / 2;
        var targetTop = ballBottom - 28;

        // Clamp horizontal
        if (targetLeft < workArea.Left)
            targetLeft = workArea.Left + 4;
        if (targetLeft + bubbleWidth > workArea.Right)
            targetLeft = workArea.Right - bubbleWidth - 4;

        // Clamp vertical
        if (targetTop + bubbleHeight > workArea.Bottom)
            targetTop = _owner.Top - bubbleHeight - 4;

        Left = targetLeft;
        Top = targetTop;
    }

    public void SuppressTopmost(bool suppress)
    {
        _suppressTop = suppress;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dismissTimer.Stop();
        _activityPollTimer.Stop();
        FadeOut();
    }

    private void FadeIn()
    {
        _isFadingOut = false;
        var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(OpacityProperty, anim);
    }

    private void FadeOut()
    {
        if (_isFadingOut) return;
        _isFadingOut = true;
        var anim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(500))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        anim.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, anim);
    }
}
