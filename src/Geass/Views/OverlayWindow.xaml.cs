using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Geass.ViewModels;

namespace Geass.Views;

public partial class OverlayWindow : Window
{
    private readonly List<Storyboard> _barAnims = [];
    private Storyboard? _recDotAnim;
    private Storyboard? _spinAnim;

    public OverlayViewModel ViewModel => (OverlayViewModel)DataContext;

    public Action? OnStopRequested { get; set; }
    public Action? OnCancelRequested { get; set; }

    public OverlayWindow()
    {
        InitializeComponent();
        DataContext = new OverlayViewModel();
        PositionBottomCenter();
    }

    private void PositionBottomCenter()
    {
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;
        var taskbarHeight = screenHeight - SystemParameters.WorkArea.Height;

        Left = (screenWidth - Width) / 2;
        Top = screenHeight - taskbarHeight - Height - 48;
    }

    public void ShowRecording()
    {
        ViewModel.IsRecording = true;
        ViewModel.IsProcessing = false;
        ViewModel.StatusText = "Listening...";

        Opacity = 0;
        RootBorder.RenderTransform = new TranslateTransform(0, 24);
        Show();

        // Slide up + fade in
        var duration = TimeSpan.FromSeconds(0.35);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var slideUp = new DoubleAnimation(24, 0, duration) { EasingFunction = ease };
        ((TranslateTransform)RootBorder.RenderTransform).BeginAnimation(TranslateTransform.YProperty, slideUp);

        var fadeIn = new DoubleAnimation(0, 1, duration) { EasingFunction = ease };
        BeginAnimation(OpacityProperty, fadeIn);

        // Waveform bar animations
        StartWaveformAnimation();
        StartRecDotAnimation();
    }

    public void ShowProcessing()
    {
        ViewModel.IsRecording = false;
        ViewModel.IsProcessing = true;
        ViewModel.StatusText = "Processing...";

        StopWaveformAnimation();
        _recDotAnim?.Stop();
        StartSpinAnimation();
    }

    public void HideWithAnimation(Action? onComplete = null)
    {
        StopWaveformAnimation();
        _recDotAnim?.Stop();
        _spinAnim?.Stop();

        var duration = TimeSpan.FromSeconds(0.25);
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

        var fadeOut = new DoubleAnimation(1, 0, duration) { EasingFunction = ease };
        var slideDown = new DoubleAnimation(0, 12, duration) { EasingFunction = ease };

        fadeOut.Completed += (_, _) =>
        {
            Hide();
            ViewModel.IsRecording = false;
            ViewModel.IsProcessing = false;
            onComplete?.Invoke();
        };

        ((TranslateTransform)RootBorder.RenderTransform).BeginAnimation(TranslateTransform.YProperty, slideDown);
        BeginAnimation(OpacityProperty, fadeOut);
    }

    private void StartWaveformAnimation()
    {
        var bars = new FrameworkElement[] { Bar1, Bar2, Bar3, Bar4, Bar5, Bar6 };
        var heights = new double[] { 8, 14, 20, 12, 18, 10 };
        var random = new Random(42);

        foreach (var (bar, i) in bars.Select((b, i) => (b, i)))
        {
            var anim = new DoubleAnimationUsingKeyFrames
            {
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromMilliseconds(random.Next(0, 200))
            };

            var baseH = heights[i];
            var low = Math.Max(4, baseH * 0.3);

            anim.KeyFrames.Add(new EasingDoubleKeyFrame(baseH,
                KeyTime.FromTimeSpan(TimeSpan.Zero),
                new SineEase { EasingMode = EasingMode.EaseInOut }));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(low,
                KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.3)),
                new SineEase { EasingMode = EasingMode.EaseInOut }));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(baseH * 1.2,
                KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.6)),
                new SineEase { EasingMode = EasingMode.EaseInOut }));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(baseH * 0.5,
                KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.9)),
                new SineEase { EasingMode = EasingMode.EaseInOut }));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(baseH,
                KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.2)),
                new SineEase { EasingMode = EasingMode.EaseInOut }));

            Storyboard.SetTarget(anim, bar);
            Storyboard.SetTargetProperty(anim, new PropertyPath(HeightProperty));

            var sb = new Storyboard();
            sb.Children.Add(anim);
            sb.Begin();
            _barAnims.Add(sb);
        }
    }

    private void StopWaveformAnimation()
    {
        foreach (var sb in _barAnims) sb.Stop();
        _barAnims.Clear();
    }

    private void StartRecDotAnimation()
    {
        var anim = new DoubleAnimation(1, 0.2, TimeSpan.FromSeconds(0.8))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };

        Storyboard.SetTarget(anim, RecDot);
        Storyboard.SetTargetProperty(anim, new PropertyPath(OpacityProperty));

        _recDotAnim = new Storyboard();
        _recDotAnim.Children.Add(anim);
        _recDotAnim.Begin();
    }

    private void StartSpinAnimation()
    {
        var anim = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.8))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };

        Storyboard.SetTarget(anim, Spinner);
        Storyboard.SetTargetProperty(anim,
            new PropertyPath("(UIElement.RenderTransform).(RotateTransform.Angle)"));

        _spinAnim = new Storyboard();
        _spinAnim.Children.Add(anim);
        _spinAnim.Begin();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        switch (e.Key)
        {
            case Key.Escape:
                OnCancelRequested?.Invoke();
                e.Handled = true;
                break;
            case Key.Enter:
                OnStopRequested?.Invoke();
                e.Handled = true;
                break;
        }
    }
}
