using System.Drawing;
using Translator.Core.Models;

namespace Translator.Core.Interfaces;

/// <summary>
/// 화면 캡처 서비스 인터페이스
/// </summary>
public interface IScreenCaptureService
{
    /// <summary>
    /// 지정된 화면 영역을 캡처하여 Bitmap으로 반환합니다.
    /// 캡처된 이미지는 메모리에서만 처리되며 디스크에 저장되지 않습니다.
    /// </summary>
    /// <param name="region">캡처할 화면 영역</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>캡처된 Bitmap (호출자가 Dispose 책임)</returns>
    Task<Bitmap> CaptureRegionAsync(Region region, CancellationToken cancellationToken = default);
}
