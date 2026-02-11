using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Geass.ViewModels;

namespace Geass.Views;

public partial class SettingsWindow : Window
{
    private bool _isUpdatingPassword;
    private Storyboard? _spinStoryboard;

    public SettingsViewModel ViewModel => (SettingsViewModel)DataContext;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        Loaded += async (_, _) =>
        {
            await viewModel.LoadAsync();
            _isUpdatingPassword = true;
            ApiKeyPasswordBox.Password = viewModel.ApiKey;
            _isUpdatingPassword = false;

            _spinStoryboard = (Storyboard)FindResource("SpinAnimation");
            UpdateSpinnerAnimation(viewModel.IsMemoryUpdating);
        };

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.IsMemoryUpdating))
        {
            UpdateSpinnerAnimation(ViewModel.IsMemoryUpdating);
        }
    }

    private void UpdateSpinnerAnimation(bool isUpdating)
    {
        if (_spinStoryboard is null) return;

        if (isUpdating)
            _spinStoryboard.Begin(this, true);
        else
            _spinStoryboard.Stop(this);
    }

    private void ApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingPassword) return;
        ViewModel.ApiKey = ApiKeyPasswordBox.Password;
    }

    private void HotkeyRecorder_MouseDown(object sender, MouseButtonEventArgs e)
    {
        ViewModel.IsRecordingHotkey = true;
        HotkeyRecorder.BorderBrush = (Brush)FindResource("AccentBrush");
        HotkeyRecorder.Focus();
        e.Handled = true;
    }

    private void HotkeyRecorder_KeyDown(object sender, KeyEventArgs e)
    {
        if (!ViewModel.IsRecordingHotkey) return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Ignore modifier-only presses
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            e.Handled = true;
            return;
        }

        // Escape cancels recording
        if (key == Key.Escape)
        {
            ViewModel.IsRecordingHotkey = false;
            HotkeyRecorder.BorderBrush = (Brush)FindResource("BorderBrush");
            e.Handled = true;
            return;
        }

        var modifiers = Keyboard.Modifiers;

        // Require at least one modifier
        if (modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            return;
        }

        ViewModel.SetHotkey(key, modifiers);
        HotkeyRecorder.BorderBrush = (Brush)FindResource("BorderBrush");
        e.Handled = true;
    }

    private void HotkeyRecorder_LostFocus(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsRecordingHotkey)
        {
            ViewModel.IsRecordingHotkey = false;
            HotkeyRecorder.BorderBrush = (Brush)FindResource("BorderBrush");
        }
    }

    private void ShowPasswordToggle_Click(object sender, RoutedEventArgs e)
    {
        var isChecked = ShowPasswordToggle.IsChecked == true;

        if (isChecked)
        {
            ApiKeyTextBox.Text = ViewModel.ApiKey;
            ApiKeyPasswordBox.Visibility = Visibility.Collapsed;
            ApiKeyTextBox.Visibility = Visibility.Visible;
            ApiKeyTextBox.Focus();
        }
        else
        {
            _isUpdatingPassword = true;
            ApiKeyPasswordBox.Password = ViewModel.ApiKey;
            _isUpdatingPassword = false;
            ApiKeyTextBox.Visibility = Visibility.Collapsed;
            ApiKeyPasswordBox.Visibility = Visibility.Visible;
            ApiKeyPasswordBox.Focus();
        }
    }
}
