using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Translator.Core.Interfaces;
using Translator.Core.Models;

namespace Translator.OCR;

/// <summary>
/// BitBlt 기반 화면 캡처 서비스 구현체.
/// 캡처 이미지는 메모리에서만 처리되며 디스크에 절대 저장되지 않습니다.
/// </summary>
public sealed class ScreenCaptureService : IScreenCaptureService, IDisposable
{
    private readonly ILogger<ScreenCaptureService> _logger;
    private bool _disposed;

    // GDI32 / User32 P/Invoke
    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool BitBlt(
        IntPtr hdcDest, int xDest, int yDest, int width, int height,
        IntPtr hdcSrc, int xSrc, int ySrc, int ropCode);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteDC(IntPtr hdc);

    private const int SrcCopy = 0x00CC0020;

    public ScreenCaptureService(ILogger<ScreenCaptureService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public Task<Bitmap> CaptureRegionAsync(Region region, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(region);

        if (region.IsEmpty)
            throw new ArgumentException("캡처 영역이 비어 있습니다.", nameof(region));

        cancellationToken.ThrowIfCancellationRequested();

        // BitBlt는 동기 GDI 호출이므로 Task.Run으로 스레드풀에서 실행
        return Task.Run(() => CaptureWithBitBlt(region), cancellationToken);
    }

    /// <summary>
    /// BitBlt를 사용하여 화면 영역을 캡처합니다.
    /// Graphics.CopyFromScreen 폴백 포함.
    /// </summary>
    private Bitmap CaptureWithBitBlt(Region region)
    {
        _logger.LogDebug("BitBlt 캡처 시작: X={X}, Y={Y}, W={W}, H={H}",
            region.X, region.Y, region.Width, region.Height);

        IntPtr desktopHwnd = IntPtr.Zero;
        IntPtr desktopDc = IntPtr.Zero;
        IntPtr memDc = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr oldObj = IntPtr.Zero;

        try
        {
            desktopHwnd = GetDesktopWindow();
            desktopDc = GetDC(desktopHwnd);

            if (desktopDc == IntPtr.Zero)
            {
                _logger.LogWarning("GetDC 실패. Graphics.CopyFromScreen 폴백으로 전환합니다.");
                return CaptureWithCopyFromScreen(region);
            }

            memDc = CreateCompatibleDC(desktopDc);
            hBitmap = CreateCompatibleBitmap(desktopDc, region.Width, region.Height);
            oldObj = SelectObject(memDc, hBitmap);

            bool success = BitBlt(
                memDc, 0, 0, region.Width, region.Height,
                desktopDc, region.X, region.Y, SrcCopy);

            if (!success)
            {
                int error = Marshal.GetLastWin32Error();
                _logger.LogWarning("BitBlt 실패 (Win32Error={Error}). Graphics.CopyFromScreen 폴백으로 전환합니다.", error);
                return CaptureWithCopyFromScreen(region);
            }

            // GDI HBITMAP → managed Bitmap으로 변환 (메모리 복사)
            var bitmap = Image.FromHbitmap(hBitmap);

            _logger.LogDebug("BitBlt 캡처 완료: {W}x{H}", region.Width, region.Height);
            return bitmap;
        }
        finally
        {
            if (oldObj != IntPtr.Zero && memDc != IntPtr.Zero)
                SelectObject(memDc, oldObj);
            if (hBitmap != IntPtr.Zero)
                DeleteObject(hBitmap);
            if (memDc != IntPtr.Zero)
                DeleteDC(memDc);
            if (desktopDc != IntPtr.Zero && desktopHwnd != IntPtr.Zero)
                ReleaseDC(desktopHwnd, desktopDc);
        }
    }

    /// <summary>
    /// Graphics.CopyFromScreen 기반 폴백 캡처.
    /// BitBlt 실패 시 호출됩니다.
    /// </summary>
    private Bitmap CaptureWithCopyFromScreen(Region region)
    {
        _logger.LogDebug("CopyFromScreen 캡처 시작: X={X}, Y={Y}, W={W}, H={H}",
            region.X, region.Y, region.Width, region.Height);

        var bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(
                region.X, region.Y,
                0, 0,
                new System.Drawing.Size(region.Width, region.Height),
                CopyPixelOperation.SourceCopy);

            _logger.LogDebug("CopyFromScreen 캡처 완료: {W}x{H}", region.Width, region.Height);
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
