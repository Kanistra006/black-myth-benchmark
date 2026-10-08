using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using static WukongBenchRunner.Automation.NativeMethods;

namespace WukongBenchRunner.Automation;

/// <summary>
/// Measures the average brightness of the game window. The benchmark's loading screen is pure black
/// (luma ≈ 0), while the title screen, main menu and shader-compilation screen are clearly brighter
/// (luma ≈ 14–35 on a 0–255 scale). That difference tells the runner the benchmark scene is loading.
/// </summary>
internal static class ScreenProbe
{
    public const double DarkThreshold = 4;
    public const double BrightThreshold = 8;

    /// <summary>
    /// Average luma (0..255) of the window's client area, or null if it cannot be captured.
    /// The window itself is captured (PrintWindow), not the screen region, so other windows that
    /// overlap it — e.g. while the game is still starting up — cannot affect the measurement.
    /// </summary>
    public static double? MeanLuma(nint hwnd)
    {
        if (!GetClientRect(hwnd, out var rc)) return null;
        int w = rc.Right - rc.Left, h = rc.Bottom - rc.Top;
        if (w <= 0 || h <= 0) return null;

        try
        {
            using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            bool ok;
            using (var g = Graphics.FromImage(bmp))
            {
                var hdc = g.GetHdc();
                try
                {
                    ok = PrintWindow(hwnd, hdc, PW_CLIENTONLY | PW_RENDERFULLCONTENT);
                }
                finally
                {
                    g.ReleaseHdc(hdc);
                }
            }
            return ok ? MeanLuma(bmp, step: 8) : null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or ExternalException or ArgumentException)
        {
            // The desktop can be unavailable (e.g. locked workstation or secure desktop).
            return null;
        }
    }

    internal static unsafe double MeanLuma(Bitmap bmp, int step)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            double sum = 0;
            long n = 0;
            for (var y = 0; y < data.Height; y += step)
            {
                var row = (byte*)data.Scan0 + (long)y * data.Stride;
                for (var x = 0; x < data.Width; x += step)
                {
                    var px = row + x * 4; // BGRA
                    sum += 0.114 * px[0] + 0.587 * px[1] + 0.299 * px[2];
                    n++;
                }
            }
            return n == 0 ? 0 : sum / n;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }
}
