namespace Translator.Core.Models;

/// <summary>
/// 화면 오버레이에 표시할 번역 아이템
/// </summary>
/// <param name="Id">아이템 고유 식별자</param>
/// <param name="TranslatedText">표시할 번역 텍스트</param>
/// <param name="Region">표시 위치 및 크기</param>
/// <param name="CreatedAt">아이템 생성 시각</param>
public record OverlayItem(
    Guid Id,
    string TranslatedText,
    Region Region,
    DateTimeOffset CreatedAt)
{
    /// <summary>
    /// 새 고유 ID로 OverlayItem을 생성하는 팩토리 메서드
    /// </summary>
    public static OverlayItem Create(string translatedText, Region region)
        => new(Guid.NewGuid(), translatedText, region, DateTimeOffset.UtcNow);
}
