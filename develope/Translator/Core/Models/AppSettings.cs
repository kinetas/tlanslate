namespace Translator.Core.Models;

/// <summary>
/// 애플리케이션 전체 설정을 나타내는 클래스
/// </summary>
public class AppSettings
{
    /// <summary>
    /// 번역 API 설정
    /// </summary>
    public TranslationApiSettings TranslationApi { get; set; } = new();

    /// <summary>
    /// OCR 설정
    /// </summary>
    public OcrSettings Ocr { get; set; } = new();

    /// <summary>
    /// 오버레이 표시 설정
    /// </summary>
    public OverlaySettings Overlay { get; set; } = new();

    /// <summary>
    /// 일반 애플리케이션 설정
    /// </summary>
    public GeneralSettings General { get; set; } = new();
}

/// <summary>
/// 번역 API 관련 설정
/// </summary>
public class TranslationApiSettings
{
    /// <summary>
    /// 사용할 번역 제공자 식별자 (예: "OpenAI", "DeepL", "Ollama", "LMStudio")
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// DPAPI로 암호화된 API 키 (Base64 인코딩)
    /// API Key 평문 저장 금지 — 반드시 AppSettingsManager를 통해 복호화 후 사용
    /// </summary>
    public string EncryptedApiKey { get; set; } = string.Empty;

    /// <summary>
    /// 번역 목표 언어 코드 (예: "ko", "en")
    /// </summary>
    public string TargetLanguage { get; set; } = "ko";

    /// <summary>
    /// OpenAI 번역 엔진 설정
    /// </summary>
    public OpenAiTranslationSettings OpenAi { get; set; } = new();

    /// <summary>
    /// DeepL 번역 엔진 설정
    /// </summary>
    public DeepLTranslationSettings DeepL { get; set; } = new();

    /// <summary>
    /// Ollama 번역 엔진 설정
    /// </summary>
    public OllamaTranslationSettings Ollama { get; set; } = new();

    /// <summary>
    /// LMStudio 번역 엔진 설정
    /// </summary>
    public LmStudioTranslationSettings LmStudio { get; set; } = new();
}

/// <summary>
/// OpenAI 번역 엔진 전용 설정
/// </summary>
public class OpenAiTranslationSettings
{
    /// <summary>
    /// 사용할 OpenAI 모델 ID (예: "gpt-4o-mini")
    /// </summary>
    public string ModelId { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// OpenAI API 베이스 URL
    /// </summary>
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
}

/// <summary>
/// DeepL 번역 엔진 전용 설정
/// </summary>
public class DeepLTranslationSettings
{
    /// <summary>
    /// DeepL API 베이스 URL (Free: api-free.deepl.com, Pro: api.deepl.com)
    /// </summary>
    public string BaseUrl { get; set; } = "https://api-free.deepl.com/v2";
}

/// <summary>
/// Ollama 번역 엔진 전용 설정
/// </summary>
public class OllamaTranslationSettings
{
    /// <summary>
    /// Ollama API 베이스 URL (로컬)
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>
    /// 사용할 Ollama 모델명 (예: "llama3", "mistral")
    /// </summary>
    public string ModelId { get; set; } = "llama3";
}

/// <summary>
/// LMStudio 번역 엔진 전용 설정
/// </summary>
public class LmStudioTranslationSettings
{
    /// <summary>
    /// LMStudio OpenAI 호환 API 베이스 URL (로컬)
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:1234/v1";

    /// <summary>
    /// 사용할 모델 ID (LMStudio에서 로드된 모델명)
    /// </summary>
    public string ModelId { get; set; } = "local-model";
}

/// <summary>
/// OCR 관련 설정
/// </summary>
public class OcrSettings
{
    /// <summary>
    /// 사용할 OCR 엔진 식별자 (예: "Tesseract", "WindowsOcr")
    /// </summary>
    public string Engine { get; set; } = "WindowsOcr";

    /// <summary>
    /// OCR 인식 언어 코드 (예: "en", "ja")
    /// </summary>
    public string RecognitionLanguage { get; set; } = "en";

    /// <summary>
    /// OCR 인식 최소 신뢰도 임계값 (0.0 ~ 1.0)
    /// </summary>
    public double MinConfidenceThreshold { get; set; } = 0.7;

    /// <summary>
    /// 자동 캡처 주기 (밀리초)
    /// </summary>
    public int CaptureIntervalMs { get; set; } = 1000;
}

/// <summary>
/// 오버레이 표시 관련 설정
/// </summary>
public class OverlaySettings
{
    /// <summary>
    /// 오버레이 배경 색상 (ARGB 16진수 문자열, 예: "#CC000000")
    /// </summary>
    public string BackgroundColor { get; set; } = "#CC000000";

    /// <summary>
    /// 오버레이 텍스트 색상 (ARGB 16진수 문자열, 예: "#FFFFFFFF")
    /// </summary>
    public string TextColor { get; set; } = "#FFFFFFFF";

    /// <summary>
    /// 오버레이 텍스트 폰트 크기 (포인트)
    /// </summary>
    public double FontSize { get; set; } = 14.0;

    /// <summary>
    /// 오버레이 투명도 (0.0 ~ 1.0)
    /// </summary>
    public double Opacity { get; set; } = 0.85;

    /// <summary>
    /// 오버레이 자동 숨김 지연 시간 (밀리초, 0이면 자동 숨김 없음)
    /// </summary>
    public int AutoHideDelayMs { get; set; } = 0;
}

/// <summary>
/// 일반 애플리케이션 설정
/// </summary>
public class GeneralSettings
{
    /// <summary>
    /// 애플리케이션 시작 시 최소화 여부
    /// </summary>
    public bool StartMinimized { get; set; } = false;

    /// <summary>
    /// 시스템 트레이 아이콘 사용 여부
    /// </summary>
    public bool UseSystemTray { get; set; } = true;

    /// <summary>
    /// 캐시 최대 항목 수
    /// </summary>
    public int MaxCacheItems { get; set; } = 1000;

    /// <summary>
    /// 캐시 항목 만료 시간 (분)
    /// </summary>
    public int CacheExpirationMinutes { get; set; } = 60;
}
