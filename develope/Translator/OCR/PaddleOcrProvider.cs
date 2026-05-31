using System.Drawing;
using Microsoft.Extensions.Logging;
using PaddleOCRSharp;
using Translator.Core.Interfaces;
using Translator.Core.Models;

namespace Translator.OCR;

/// <summary>
/// PaddleOCR.NET NuGet 패키지를 사용하는 OCR 제공자 구현체.
/// IScreenCaptureService로 화면 영역을 캡처하고 PaddleOCR로 텍스트를 추출합니다.
/// </summary>
public sealed class PaddleOcrProvider : IOcrProvider, IDisposable
{
    private readonly ILogger<PaddleOcrProvider> _logger;
    private readonly IScreenCaptureService _captureService;
    private readonly AppSettings _settings;
    private PaddleOCREngine? _engine;
    private bool _disposed;
    private readonly SemaphoreSlim _engineLock = new(1, 1);

    public PaddleOcrProvider(
        ILogger<PaddleOcrProvider> logger,
        IScreenCaptureService captureService,
        AppSettings settings)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _captureService = captureService ?? throw new ArgumentNullException(nameof(captureService));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <summary>
    /// PaddleOCR 엔진을 초기화합니다. 첫 번째 인식 요청 전에 호출합니다.
    /// </summary>
    public void Initialize()
    {
        if (_engine is not null)
            return;

        _logger.LogInformation("PaddleOCR 엔진 초기화 시작. 언어: {Language}",
            _settings.Ocr.RecognitionLanguage);

        try
        {
            var config = new OCRModelConfig();
            var parameter = new OCRParameter
            {
                use_angle_cls = true,
                use_gpu = false
            };

            _engine = new PaddleOCREngine(config, parameter);
            _logger.LogInformation("PaddleOCR 엔진 초기화 완료.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PaddleOCR 엔진 초기화 실패.");
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<OCRResult> RecognizeAsync(
        Region region,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(region);

        if (region.IsEmpty)
            throw new ArgumentException("인식 영역이 비어 있습니다.", nameof(region));

        // 화면 캡처 (메모리에서만 처리, 디스크 저장 금지)
        using var bitmap = await _captureService
            .CaptureRegionAsync(region, cancellationToken)
            .ConfigureAwait(false);

        var capturedAt = DateTimeOffset.UtcNow;

        var ocrResults = await RecognizeBitmapAsync(bitmap, cancellationToken).ConfigureAwait(false);

        // 단일 영역 결과: 모든 텍스트를 줄바꿈으로 합쳐 하나의 OCRResult 반환
        var combinedText = string.Join(Environment.NewLine,
            ocrResults.Select(r => r.Text));

        var avgConfidence = ocrResults.Length > 0
            ? ocrResults.Average(r => r.Confidence)
            : 0.0;

        _logger.LogDebug("RecognizeAsync 완료: 텍스트 블록={Count}, 평균신뢰도={Conf:F3}",
            ocrResults.Length, avgConfidence);

        return new OCRResult(combinedText, region, avgConfidence, capturedAt);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<OCRResult>> RecognizeAllAsync(
        CancellationToken cancellationToken = default)
    {
        // 전체 화면을 전체 기본 스크린 크기로 캡처
        var screenBounds = GetPrimaryScreenBounds();
        var fullRegion = new Region(
            screenBounds.X, screenBounds.Y,
            screenBounds.Width, screenBounds.Height);

        using var bitmap = await _captureService
            .CaptureRegionAsync(fullRegion, cancellationToken)
            .ConfigureAwait(false);

        var capturedAt = DateTimeOffset.UtcNow;

        var results = await RecognizeBitmapAsync(bitmap, cancellationToken).ConfigureAwait(false);

        _logger.LogDebug("RecognizeAllAsync 완료: 텍스트 블록={Count}", results.Length);

        // 각 블록을 개별 OCRResult로 반환
        return results
            .Select(r => r with { CapturedAt = capturedAt })
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Bitmap을 PaddleOCR로 인식하여 OCRResult 배열을 반환합니다.
    /// 텍스트와 좌표(Region)를 모두 추출합니다.
    /// </summary>
    private async Task<OCRResult[]> RecognizeBitmapAsync(
        Bitmap bitmap,
        CancellationToken cancellationToken)
    {
        EnsureEngineInitialized();

        await _engineLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // PaddleOCR은 동기 API이므로 스레드풀에서 실행
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var ocrResult = _engine!.DetectText(bitmap);

                if (ocrResult?.TextBlocks is null || ocrResult.TextBlocks.Count == 0)
                {
                    _logger.LogDebug("PaddleOCR 인식 결과 없음.");
                    return Array.Empty<OCRResult>();
                }

                var results = new List<OCRResult>(ocrResult.TextBlocks.Count);
                var capturedAt = DateTimeOffset.UtcNow;

                foreach (var block in ocrResult.TextBlocks)
                {
                    if (string.IsNullOrWhiteSpace(block.Text))
                        continue;

                    // 바운딩 박스: PaddleOCR BoxPoints (4개 꼭짓점) → Region으로 변환
                    var blockRegion = ExtractRegionFromBoxPoints(block.BoxPoints);

                    var result = new OCRResult(
                        block.Text,
                        blockRegion,
                        block.Score,
                        capturedAt);

                    results.Add(result);
                }

                return results.ToArray();
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _engineLock.Release();
        }
    }

    /// <summary>
    /// PaddleOCR BoxPoints (4개 꼭짓점 좌표)를 Region으로 변환합니다.
    /// </summary>
    private static Region ExtractRegionFromBoxPoints(List<System.Drawing.Point>? boxPoints)
    {
        if (boxPoints is null || boxPoints.Count == 0)
            return new Region(0, 0, 0, 0);

        var minX = boxPoints.Min(p => p.X);
        var minY = boxPoints.Min(p => p.Y);
        var maxX = boxPoints.Max(p => p.X);
        var maxY = boxPoints.Max(p => p.Y);

        return new Region(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>
    /// 기본 화면 크기를 반환합니다.
    /// </summary>
    private static System.Drawing.Rectangle GetPrimaryScreenBounds()
    {
        return new System.Drawing.Rectangle(
            0, 0,
            System.Windows.Forms.SystemInformation.PrimaryMonitorSize.Width,
            System.Windows.Forms.SystemInformation.PrimaryMonitorSize.Height);
    }

    private void EnsureEngineInitialized()
    {
        if (_engine is null)
        {
            _logger.LogInformation("PaddleOCR 엔진이 초기화되지 않았습니다. 자동 초기화를 시도합니다.");
            Initialize();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _engine?.Dispose();
            _engineLock.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
