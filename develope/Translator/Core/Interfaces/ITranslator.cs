using Translator.Core.Models;

namespace Translator.Core.Interfaces;

/// <summary>
/// 번역 서비스 인터페이스
/// </summary>
public interface ITranslator
{
    /// <summary>
    /// 텍스트를 지정한 언어로 번역합니다.
    /// </summary>
    /// <param name="text">번역할 원본 텍스트</param>
    /// <param name="targetLanguage">목표 언어 코드 (예: "ko", "en")</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>번역 결과</returns>
    Task<TranslationResult> TranslateAsync(
        string text,
        string targetLanguage,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 여러 텍스트를 한 번에 번역합니다. 입력 순서와 동일한 순서로 번역 결과를 반환합니다.
    /// 50개 이상의 텍스트는 내부적으로 청크로 나눠 처리됩니다.
    /// </summary>
    /// <param name="texts">번역할 텍스트 목록</param>
    /// <param name="targetLanguage">목표 언어 코드 (예: "ko", "en")</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>번역된 텍스트 목록 (입력과 동일한 순서 보장)</returns>
    Task<IReadOnlyList<string>> TranslateBatchAsync(
        IReadOnlyList<string> texts,
        string targetLanguage,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 지원하는 언어 목록을 반환합니다.
    /// </summary>
    Task<IReadOnlyList<string>> GetSupportedLanguagesAsync(
        CancellationToken cancellationToken = default);
}
