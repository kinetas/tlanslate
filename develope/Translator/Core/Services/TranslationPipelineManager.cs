using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Translator.Core.Interfaces;
using Translator.Core.Models;

namespace Translator.Core.Services;

/// <summary>
/// 번역 전체 파이프라인을 조율하는 매니저.
/// Region → ScreenCapture(OCRManager) → 텍스트변경감지 → CacheCheck → TranslateAsync → OverlayRender
/// 순서로 처리하며, ITranslator / IOcrProvider(OCRManager 내부) / ICacheService / IOverlayRenderer를 주입받습니다.
/// CancellationToken과 초당 최대 호출 횟수 제한(Rate Limiting)을 지원합니다.
/// </summary>
public sealed class TranslationPipelineManager : IDisposable
{
    private readonly ITranslator _translator;
    private readonly OCRManager _ocrManager;
    private readonly ICacheService _cacheService;
    private readonly IOverlayRenderer _overlayRenderer;
    private readonly AppSettings _settings;
    private readonly ILogger<TranslationPipelineManager> _logger;

    /// <summary>
    /// Rate Limiter: 초당 최대 호출 횟수 제한용 슬라이딩 윈도우 타임스탬프 큐.
    /// </summary>
    private readonly ConcurrentQueue<DateTimeOffset> _callTimestamps = new();
    private readonly SemaphoreSlim _rateLimitLock = new(1, 1);

    private bool _disposed;

    /// <summary>
    /// 초당 최대 번역 API 호출 횟수. 0이면 제한 없음.
    /// </summary>
    public int MaxCallsPerSecond { get; set; } = 3;

    public TranslationPipelineManager(
        ITranslator translator,
        OCRManager ocrManager,
        ICacheService cacheService,
        IOverlayRenderer overlayRenderer,
        AppSettings settings,
        ILogger<TranslationPipelineManager> logger)
    {
        _translator = translator ?? throw new ArgumentNullException(nameof(translator));
        _ocrManager = ocrManager ?? throw new ArgumentNullException(nameof(ocrManager));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        _overlayRenderer = overlayRenderer ?? throw new ArgumentNullException(nameof(overlayRenderer));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ──────────────────────────────────────────────────────────
    // 공개 API
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// 단일 영역에 대해 블록 단위 OCR → 변경감지 → 캐시 확인 → 배치 번역 → 각 블록 위치에 오버레이 렌더링.
    /// </summary>
    /// <param name="region">처리할 화면 영역</param>
    /// <param name="cancellationToken">취소 토큰</param>
    public async Task ProcessRegionAsync(Region region, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(region);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (region.IsEmpty)
        {
            _logger.LogWarning("비어있는 영역 처리 요청은 무시됩니다.");
            return;
        }

        _logger.LogDebug("블록 단위 파이프라인 시작. Region=({X},{Y},{W},{H})",
            region.X, region.Y, region.Width, region.Height);

        // 1단계: 블록 단위 OCR + 변경 감지
        var changedBlocks = await _ocrManager
            .RecognizeChangedBlocksAsync(region, cancellationToken)
            .ConfigureAwait(false);

        if (changedBlocks.Length == 0)
        {
            _logger.LogDebug("블록 텍스트 변경 없음. 파이프라인 조기 종료.");
            return;
        }

        // 2단계: 캐시 분리 (캐시 히트 / 번역 필요)
        var targetLanguage = _settings.TranslationApi.TargetLanguage;
        var translations = new string?[changedBlocks.Length];

        var toTranslateIndices = new List<int>(changedBlocks.Length);

        for (int i = 0; i < changedBlocks.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var text = changedBlocks[i].Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                translations[i] = null;
                continue;
            }

            var cacheKey = BuildCacheKey(text, targetLanguage);
            var cached = await _cacheService.GetAsync<string>(cacheKey).ConfigureAwait(false);

            if (cached is not null)
            {
                translations[i] = cached;
                _logger.LogDebug("배치 파이프라인 캐시 히트. Key={Key}", cacheKey);
            }
            else
            {
                toTranslateIndices.Add(i);
            }
        }

        // 3단계: 캐시 미스 항목 배치 번역
        if (toTranslateIndices.Count > 0)
        {
            await EnforceRateLimitAsync(cancellationToken).ConfigureAwait(false);

            var textsToTranslate = toTranslateIndices
                .Select(idx => changedBlocks[idx].Text)
                .ToList();

            IReadOnlyList<string> batchResults;
            try
            {
                batchResults = await _translator
                    .TranslateBatchAsync(textsToTranslate, targetLanguage, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "배치 번역 API 호출 실패. 텍스트 수={Count}", textsToTranslate.Count);
                return;
            }

            var expiration = TimeSpan.FromMinutes(_settings.General.CacheExpirationMinutes);

            for (int i = 0; i < toTranslateIndices.Count; i++)
            {
                var blockIdx = toTranslateIndices[i];
                var translated = batchResults[i];

                if (!string.IsNullOrWhiteSpace(translated))
                {
                    translations[blockIdx] = translated;
                    var cacheKey = BuildCacheKey(changedBlocks[blockIdx].Text, targetLanguage);
                    await _cacheService.SetAsync(cacheKey, translated, expiration).ConfigureAwait(false);
                }
            }
        }

        // 4단계: 각 블록 위치에 번역 오버레이 렌더링
        var renderItems = new List<(OCRResult Source, string Translation)>(changedBlocks.Length);

        for (int i = 0; i < changedBlocks.Length; i++)
        {
            var translatedText = translations[i];
            if (!string.IsNullOrWhiteSpace(translatedText))
                renderItems.Add((changedBlocks[i], translatedText));
        }

        if (renderItems.Count == 0)
        {
            _logger.LogDebug("번역 결과 없음. 오버레이 렌더링 건너뜀.");
            return;
        }

        _overlayRenderer.RenderTranslations(renderItems);

        _logger.LogInformation("블록 단위 파이프라인 완료. 렌더링된 항목={Count}", renderItems.Count);
    }

    /// <summary>
    /// 여러 영역을 순차적으로 처리합니다. 각 영역에서 블록 단위 OCR과 배치 번역을 수행합니다.
    /// </summary>
    /// <param name="regions">처리할 화면 영역 목록</param>
    /// <param name="cancellationToken">취소 토큰</param>
    public async Task ProcessRegionsAsync(
        IEnumerable<Region> regions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(regions);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var regionList = regions.ToList();
        if (regionList.Count == 0)
        {
            _logger.LogDebug("처리할 영역이 없습니다.");
            return;
        }

        _logger.LogDebug("다중 영역 블록 파이프라인 시작. Count={Count}", regionList.Count);

        foreach (var region in regionList)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ProcessRegionAsync(region, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogDebug("다중 영역 블록 파이프라인 완료.");
    }

    /// <summary>
    /// 지정된 간격으로 단일 영역을 반복 처리하는 루프를 시작합니다.
    /// CancellationToken으로 루프를 중단할 수 있습니다.
    /// </summary>
    /// <param name="region">처리할 화면 영역</param>
    /// <param name="intervalMs">반복 간격 (밀리초). AppSettings.Ocr.CaptureIntervalMs 기본값 사용.</param>
    /// <param name="cancellationToken">취소 토큰</param>
    public async Task RunContinuousAsync(
        Region region,
        int? intervalMs = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(region);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var interval = intervalMs ?? _settings.Ocr.CaptureIntervalMs;
        if (interval <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(intervalMs), "캡처 간격은 0보다 커야 합니다.");
        }

        _logger.LogInformation(
            "연속 파이프라인 시작. Region=({X},{Y},{W},{H}), Interval={Interval}ms",
            region.X, region.Y, region.Width, region.Height, interval);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ProcessRegionAsync(region, cancellationToken).ConfigureAwait(false);
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("연속 파이프라인 취소됨.");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "연속 파이프라인 처리 중 오류 발생. 다음 주기에 재시도합니다.");
                // 오류 발생 시에도 루프를 유지 — 취소 시에만 종료
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            }
        }

        _logger.LogInformation("연속 파이프라인 종료.");
    }

    /// <summary>
    /// 현재 오버레이를 즉시 지웁니다.
    /// </summary>
    public void ClearOverlay()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _overlayRenderer.ClearAllOverlays();
        _logger.LogDebug("오버레이 강제 초기화.");
    }

    // ──────────────────────────────────────────────────────────
    // 내부 헬퍼
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// 캐시 우선 조회 → 캐시 미스 시 번역 API 호출(Rate Limit 적용) → 결과 캐시 저장.
    /// 번역에 실패하거나 빈 텍스트이면 null을 반환합니다.
    /// </summary>
    private async Task<string?> ResolveTranslationAsync(
        OCRResult ocrResult,
        CancellationToken cancellationToken)
    {
        var text = ocrResult.Text;

        if (string.IsNullOrWhiteSpace(text))
        {
            _logger.LogDebug("OCR 텍스트 비어있음. 번역 건너뜀.");
            return null;
        }

        var targetLanguage = _settings.TranslationApi.TargetLanguage;
        var cacheKey = BuildCacheKey(text, targetLanguage);

        // 2a. 캐시 조회
        var cached = await _cacheService.GetAsync<string>(cacheKey).ConfigureAwait(false);
        if (cached is not null)
        {
            _logger.LogDebug("캐시 히트. Key={Key}", cacheKey);
            return cached;
        }

        // 2b. Rate Limit 확인
        await EnforceRateLimitAsync(cancellationToken).ConfigureAwait(false);

        // 2c. 번역 API 호출
        TranslationResult result;
        try
        {
            result = await _translator
                .TranslateAsync(text, targetLanguage, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw; // 취소는 상위로 전파
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "번역 API 호출 실패. Text={TextPreview}",
                text.Length > 30 ? text[..30] + "…" : text);
            return null;
        }

        if (string.IsNullOrWhiteSpace(result.TranslatedText))
        {
            _logger.LogWarning("번역 결과가 비어있습니다. OriginalText={TextPreview}",
                text.Length > 30 ? text[..30] + "…" : text);
            return null;
        }

        // 2d. 캐시에 저장
        var expiration = TimeSpan.FromMinutes(_settings.General.CacheExpirationMinutes);
        await _cacheService
            .SetAsync(cacheKey, result.TranslatedText, expiration)
            .ConfigureAwait(false);

        _logger.LogDebug("번역 완료 및 캐시 저장. Key={Key}", cacheKey);

        return result.TranslatedText;
    }

    /// <summary>
    /// 슬라이딩 윈도우 방식으로 초당 최대 호출 횟수를 제한합니다.
    /// MaxCallsPerSecond=0이면 제한 없음.
    /// </summary>
    private async Task EnforceRateLimitAsync(CancellationToken cancellationToken)
    {
        if (MaxCallsPerSecond <= 0)
            return;

        await _rateLimitLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var windowStart = now.AddSeconds(-1);

            // 1초 이전 타임스탬프 제거
            while (_callTimestamps.TryPeek(out var oldest) && oldest < windowStart)
            {
                _callTimestamps.TryDequeue(out _);
            }

            // 현재 윈도우 내 호출 수가 한도를 초과하면 대기
            if (_callTimestamps.Count >= MaxCallsPerSecond)
            {
                if (_callTimestamps.TryPeek(out var earliestInWindow))
                {
                    var waitTime = earliestInWindow.AddSeconds(1) - now;
                    if (waitTime > TimeSpan.Zero)
                    {
                        _logger.LogDebug(
                            "Rate Limit 적용. 대기={WaitMs}ms, CurrentCalls={Count}/{Max}",
                            waitTime.TotalMilliseconds, _callTimestamps.Count, MaxCallsPerSecond);

                        await Task.Delay(waitTime, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            _callTimestamps.Enqueue(DateTimeOffset.UtcNow);
        }
        finally
        {
            _rateLimitLock.Release();
        }
    }

    /// <summary>
    /// 원문과 목표 언어로 캐시 키를 생성합니다.
    /// </summary>
    private static string BuildCacheKey(string text, string targetLanguage) =>
        $"{targetLanguage}:{text}";

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _rateLimitLock.Dispose();
        _ocrManager.Dispose();

        _logger.LogDebug("TranslationPipelineManager 해제 완료.");
    }
}
