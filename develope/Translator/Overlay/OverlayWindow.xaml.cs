using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Extensions.Logging;

namespace Translator.Overlay;

/// <summary>
/// 클릭스루 투명 오버레이 창 (코드비하인드에는 UI 초기화 및 Win32 설정만 포함)
/// </summary>
public partial class OverlayWindow : Window
{
    private const int GwlExstyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExLayered = 0x00080000;

    private readonly ILogger<OverlayWindow> _logger;

    public OverlayWindow(ILogger<OverlayWindow> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        InitializeComponent();
    }

    /// <summary>
    /// 창이 로드된 이후 Win32 클릭스루 스타일을 적용합니다.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        SetClickThrough();
    }

    /// <summary>
    /// WS_EX_TRANSPARENT + WS_EX_LAYERED 설정으로 Win32 레벨 클릭스루를 활성화합니다.
    /// </summary>
    private void SetClickThrough()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            _logger.LogWarning("OverlayWindow 핸들을 가져올 수 없습니다. 클릭스루 설정을 건너뜁니다.");
            return;
        }

        var currentStyle = NativeMethods.GetWindowLong(hwnd, GwlExstyle);
        var newStyle = currentStyle | WsExTransparent | WsExLayered;
        NativeMethods.SetWindowLong(hwnd, GwlExstyle, newStyle);

        _logger.LogDebug("클릭스루 설정 완료. HWND={Hwnd:X}, ExStyle={Style:X}", hwnd, newStyle);
    }

    /// <summary>
    /// 오버레이 Canvas를 외부에서 접근할 수 있도록 노출합니다.
    /// </summary>
    internal System.Windows.Controls.Canvas Canvas => OverlayCanvas;

    /// <summary>
    /// Win32 P/Invoke 래퍼 (정적 내부 클래스로 격리)
    /// </summary>
    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}
