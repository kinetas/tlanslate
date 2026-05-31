namespace Translator.Core.Models;

/// <summary>
/// 번역 결과를 나타내는 레코드
/// </summary>
/// <param name="OriginalText">원본 텍스트</param>
/// <param name="TranslatedText">번역된 텍스트</param>
/// <param name="SourceLanguage">감지된 원본 언어 코드</param>
/// <param name="TargetLanguage">목표 언어 코드</param>
/// <param name="TranslatedAt">번역 완료 시각</param>
/// <param name="IsFromCache">캐시에서 반환된 결과 여부</param>
public record TranslationResult(
    string OriginalText,
    string TranslatedText,
    string SourceLanguage,
    string TargetLanguage,
    DateTimeOffset TranslatedAt,
    bool IsFromCache = false);
