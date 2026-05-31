using System.Windows;
using Microsoft.Extensions.Logging;
using Translator.Core.Interfaces;
using Translator.Core.Models;

namespace Translator.Overlay;

/// <summary>
/// 오버레이 창 수명 주기를 관리하고 IOverlayRenderer를 외부에 노출하는 서비스.
/// DI 컨테이너에서 싱글턴으로 등록하여 사용합니다.
/// </summary>
public sealed class OverlayService : IDisposable
{
    private readonly OverlayWindow _window;
    private readonly ILogger<OverlayService> _logger;
    private bool _disposed;

    /// <summary>
    /// 렌더러 인스턴스. 파이프라인에서 직접 사용합니다.
    /// </summary>
    public IOverlayRenderer Renderer { get; }

    public OverlayService(
        OverlayWindow window,
        OverlayRenderer renderer,
        ILogger<OverlayService> logger)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        Renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 오버레이 창을 전체 화면 크기로 펼쳐 표시합니다.
    /// WPF Dispatcher 스레드에서 호출되어야 합니다.
    /// </summary>
    public void Show()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _window.Dispatcher.Invoke(() =>
        {
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            var screenHeight = SystemParameters.PrimaryScreenHeight;

            _window.Left = 0;
            _window.Top = 0;
            _window.Width = screenWidth;
            _window.Height = screenHeight;

            _window.Show();
            _logger.LogInformation("오버레이 창 표시. Size={W}x{H}", screenWidth, screenHeight);
        });
    }

    /// <summary>
    /// 오버레이 창을 숨기고 모든 렌더링된 항목을 제거합니다.
    /// </summary>
    public void Hide()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _window.Dispatcher.Invoke(() =>
        {
            Renderer.ClearAllOverlays();
            _window.Hide();
            _logger.LogInformation("오버레이 창 숨김.");
        });
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _window.Dispatcher.Invoke(() =>
        {
            Renderer.ClearAllOverlays();
            _window.Close();
        });

        _logger.LogDebug("OverlayService 해제 완료.");
    }
}
