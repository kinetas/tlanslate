using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Translator.Core.Interfaces;
using Translator.Core.Models;

namespace Translator.Overlay;

/// <summary>
/// WPF Canvas 기반 IOverlayRenderer 구현체.
/// OCR 원문 위치 위에 번역 텍스트를 렌더링하며, 폰트·크기·투명도를 설정에서 주입받습니다.
/// </summary>
public sealed class OverlayRenderer : IOverlayRenderer
{
    private readonly OverlayWindow _window;
    private readonly OverlaySettings _settings;
    private readonly ILogger<OverlayRenderer> _logger;
    private readonly Dispatcher _dispatcher;

    /// <summary>
    /// 현재 Canvas에 렌더링된 아이템 목록 (Id → UIElement)
    /// </summary>
    private readonly Dictionary<Guid, UIElement> _activeElements = new();
    private readonly List<OverlayItem> _activeItems = new();
    private readonly object _lock = new();

    public OverlayRenderer(
        OverlayWindow window,
        OverlaySettings settings,
        ILogger<OverlayRenderer> logger)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dispatcher = window.Dispatcher;
    }

    /// <summary>
    /// OCR 결과와 번역 텍스트 쌍을 받아 각 원문 위치에 번역 텍스트를 렌더링합니다.
    /// </summary>
    public void RenderTranslations(IReadOnlyList<(OCRResult Source, string Translation)> items)
    {
        if (items is null) throw new ArgumentNullException(nameof(items));

        _dispatcher.Invoke(() =>
        {
            ClearCanvasInternal();

            foreach (var (source, translation) in items)
            {
                if (string.IsNullOrWhiteSpace(translation))
                {
                    _logger.LogDebug("빈 번역 텍스트 건너뜀. Region={Region}", source.Region);
                    continue;
                }

                var overlayItem = OverlayItem.Create(translation, source.Region);
                var element = CreateOverlayElement(overlayItem);

                Canvas.SetLeft(element, source.Region.X);
                Canvas.SetTop(element, source.Region.Y);

                _window.Canvas.Children.Add(element);

                lock (_lock)
                {
                    _activeElements[overlayItem.Id] = element;
                    _activeItems.Add(overlayItem);
                }

                _logger.LogTrace(
                    "오버레이 렌더링. Id={Id}, Region=({X},{Y},{W},{H}), Text={Text}",
                    overlayItem.Id, source.Region.X, source.Region.Y,
                    source.Region.Width, source.Region.Height,
                    translation.Length > 30 ? translation[..30] + "…" : translation);
            }

            if (!_window.IsVisible)
            {
                _window.Show();
            }
        });
    }

    /// <inheritdoc />
    public void ShowOverlay(OverlayItem item)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));

        _dispatcher.Invoke(() =>
        {
            if (_activeElements.ContainsKey(item.Id))
            {
                _logger.LogDebug("이미 표시 중인 아이템입니다. Id={Id}", item.Id);
                return;
            }

            var element = CreateOverlayElement(item);
            Canvas.SetLeft(element, item.Region.X);
            Canvas.SetTop(element, item.Region.Y);

            _window.Canvas.Children.Add(element);

            lock (_lock)
            {
                _activeElements[item.Id] = element;
                _activeItems.Add(item);
            }

            if (!_window.IsVisible)
            {
                _window.Show();
            }

            _logger.LogDebug("오버레이 표시. Id={Id}", item.Id);
        });
    }

    /// <inheritdoc />
    public void HideOverlay(Guid itemId)
    {
        _dispatcher.Invoke(() =>
        {
            lock (_lock)
            {
                if (!_activeElements.TryGetValue(itemId, out var element))
                {
                    _logger.LogDebug("숨길 아이템을 찾지 못했습니다. Id={Id}", itemId);
                    return;
                }

                _window.Canvas.Children.Remove(element);
                _activeElements.Remove(itemId);
                _activeItems.RemoveAll(i => i.Id == itemId);
            }

            _logger.LogDebug("오버레이 숨김. Id={Id}", itemId);
        });
    }

    /// <inheritdoc />
    public void ClearAllOverlays()
    {
        _dispatcher.Invoke(() =>
        {
            ClearCanvasInternal();
            _logger.LogDebug("모든 오버레이 제거 완료.");
        });
    }

    /// <inheritdoc />
    public void Clear() => ClearAllOverlays();

    /// <inheritdoc />
    public IReadOnlyList<OverlayItem> GetActiveOverlays()
    {
        lock (_lock)
        {
            return _activeItems.AsReadOnly();
        }
    }

    // ──────────────────────────────────────────────────────────
    // 내부 헬퍼
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Canvas 및 내부 상태를 초기화합니다. Dispatcher 스레드에서 호출해야 합니다.
    /// </summary>
    private void ClearCanvasInternal()
    {
        _window.Canvas.Children.Clear();
        lock (_lock)
        {
            _activeElements.Clear();
            _activeItems.Clear();
        }
    }

    /// <summary>
    /// 설정 기반으로 번역 텍스트 UIElement(Border + TextBlock)를 생성합니다.
    /// </summary>
    private UIElement CreateOverlayElement(OverlayItem item)
    {
        var background = ParseColor(_settings.BackgroundColor, fallback: Color.FromArgb(0xCC, 0x00, 0x00, 0x00));
        var foreground = ParseColor(_settings.TextColor, fallback: Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));

        var textBlock = new TextBlock
        {
            Text = item.TranslatedText,
            Foreground = new SolidColorBrush(foreground),
            FontSize = _settings.FontSize,
            FontFamily = new FontFamily("Malgun Gothic, Segoe UI, Arial"),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = Math.Max(item.Region.Width, 50),
        };

        var border = new Border
        {
            Background = new SolidColorBrush(background),
            Opacity = _settings.Opacity,
            Padding = new Thickness(4, 2, 4, 2),
            CornerRadius = new CornerRadius(3),
            Child = textBlock,
            IsHitTestVisible = false,
        };

        return border;
    }

    /// <summary>
    /// ARGB 16진수 문자열("#AARRGGBB" 또는 "#RRGGBB")을 Color로 변환합니다.
    /// 파싱 실패 시 fallback 색상을 반환합니다.
    /// </summary>
    private Color ParseColor(string colorString, Color fallback)
    {
        try
        {
            if (ColorConverter.ConvertFromString(colorString) is Color color)
            {
                return color;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "색상 파싱 실패. Value={Value}, fallback 사용.", colorString);
        }

        return fallback;
    }
}
