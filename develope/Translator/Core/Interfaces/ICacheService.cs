namespace Translator.Core.Interfaces;

/// <summary>
/// 번역 캐시 서비스 인터페이스
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// 캐시에서 값을 가져옵니다.
    /// </summary>
    /// <typeparam name="T">캐시 값 타입</typeparam>
    /// <param name="key">캐시 키</param>
    /// <returns>캐시된 값, 없으면 null</returns>
    Task<T?> GetAsync<T>(string key) where T : class;

    /// <summary>
    /// 캐시에 값을 저장합니다.
    /// </summary>
    /// <typeparam name="T">캐시 값 타입</typeparam>
    /// <param name="key">캐시 키</param>
    /// <param name="value">저장할 값</param>
    /// <param name="expiration">만료 시간 (null이면 만료 없음)</param>
    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null) where T : class;

    /// <summary>
    /// 캐시에서 특정 항목을 제거합니다.
    /// </summary>
    /// <param name="key">제거할 캐시 키</param>
    Task RemoveAsync(string key);

    /// <summary>
    /// 모든 캐시를 비웁니다.
    /// </summary>
    Task ClearAsync();
}
