using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace Geass.Views;

public partial class TranscriptionWindow : Window
{
    public enum ViewState { Recording, Processing, Streaming, Editing }

    private ViewState _visualState = ViewState.Recording;
    private readonly List<Storyboard> _barAnims = [];
    private Storyboard? _recDotAnim;
    private Storyboard? _spinAnim;
    private Storyboard? _dotPulse1, _dotPulse2, _dotPulse3;
    private bool _hasExpandedOnce;
    private Rect _targetWorkArea;

    public string TranscribedText
    {
        get => TranscriptionTextBox.Text;
        set => TranscriptionTextBox.Text = value;
    }

    public string OriginalText { get; set; } = "";

    public Action? OnConfirm { get; set; }
    public Action? OnCancel { get; set; }
    public Action? OnStopRecording { get; set; }

    public TranscriptionWindow()
    {
        InitializeComponent();
        SizeChanged += (_, _) => RepositionBottomCenter();
        TranscriptionTextBox.TextChanged += (_, _) => UpdateCharCount();
        CaptureTargetMonitor();
    }

    // ── Multi-monitor: capture the monitor of the foreground window ──

    private void CaptureTargetMonitor()
    {
        var fgHwnd = GetForegroundWindow();
        var hMonitor = MonitorFromWindow(fgHwnd, MONITOR_DEFAULTTONEAREST);

        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(hMonitor, ref mi);

        double scaleX = 1.0, scaleY = 1.0;
        try
        {
            if (GetDpiForMonitor(hMonitor, 0, out var dpiX, out var dpiY) == 0)
            {
                scaleX = dpiX / 96.0;
                scaleY = dpiY / 96.0;
            }
        }
        catch { /* shcore.dll unavailable on older systems */ }

        _targetWorkArea = new Rect(
            mi.rcWork.Left / scaleX,
            mi.rcWork.Top / scaleY,
            (mi.rcWork.Right - mi.rcWork.Left) / scaleX,
            (mi.rcWork.Bottom - mi.rcWork.Top) / scaleY);
    }

    // ── Positioning ──

    private void RepositionBottomCenter()
    {
        Left = _targetWorkArea.Left + (_targetWorkArea.Width - ActualWidth) / 2;
        Top = _targetWorkArea.Top + _targetWorkArea.Height - ActualHeight - 48;
    }

    // ── State: Recording ──

    public void ShowRecording()
    {
        _visualState = ViewState.Recording;

        WaveformCanvas.Visibility = Visibility.Visible;
        RecDot.Visibility = Visibility.Visible;
        Spinner.Visibility = Visibility.Collapsed;
        StreamDotsPanel.Visibility = Visibility.Collapsed;
        StatusText.Text = "Listening...";
        CompactPanel.Visibility = Visibility.Visible;
        ExpandedPanel.Visibility = Visibility.Collapsed;

        Opacity = 0;
        RootBorder.RenderTransform = new TranslateTransform(0, 24);
        Show();

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var slideUp = new DoubleAnimation(24, 0, Dur(0.35)) { EasingFunction = ease };
        ((TranslateTransform)RootBorder.RenderTransform).BeginAnimation(TranslateTransform.YProperty, slideUp);
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Dur(0.35)) { EasingFunction = ease });

        StartWaveformAnimation();
        StartRecDotAnimation();
        RepositionBottomCenter();

        // Steal focus so Enter key works during recording
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
        {
            ForceForeground();
            Focus();
        });
    }

    // ── State: Processing ──

    public void ShowProcessing()
    {
        _visualState = ViewState.Processing;

        StopWaveformAnimation();
        _recDotAnim?.Stop();

        WaveformCanvas.Visibility = Visibility.Collapsed;
        RecDot.Visibility = Visibility.Collapsed;
        Spinner.Visibility = Visibility.Visible;
        StreamDotsPanel.Visibility = Visibility.Collapsed;
        StatusText.Text = "Processing...";

        StartSpinAnimation();
    }

    // ── State: Streaming ──

    public void ShowStreaming()
    {
        _visualState = ViewState.Streaming;

        _spinAnim?.Stop();
        Spinner.Visibility = Visibility.Collapsed;
        WaveformCanvas.Visibility = Visibility.Collapsed;
        RecDot.Visibility = Visibility.Collapsed;
        StreamDotsPanel.Visibility = Visibility.Visible;
        StatusText.Text = "Transcribing...";

        StartStreamDotAnimation();
    }

    public void AppendStreamingText(string chunk)
    {
        // Strip newlines from streaming output (Japanese doesn't need spaces at join points)
        chunk = chunk.Replace("\r\n", "").Replace("\n", "").Replace("\r", "");

        Dispatcher.Invoke(() =>
        {
            TranscriptionTextBox.Text += chunk;
            TranscriptionTextBox.CaretIndex = TranscriptionTextBox.Text.Length;
            TranscriptionTextBox.ScrollToEnd();

            if (!_hasExpandedOnce && TranscriptionTextBox.Text.Length > 0)
            {
                _hasExpandedOnce = true;
                ExpandToSubtitleMode();
            }
        });
    }

    // ── State: Editing ──

    public void ShowEditing()
    {
        Dispatcher.Invoke(() =>
        {
            _visualState = ViewState.Editing;

            StopStreamDotAnimation();
            StreamDotsPanel.Visibility = Visibility.Collapsed;
            CompactPanel.Visibility = Visibility.Collapsed;

            if (!_hasExpandedOnce)
            {
                _hasExpandedOnce = true;
                ExpandToSubtitleMode();
            }

            OriginalText = TranscriptionTextBox.Text;
            TranscriptionTextBox.IsReadOnly = false;

            ForceForeground();
            TranscriptionTextBox.Focus();
            TranscriptionTextBox.CaretIndex = TranscriptionTextBox.Text.Length;
            UpdateCharCount();

            // Retry focus after layout settles
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
            {
                if (_visualState == ViewState.Editing)
                {
                    ForceForeground();
                    TranscriptionTextBox.Focus();
                }
            });
        });
    }

    // ── Expand animation: compact pill → subtitle card ──

    private void ExpandToSubtitleMode()
    {
        ExpandedPanel.Visibility = Visibility.Visible;
        ExpandedPanel.Opacity = 0;

        // Widen
        var widthAnim = new DoubleAnimation(300, 520, Dur(0.4))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(WidthProperty, widthAnim);

        // Slightly increase background opacity for readability
        var colorAnim = new ColorAnimation(
            Color.FromArgb(0xE6, 0x18, 0x18, 0x20),
            Color.FromArgb(0xF2, 0x18, 0x18, 0x20),
            Dur(0.4))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BgBrush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnim);

        // Fade in expanded panel
        var fadeIn = new DoubleAnimation(0, 1, Dur(0.3))
        {
            BeginTime = TimeSpan.FromSeconds(0.15),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        ExpandedPanel.BeginAnimation(OpacityProperty, fadeIn);

        // Increase shadow
        if (RootBorder.Effect is DropShadowEffect shadow)
        {
            shadow.BeginAnimation(DropShadowEffect.BlurRadiusProperty,
                new DoubleAnimation(20, 28, Dur(0.4)));
        }

        Activate();
    }

    // ── Hide ──

    public void HideWithAnimation(Action? onComplete = null)
    {
        StopAllAnimations();

        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var fadeOut = new DoubleAnimation(1, 0, Dur(0.2)) { EasingFunction = ease };
        fadeOut.Completed += (_, _) =>
        {
            Hide();
            onComplete?.Invoke();
        };
        BeginAnimation(OpacityProperty, fadeOut);
    }

    // ── Focus stealing ──

    private void ForceForeground()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        var fgHwnd = GetForegroundWindow();
        if (fgHwnd == hwnd)
        {
            Activate();
            return;
        }

        var fgThread = GetWindowThreadProcessId(fgHwnd, IntPtr.Zero);
        var currentThread = GetCurrentThreadId();

        if (fgThread != currentThread)
        {
            AttachThreadInput(currentThread, fgThread, true);
            SetForegroundWindow(hwnd);
            AttachThreadInput(currentThread, fgThread, false);
        }
        else
        {
            SetForegroundWindow(hwnd);
        }

        Activate();
    }

    // ── Keyboard ──

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        switch (e.Key)
        {
            case Key.Escape:
                OnCancel?.Invoke();
                e.Handled = true;
                break;

            case Key.Enter when _visualState == ViewState.Recording:
                OnStopRecording?.Invoke();
                e.Handled = true;
                break;

            case Key.Enter when _visualState == ViewState.Editing
                           && Keyboard.Modifiers != ModifierKeys.Shift:
                OnConfirm?.Invoke();
                e.Handled = true;
                break;
        }
    }

    // ── Waveform Animation ──

    private void StartWaveformAnimation()
    {
        var bars = new FrameworkElement[] { Bar1, Bar2, Bar3, Bar4, Bar5, Bar6 };
        double[] heights = [8, 14, 20, 12, 18, 10];
        var rng = new Random(42);

        for (var i = 0; i < bars.Length; i++)
        {
            var baseH = heights[i];
            var low = Math.Max(4, baseH * 0.3);

            var anim = new DoubleAnimationUsingKeyFrames
            {
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromMilliseconds(rng.Next(0, 200))
            };
            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };

            anim.KeyFrames.Add(new EasingDoubleKeyFrame(baseH, KT(0), ease));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(low, KT(0.3), ease));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(baseH * 1.2, KT(0.6), ease));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(baseH * 0.5, KT(0.9), ease));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(baseH, KT(1.2), ease));

            Storyboard.SetTarget(anim, bars[i]);
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

    // ── Rec Dot Animation ──

    private void StartRecDotAnimation()
    {
        var anim = new DoubleAnimation(1, 0.15, Dur(0.8))
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

    // ── Spinner Animation ──

    private void StartSpinAnimation()
    {
        var anim = new DoubleAnimation(0, 360, Dur(0.8))
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

    // ── Streaming Dot Animation ──

    private void StartStreamDotAnimation()
    {
        _dotPulse1 = CreateDotPulse(SDot1, TimeSpan.Zero);
        _dotPulse2 = CreateDotPulse(SDot2, TimeSpan.FromMilliseconds(200));
        _dotPulse3 = CreateDotPulse(SDot3, TimeSpan.FromMilliseconds(400));
        _dotPulse1.Begin();
        _dotPulse2.Begin();
        _dotPulse3.Begin();
    }

    private void StopStreamDotAnimation()
    {
        _dotPulse1?.Stop();
        _dotPulse2?.Stop();
        _dotPulse3?.Stop();
    }

    private static Storyboard CreateDotPulse(System.Windows.Shapes.Ellipse target, TimeSpan delay)
    {
        var anim = new DoubleAnimationUsingKeyFrames
        {
            RepeatBehavior = RepeatBehavior.Forever,
            BeginTime = delay
        };
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(0.3, KT(0)));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KT(0.4)));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(0.3, KT(0.8)));

        Storyboard.SetTarget(anim, target);
        Storyboard.SetTargetProperty(anim, new PropertyPath(OpacityProperty));

        var sb = new Storyboard();
        sb.Children.Add(anim);
        return sb;
    }

    // ── Cleanup ──

    private void StopAllAnimations()
    {
        StopWaveformAnimation();
        _recDotAnim?.Stop();
        _spinAnim?.Stop();
        StopStreamDotAnimation();
    }

    private void UpdateCharCount()
    {
        var len = TranscriptionTextBox.Text.Length;
        CharCount.Text = len > 0 ? $"{len}" : "";
    }

    // ── Helpers ──

    private static Duration Dur(double seconds) => new(TimeSpan.FromSeconds(seconds));
    private static KeyTime KT(double seconds) => KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds));

    // ── P/Invoke ──

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
}
