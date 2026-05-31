using Translator.Core.Models;

namespace Translator.Core.Interfaces;

/// <summary>
/// OCR 제공자 인터페이스
/// </summary>
public interface IOcrProvider
{
    /// <summary>
    /// 지정된 화면 영역에서 텍스트를 인식합니다.
    /// </summary>
    /// <param name="region">캡처할 화면 영역</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>OCR 인식 결과</returns>
    Task<OCRResult> RecognizeAsync(
        Region region,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 전체 화면에서 텍스트를 인식합니다.
    /// </summary>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>OCR 인식 결과 목록</returns>
    Task<IReadOnlyList<OCRResult>> RecognizeAllAsync(
        CancellationToken cancellationToken = default);
}
