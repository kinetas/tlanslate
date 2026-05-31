using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Translator.Core.Interfaces;
using Translator.Core.Models;

namespace Translator.Core.Services;

/// <summary>
/// txt 파일 기반 번역 캐시 서비스 구현체.
/// 파일 형식: "원문=번역" (한 줄에 하나).
/// 위치: %APPDATA%\Translator\cache.txt
/// AppSettings.General.MaxCacheItems 로 최대 항목 수 제어.
/// </summary>
public sealed class FileCacheService : ICacheService, IDisposable
{
    private readonly ILogger<FileCacheService> _logger;
    private readonly AppSettings _settings;
    private readonly string _cacheFilePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    // 메모리 캐시: key → (value, expireAt)
    private readonly ConcurrentDictionary<string, (string Value, DateTimeOffset? ExpireAt)> _memoryCache = new();

    private bool _disposed;
    private bool _isEnabled;

    /// <summary>
    /// FileCacheService 생성자.
    /// </summary>
    /// <param name="logger">ILogger 인스턴스</param>
    /// <param name="settings">AppSettings (캐시 활성화 여부 및 최대 항목 수)</param>
    public FileCacheService(ILogger<FileCacheService> logger, AppSettings settings)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _cacheFilePath = Path.Combine(appDataPath, "Translator", "cache.txt");

        // 캐시는 기본 활성화 (사용자 설정으로 비활성화 가능)
        _isEnabled = true;
    }

    /// <summary>
    /// 캐시 서비스 활성화/비활성화를 토글합니다.
    /// </summary>
    /// <param name="enabled">true이면 활성화, false이면 비활성화</param>
    public void SetEnabled(bool enabled)
    {
        _isEnabled = enabled;
        _logger.LogInformation("캐시 서비스 상태 변경: {State}", enabled ? "활성화" : "비활성화");
    }

    /// <summary>
    /// 캐시 파일을 메모리로 로드합니다. 애플리케이션 시작 시 호출하십시오.
    /// </summary>
    public async Task LoadFromFileAsync(CancellationToken cancellationToken = default)
    {
        if (!_isEnabled)
            return;

        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(_cacheFilePath)!;
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                _logger.LogInformation("캐시 디렉토리 생성: {Directory}", directory);
                return;
            }

            if (!File.Exists(_cacheFilePath))
            {
                _logger.LogInformation("캐시 파일이 없습니다. 빈 캐시로 시작합니다: {Path}", _cacheFilePath);
                return;
            }

            var lines = await File.ReadAllLinesAsync(_cacheFilePath, cancellationToken).ConfigureAwait(false);
            int loaded = 0;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var separatorIndex = line.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    _logger.LogWarning("캐시 파일 형식 오류 (구분자 없음): {Line}", line);
                    continue;
                }

                var key = line[..separatorIndex];
                var value = line[(separatorIndex + 1)..];
                _memoryCache[key] = (value, null);
                loaded++;
            }

            _logger.LogInformation("캐시 파일 로드 완료: {Count}개 항목, {Path}", loaded, _cacheFilePath);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "캐시 파일 로드 중 IO 오류: {Path}", _cacheFilePath);
            throw;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <inheritdoc/>
    public Task<T?> GetAsync<T>(string key) where T : class
    {
        if (!_isEnabled)
            return Task.FromResult<T?>(null);

        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("캐시 키가 비어 있습니다.", nameof(key));

        if (!_memoryCache.TryGetValue(key, out var entry))
            return Task.FromResult<T?>(null);

        // 만료 확인
        if (entry.ExpireAt.HasValue && DateTimeOffset.UtcNow > entry.ExpireAt.Value)
        {
            _memoryCache.TryRemove(key, out _);
            _logger.LogDebug("캐시 항목 만료됨: {Key}", key);
            return Task.FromResult<T?>(null);
        }

        // string 타입만 지원 (FileCacheService는 텍스트 기반)
        if (typeof(T) != typeof(string))
        {
            _logger.LogWarning("FileCacheService는 string 타입만 지원합니다. 요청 타입: {Type}", typeof(T).Name);
            return Task.FromResult<T?>(null);
        }

        _logger.LogDebug("캐시 히트: {Key}", key);
        return Task.FromResult<T?>((T)(object)entry.Value);
    }

    /// <inheritdoc/>
    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null) where T : class
    {
        if (!_isEnabled)
            return;

        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("캐시 키가 비어 있습니다.", nameof(key));

        ArgumentNullException.ThrowIfNull(value);

        if (typeof(T) != typeof(string))
        {
            _logger.LogWarning("FileCacheService는 string 타입만 지원합니다. 요청 타입: {Type}", typeof(T).Name);
            return;
        }

        var stringValue = (string)(object)value;

        // 원문 또는 번역 텍스트에 '=' 포함 시 첫 번째 '='만 구분자로 사용하므로 값 부분에는 허용
        DateTimeOffset? expireAt = expiration.HasValue
            ? DateTimeOffset.UtcNow.Add(expiration.Value)
            : null;

        _memoryCache[key] = (stringValue, expireAt);

        // 최대 항목 수 초과 시 가장 오래된 항목 제거 (간단한 FIFO 근사)
        if (_memoryCache.Count > _settings.General.MaxCacheItems)
        {
            var oldestKey = _memoryCache.Keys.FirstOrDefault();
            if (oldestKey is not null)
            {
                _memoryCache.TryRemove(oldestKey, out _);
                _logger.LogDebug("캐시 최대 항목 수 초과로 항목 제거: {Key}", oldestKey);
            }
        }

        _logger.LogDebug("캐시 항목 설정: {Key}", key);

        await PersistToCacheFileAsync().ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task RemoveAsync(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("캐시 키가 비어 있습니다.", nameof(key));

        _memoryCache.TryRemove(key, out _);
        _logger.LogDebug("캐시 항목 제거: {Key}", key);

        await PersistToCacheFileAsync().ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task ClearAsync()
    {
        _memoryCache.Clear();
        _logger.LogInformation("캐시 전체 비움.");

        await PersistToCacheFileAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// 메모리 캐시를 cache.txt 파일에 기록합니다.
    /// "원문=번역" 형식, 만료되지 않은 항목만 저장.
    /// </summary>
    private async Task PersistToCacheFileAsync()
    {
        await _fileLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(_cacheFilePath)!;
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var now = DateTimeOffset.UtcNow;
            var lines = _memoryCache
                .Where(kv => !kv.Value.ExpireAt.HasValue || kv.Value.ExpireAt.Value > now)
                .Select(kv => $"{kv.Key}={kv.Value.Value}")
                .ToList();

            await File.WriteAllLinesAsync(_cacheFilePath, lines).ConfigureAwait(false);
            _logger.LogDebug("캐시 파일 저장 완료: {Count}개 항목", lines.Count);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "캐시 파일 저장 중 IO 오류: {Path}", _cacheFilePath);
            throw;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _fileLock.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
