namespace Translator.Core.Models;

/// <summary>
/// OCR 인식 결과를 나타내는 레코드
/// </summary>
/// <param name="Text">인식된 텍스트</param>
/// <param name="Region">인식된 화면 영역</param>
/// <param name="Confidence">인식 신뢰도 (0.0 ~ 1.0)</param>
/// <param name="CapturedAt">캡처 시각</param>
public record OCRResult(
    string Text,
    Region Region,
    double Confidence,
    DateTimeOffset CapturedAt);
