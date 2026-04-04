using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace AiNotifier;

public partial class NudgeWindow : Window
{
    private readonly DispatcherTimer _stayTimer;
    private readonly DispatcherTimer _topTimer;
    private Window? _owner;
    private bool _suppressTop;

    public NudgeWindow(string message, int staySeconds = 10)
    {
        InitializeComponent();
        MessageText.Text = message;

        _stayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Clamp(staySeconds, 5, 20)) };
        _stayTimer.Tick += (_, _) =>
        {
            _stayTimer.Stop();
            FadeOut();
        };

        // Periodically re-assert Z-order so nudge stays above the ball
        _topTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _topTimer.Tick += (_, _) =>
        {
            if (IsVisible && !_suppressTop)
            {
                Topmost = false;
                Topmost = true;
            }
        };

        Loaded += (_, _) => PositionNearOwner();
    }

    public void ShowNear(Window owner)
    {
        _owner = owner;
        _owner.LocationChanged += Owner_LocationChanged;
        Closed += (_, _) =>
        {
            _topTimer.Stop();
            if (_owner != null)
                _owner.LocationChanged -= Owner_LocationChanged;
        };
        Show();
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

        // Centered below the ball, slightly overlapping
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

    private void FadeIn()
    {
        var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        anim.Completed += (_, _) => _stayTimer.Start();
        BeginAnimation(OpacityProperty, anim);
    }

    private void FadeOut()
    {
        var anim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(500))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        anim.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, anim);
    }
}
