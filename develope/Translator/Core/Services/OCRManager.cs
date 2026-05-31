using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Translator.Core.Interfaces;
using Translator.Core.Models;

namespace Translator.Core.Services;

/// <summary>
/// IScreenCaptureService와 IOcrProvider를 조율하는 OCR 관리자.
/// Region → OCRResult[] 처리, 텍스트 변경 감지 포함.
/// 변경된 텍스트만 번역 요청 대상으로 반환합니다.
/// </summary>
public sealed class OCRManager : IDisposable
{
    private readonly ILogger<OCRManager> _logger;
    private readonly IScreenCaptureService _captureService;
    private readonly IOcrProvider _ocrProvider;
    private readonly AppSettings _settings;

    // 영역별 이전 OCR 결과 (텍스트 변경 감지용)
    private readonly Dictionary<string, string> _previousTextByRegion = new();
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    // 블록 단위 변경 감지용: key=영역 식별자, value=이전 블록 텍스트 배열
    private readonly ConcurrentDictionary<string, string[]> _previousBlockTexts = new();

    private bool _disposed;

    public OCRManager(
        ILogger<OCRManager> logger,
        IScreenCaptureService captureService,
        IOcrProvider ocrProvider,
        AppSettings settings)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _captureService = captureService ?? throw new ArgumentNullException(nameof(captureService));
        _ocrProvider = ocrProvider ?? throw new ArgumentNullException(nameof(ocrProvider));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <summary>
    /// 지정된 화면 영역에서 OCR을 수행하고 결과를 반환합니다.
    /// </summary>
    /// <param name="region">인식할 화면 영역</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>OCR 인식 결과 배열</returns>
    public async Task<OCRResult[]> RecognizeRegionAsync(
        Region region,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(region);

        if (region.IsEmpty)
        {
            _logger.LogWarning("빈 영역 인식 요청이 무시됩니다.");
            return Array.Empty<OCRResult>();
        }

        _logger.LogDebug("OCR 인식 시작: X={X}, Y={Y}, W={W}, H={H}",
            region.X, region.Y, region.Width, region.Height);

        var result = await _ocrProvider
            .RecognizeAsync(region, cancellationToken)
            .ConfigureAwait(false);

        // 신뢰도 임계값 필터링
        if (result.Confidence < _settings.Ocr.MinConfidenceThreshold)
        {
            _logger.LogDebug("신뢰도 임계값 미달로 결과 제외: {Confidence:F3} < {Threshold:F3}",
                result.Confidence, _settings.Ocr.MinConfidenceThreshold);
            return Array.Empty<OCRResult>();
        }

        return new[] { result };
    }

    /// <summary>
    /// 지정된 화면 영역의 텍스트 변경 여부를 감지하여 변경된 결과만 반환합니다.
    /// 이전 결과와 동일한 텍스트는 제외되어 번역 요청 수를 최소화합니다.
    /// </summary>
    /// <param name="region">인식할 화면 영역</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>변경된 OCR 결과 배열. 변경 없으면 빈 배열.</returns>
    public async Task<OCRResult[]> RecognizeChangedTextAsync(
        Region region,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(region);

        var results = await RecognizeRegionAsync(region, cancellationToken).ConfigureAwait(false);

        if (results.Length == 0)
            return results;

        var regionKey = GetRegionKey(region);
        var changedResults = new List<OCRResult>();

        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var result in results)
            {
                if (string.IsNullOrWhiteSpace(result.Text))
                    continue;

                if (_previousTextByRegion.TryGetValue(regionKey, out var previousText)
                    && string.Equals(previousText, result.Text, StringComparison.Ordinal))
                {
                    _logger.LogDebug("텍스트 변경 없음: 영역={RegionKey}", regionKey);
                    continue;
                }

                _previousTextByRegion[regionKey] = result.Text;
                changedResults.Add(result);

                _logger.LogDebug("텍스트 변경 감지: 영역={RegionKey}, 텍스트길이={Length}",
                    regionKey, result.Text.Length);
            }
        }
        finally
        {
            _stateLock.Release();
        }

        return changedResults.ToArray();
    }

    /// <summary>
    /// 여러 화면 영역을 병렬로 OCR 처리하고 변경된 결과만 반환합니다.
    /// </summary>
    /// <param name="regions">인식할 화면 영역 목록</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>각 영역별 변경된 OCR 결과 목록</returns>
    public async Task<IReadOnlyList<OCRResult>> RecognizeMultipleRegionsAsync(
        IEnumerable<Region> regions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(regions);

        var regionList = regions.ToList();
        if (regionList.Count == 0)
            return Array.Empty<OCRResult>();

        _logger.LogDebug("다중 영역 OCR 시작: {Count}개 영역", regionList.Count);

        var tasks = regionList
            .Select(region => RecognizeChangedTextAsync(region, cancellationToken))
            .ToList();

        var allResults = await Task.WhenAll(tasks).ConfigureAwait(false);

        var flatResults = allResults
            .SelectMany(r => r)
            .ToList()
            .AsReadOnly();

        _logger.LogDebug("다중 영역 OCR 완료: {Changed}개 변경됨", flatResults.Count);

        return flatResults;
    }

    /// <summary>
    /// 지정된 화면 영역의 텍스트 블록 단위 변경 여부를 감지하여 변경된 블록만 반환합니다.
    /// 이전 블록 목록과 비교하여 텍스트가 동일하면 빈 배열을 반환합니다.
    /// 신뢰도 임계값 미달 블록은 제외됩니다.
    /// </summary>
    /// <param name="region">인식할 화면 영역</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>변경된 OCRResult 블록 배열. 변경 없으면 빈 배열.</returns>
    public async Task<OCRResult[]> RecognizeChangedBlocksAsync(
        Region region,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(region);

        if (region.IsEmpty)
        {
            _logger.LogWarning("빈 영역 블록 인식 요청이 무시됩니다.");
            return Array.Empty<OCRResult>();
        }

        _logger.LogDebug("블록 단위 OCR 시작: X={X}, Y={Y}, W={W}, H={H}",
            region.X, region.Y, region.Width, region.Height);

        var blocks = await _ocrProvider
            .RecognizeBlocksAsync(region, cancellationToken)
            .ConfigureAwait(false);

        // 신뢰도 임계값 필터링
        blocks = blocks
            .Where(b => b.Confidence >= _settings.Ocr.MinConfidenceThreshold)
            .ToArray();

        if (blocks.Length == 0)
        {
            _logger.LogDebug("신뢰도 임계값 필터링 후 블록 없음.");
            return Array.Empty<OCRResult>();
        }

        var regionKey = GetRegionKey(region);
        var currentTexts = blocks.Select(b => b.Text).ToArray();

        if (_previousBlockTexts.TryGetValue(regionKey, out var previousTexts)
            && previousTexts.SequenceEqual(currentTexts, StringComparer.Ordinal))
        {
            _logger.LogDebug("블록 텍스트 변경 없음: 영역={RegionKey}", regionKey);
            return Array.Empty<OCRResult>();
        }

        _previousBlockTexts[regionKey] = currentTexts;

        _logger.LogDebug("블록 텍스트 변경 감지: 영역={RegionKey}, 블록 수={Count}",
            regionKey, blocks.Length);

        return blocks;
    }

    /// <summary>
    /// 특정 영역의 이전 텍스트 상태를 초기화합니다.
    /// </summary>
    /// <param name="region">초기화할 영역</param>
    public async Task ResetRegionStateAsync(Region region)
    {
        ArgumentNullException.ThrowIfNull(region);

        var regionKey = GetRegionKey(region);

        await _stateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _previousTextByRegion.Remove(regionKey);
            _previousBlockTexts.TryRemove(regionKey, out _);
            _logger.LogDebug("영역 상태 초기화: {RegionKey}", regionKey);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// 모든 영역의 이전 텍스트 상태를 초기화합니다.
    /// </summary>
    public async Task ResetAllStatesAsync()
    {
        await _stateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _previousTextByRegion.Clear();
            _previousBlockTexts.Clear();
            _logger.LogInformation("모든 영역 상태 초기화 완료.");
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Region을 딕셔너리 키 문자열로 변환합니다.
    /// </summary>
    private static string GetRegionKey(Region region) =>
        $"{region.X},{region.Y},{region.Width},{region.Height}";

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _stateLock.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
