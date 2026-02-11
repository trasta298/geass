using System.Windows.Input;
using NHotkey;
using NHotkey.Wpf;

namespace Geass.Services;

public class HotkeyService : IDisposable
{
    private const string HotkeyName = "GeassToggle";

    private Key _key = Key.P;
    private ModifierKeys _modifier = ModifierKeys.Alt;

    public event Action? HotkeyPressed;

    public void Register()
    {
        HotkeyManager.Current.AddOrReplace(
            HotkeyName,
            _key,
            _modifier,
            OnHotkeyPressed);
    }

    public void Register(Key key, ModifierKeys modifier)
    {
        _key = key;
        _modifier = modifier;
        Register();
    }

    public void Unregister()
    {
        try
        {
            HotkeyManager.Current.Remove(HotkeyName);
        }
        catch
        {
            // Already removed
        }
    }

    public static Key ParseKey(string keyName)
    {
        return Enum.TryParse<Key>(keyName, true, out var key) ? key : Key.P;
    }

    public static ModifierKeys ParseModifier(string modifierName)
    {
        return Enum.TryParse<ModifierKeys>(modifierName, true, out var mod) ? mod : ModifierKeys.Alt;
    }

    public static string FormatHotkey(ModifierKeys modifier, Key key)
    {
        var parts = new List<string>();

        if (modifier.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifier.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifier.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifier.HasFlag(ModifierKeys.Windows)) parts.Add("Win");

        parts.Add(FormatKeyName(key));
        return string.Join(" + ", parts);
    }

    private static string FormatKeyName(Key key)
    {
        return key switch
        {
            >= Key.A and <= Key.Z => key.ToString(),
            >= Key.D0 and <= Key.D9 => key.ToString()[1..],
            >= Key.NumPad0 and <= Key.NumPad9 => "Num" + key.ToString().Replace("NumPad", ""),
            Key.OemTilde => "~",
            Key.OemMinus => "-",
            Key.OemPlus => "=",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.OemPipe => "\\",
            Key.OemSemicolon => ";",
            Key.OemQuotes => "'",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemQuestion => "/",
            _ => key.ToString()
        };
    }

    private void OnHotkeyPressed(object? sender, HotkeyEventArgs e)
    {
        HotkeyPressed?.Invoke();
        e.Handled = true;
    }

    public void Dispose()
    {
        Unregister();
        GC.SuppressFinalize(this);
    }
}
