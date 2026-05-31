using Translator.Core.Models;

namespace Translator.Core.Interfaces;

/// <summary>
/// 오버레이 렌더링 서비스 인터페이스
/// </summary>
public interface IOverlayRenderer
{
    /// <summary>
    /// 오버레이 아이템을 화면에 표시합니다.
    /// </summary>
    /// <param name="item">표시할 오버레이 아이템</param>
    void ShowOverlay(OverlayItem item);

    /// <summary>
    /// 특정 오버레이 아이템을 숨깁니다.
    /// </summary>
    /// <param name="itemId">숨길 아이템의 고유 식별자</param>
    void HideOverlay(Guid itemId);

    /// <summary>
    /// 모든 오버레이를 화면에서 제거합니다.
    /// </summary>
    void ClearAllOverlays();

    /// <summary>
    /// 현재 표시 중인 오버레이 아이템 목록을 반환합니다.
    /// </summary>
    IReadOnlyList<OverlayItem> GetActiveOverlays();

    /// <summary>
    /// OCR 결과와 번역 텍스트 쌍을 일괄 렌더링합니다.
    /// </summary>
    void RenderTranslations(IReadOnlyList<(OCRResult Source, string Translation)> items);
}
