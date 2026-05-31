namespace Translator.Core.Models;

/// <summary>
/// 화면 상의 직사각형 영역을 나타내는 레코드
/// </summary>
/// <param name="X">영역 좌상단 X 좌표 (픽셀)</param>
/// <param name="Y">영역 좌상단 Y 좌표 (픽셀)</param>
/// <param name="Width">영역 너비 (픽셀)</param>
/// <param name="Height">영역 높이 (픽셀)</param>
public record Region(int X, int Y, int Width, int Height)
{
    /// <summary>
    /// 영역의 오른쪽 끝 X 좌표
    /// </summary>
    public int Right => X + Width;

    /// <summary>
    /// 영역의 아래쪽 끝 Y 좌표
    /// </summary>
    public int Bottom => Y + Height;

    /// <summary>
    /// 영역이 비어 있는지 여부
    /// </summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;
}
