using System.Windows;
using Geass.Helpers;

namespace Geass.Services;

public class ClipboardService
{
    public async Task SetTextAndPaste(string text)
    {
        // Retry — Clipboard.SetText can throw ExternalException if locked
        for (var i = 0; i < 5; i++)
        {
            try
            {
                Clipboard.SetText(text);
                break;
            }
            catch (System.Runtime.InteropServices.ExternalException) when (i < 4)
            {
                await Task.Delay(50);
            }
        }

        // Short delay to ensure clipboard is ready
        await Task.Delay(100);

        KeyboardSimulator.SendCtrlV();
    }
}
