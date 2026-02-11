using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace Geass.Services;

public class ScreenCaptureService
{
    private const int MaxWidth = 1280;
    private const int MaxHeight = 720;
    private const long JpegQuality = 80;
    private const uint PW_RENDERFULLCONTENT = 0x00000002;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    public byte[]? CaptureWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
            return null;

        // Skip hung windows
        if (IsHungAppWindow(hWnd))
            return null;

        // Skip our own process
        GetWindowThreadProcessId(hWnd, out var windowPid);
        if (windowPid == Environment.ProcessId)
            return null;

        // Get window dimensions
        if (!TryGetWindowRect(hWnd, out var rect))
            return null;

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
            return null;

        try
        {
            using var windowBitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(windowBitmap))
            {
                var hdc = g.GetHdc();
                PrintWindow(hWnd, hdc, PW_RENDERFULLCONTENT);
                g.ReleaseHdc(hdc);
            }

            // Resize if larger than max dimensions
            var targetBitmap = ResizeIfNeeded(windowBitmap, width, height);

            try
            {
                return EncodeJpeg(targetBitmap);
            }
            finally
            {
                if (targetBitmap != windowBitmap)
                    targetBitmap.Dispose();
            }
        }
        catch
        {
            return null;
        }
    }

    private static bool TryGetWindowRect(IntPtr hWnd, out RECT rect)
    {
        // Try DWM first for accurate bounds (excludes shadows)
        var hr = DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf<RECT>());
        if (hr == 0)
            return true;

        // Fallback to GetWindowRect
        return GetWindowRect(hWnd, out rect);
    }

    private static Bitmap ResizeIfNeeded(Bitmap source, int width, int height)
    {
        if (width <= MaxWidth && height <= MaxHeight)
            return source;

        var scale = Math.Min((double)MaxWidth / width, (double)MaxHeight / height);
        var newWidth = (int)(width * scale);
        var newHeight = (int)(height * scale);

        var resized = new Bitmap(newWidth, newHeight, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(resized);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(source, 0, 0, newWidth, newHeight);
        return resized;
    }

    private static byte[] EncodeJpeg(Bitmap bitmap)
    {
        var encoder = ImageCodecInfo.GetImageEncoders().First(e => e.FormatID == ImageFormat.Jpeg.Guid);
        var encoderParams = new EncoderParameters(1);
        encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, JpegQuality);

        using var ms = new MemoryStream();
        bitmap.Save(ms, encoder, encoderParams);
        return ms.ToArray();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool IsHungAppWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);
}
