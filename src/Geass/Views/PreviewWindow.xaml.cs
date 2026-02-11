using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Geass.ViewModels;

namespace Geass.Views;

public partial class PreviewWindow : Window
{
    private Storyboard? _dotPulse1;
    private Storyboard? _dotPulse2;
    private Storyboard? _dotPulse3;

    public PreviewViewModel ViewModel => (PreviewViewModel)DataContext;

    public PreviewWindow(PreviewViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PlayEntryAnimation();
        StartStreamingDotAnimation();
        TranscriptionTextBox.Focus();
        TranscriptionTextBox.TextChanged += (_, _) => UpdateCharCount();
    }

    private void PlayEntryAnimation()
    {
        var duration = TimeSpan.FromSeconds(0.4);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var fadeIn = new DoubleAnimation(0, 1, duration) { EasingFunction = ease };

        var transform = (TransformGroup)RootBorder.RenderTransform;
        var scale = (ScaleTransform)transform.Children[0];
        var translate = (TranslateTransform)transform.Children[1];

        var scaleX = new DoubleAnimation(0.92, 1.0, duration) { EasingFunction = ease };
        var scaleY = new DoubleAnimation(0.92, 1.0, duration) { EasingFunction = ease };
        var slideUp = new DoubleAnimation(16, 0, duration) { EasingFunction = ease };

        RootBorder.BeginAnimation(OpacityProperty, fadeIn);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
        translate.BeginAnimation(TranslateTransform.YProperty, slideUp);
    }

    private void StartStreamingDotAnimation()
    {
        _dotPulse1 = CreateDotPulse(SDot1, TimeSpan.Zero);
        _dotPulse2 = CreateDotPulse(SDot2, TimeSpan.FromMilliseconds(200));
        _dotPulse3 = CreateDotPulse(SDot3, TimeSpan.FromMilliseconds(400));
        _dotPulse1.Begin();
        _dotPulse2.Begin();
        _dotPulse3.Begin();
    }

    private static Storyboard CreateDotPulse(System.Windows.Shapes.Ellipse target, TimeSpan delay)
    {
        var anim = new DoubleAnimationUsingKeyFrames
        {
            RepeatBehavior = RepeatBehavior.Forever,
            BeginTime = delay
        };
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(0.3, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.4))));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(0.3, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.8))));

        Storyboard.SetTarget(anim, target);
        Storyboard.SetTargetProperty(anim, new PropertyPath(OpacityProperty));

        var sb = new Storyboard();
        sb.Children.Add(anim);
        return sb;
    }

    public void AppendStreamingText(string chunk)
    {
        Dispatcher.Invoke(() =>
        {
            ViewModel.AppendText(chunk);
            TranscriptionTextBox.CaretIndex = TranscriptionTextBox.Text.Length;
            TranscriptionTextBox.ScrollToEnd();
        });
    }

    public void StreamingComplete()
    {
        Dispatcher.Invoke(() =>
        {
            ViewModel.IsStreaming = false;
            ViewModel.OriginalText = ViewModel.TranscribedText;

            _dotPulse1?.Stop();
            _dotPulse2?.Stop();
            _dotPulse3?.Stop();

            TranscriptionTextBox.Focus();
            TranscriptionTextBox.SelectAll();
            UpdateCharCount();
        });
    }

    private void UpdateCharCount()
    {
        var len = TranscriptionTextBox.Text.Length;
        CharCount.Text = len > 0 ? $"{len} chars" : "";
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        switch (e.Key)
        {
            case Key.Escape:
                ViewModel.CancelCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Enter when Keyboard.Modifiers == ModifierKeys.Control:
                ViewModel.ConfirmCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }
}
