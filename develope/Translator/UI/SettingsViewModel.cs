using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using Translator.Config;
using Translator.Core.Models;

namespace Translator.UI;

/// <summary>
/// 설정 창의 ViewModel.
/// INotifyPropertyChanged를 구현하며, 저장 시 AppSettingsManager.SaveAsync()를 호출한다.
/// API Key는 DPAPI 암호화 후 저장한다 (평문 보관 금지).
/// </summary>
public sealed class SettingsViewModel : INotifyPropertyChanged, IDisposable
{
    /// <summary>기존 API Key가 있을 때 PasswordBox에 표시할 placeholder</summary>
    public const string ApiKeyPlaceholder = "••••••••••••••••";

    private readonly ILogger<SettingsViewModel> _logger;
    private readonly AppSettingsManager _settingsManager;

    // PasswordBox에서 입력받은 평문 API Key (메모리에만 유지, 직렬화 금지)
    private string? _pendingPlainApiKey;
    private bool _apiKeyChanged;

    private bool _disposed;

    // ──────────────────────────────────────────────
    // 이벤트
    // ──────────────────────────────────────────────

    /// <summary>창 닫기 요청. bool? = DialogResult (true: 저장, null: 취소)</summary>
    public event EventHandler<bool?>? CloseRequested;

    // ──────────────────────────────────────────────
    // 선택 목록
    // ──────────────────────────────────────────────

    public IReadOnlyList<string> AvailableProviders { get; } =
        new[] { "OpenAI", "DeepL", "Ollama", "LMStudio" };

    public IReadOnlyList<string> AvailableTargetLanguages { get; } =
        new[] { "ko", "en", "ja", "zh", "fr", "de", "es" };

    public IReadOnlyList<string> AvailableOcrEngines { get; } =
        new[] { "WindowsOcr", "Tesseract" };

    public IReadOnlyList<string> AvailableOcrLanguages { get; } =
        new[] { "en", "ja", "ko", "zh", "fr", "de" };

    // ──────────────────────────────────────────────
    // 번역 엔진 설정
    // ──────────────────────────────────────────────

    private string _selectedProvider = "OpenAI";
    public string SelectedProvider
    {
        get => _selectedProvider;
        set
        {
            if (SetProperty(ref _selectedProvider, value))
            {
                OnPropertyChanged(nameof(IsOpenAiSelected));
                OnPropertyChanged(nameof(IsDeepLSelected));
                OnPropertyChanged(nameof(IsOllamaSelected));
                OnPropertyChanged(nameof(IsLmStudioSelected));
            }
        }
    }

    public bool IsOpenAiSelected => SelectedProvider == "OpenAI";
    public bool IsDeepLSelected => SelectedProvider == "DeepL";
    public bool IsOllamaSelected => SelectedProvider == "Ollama";
    public bool IsLmStudioSelected => SelectedProvider == "LMStudio";

    private string _selectedTargetLanguage = "ko";
    public string SelectedTargetLanguage
    {
        get => _selectedTargetLanguage;
        set => SetProperty(ref _selectedTargetLanguage, value);
    }

    private string _openAiModelId = "gpt-4o-mini";
    public string OpenAiModelId
    {
        get => _openAiModelId;
        set => SetProperty(ref _openAiModelId, value);
    }

    private string _openAiBaseUrl = "https://api.openai.com/v1";
    public string OpenAiBaseUrl
    {
        get => _openAiBaseUrl;
        set => SetProperty(ref _openAiBaseUrl, value);
    }

    private string _deepLBaseUrl = "https://api-free.deepl.com/v2";
    public string DeepLBaseUrl
    {
        get => _deepLBaseUrl;
        set => SetProperty(ref _deepLBaseUrl, value);
    }

    private string _ollamaModelId = "llama3";
    public string OllamaModelId
    {
        get => _ollamaModelId;
        set => SetProperty(ref _ollamaModelId, value);
    }

    private string _ollamaBaseUrl = "http://localhost:11434";
    public string OllamaBaseUrl
    {
        get => _ollamaBaseUrl;
        set => SetProperty(ref _ollamaBaseUrl, value);
    }

    private string _lmStudioModelId = "local-model";
    public string LmStudioModelId
    {
        get => _lmStudioModelId;
        set => SetProperty(ref _lmStudioModelId, value);
    }

    private string _lmStudioBaseUrl = "http://localhost:1234/v1";
    public string LmStudioBaseUrl
    {
        get => _lmStudioBaseUrl;
        set => SetProperty(ref _lmStudioBaseUrl, value);
    }

    // ──────────────────────────────────────────────
    // API Key 상태
    // ──────────────────────────────────────────────

    private string _apiKeyStatusMessage = string.Empty;
    public string ApiKeyStatusMessage
    {
        get => _apiKeyStatusMessage;
        private set => SetProperty(ref _apiKeyStatusMessage, value);
    }

    private string _apiKeyStatusColor = "Gray";
    public string ApiKeyStatusColor
    {
        get => _apiKeyStatusColor;
        private set => SetProperty(ref _apiKeyStatusColor, value);
    }

    /// <summary>기존에 저장된 암호화 키가 있는지 여부</summary>
    public bool HasExistingApiKey { get; private set; }

    // ──────────────────────────────────────────────
    // OCR 설정
    // ──────────────────────────────────────────────

    private string _selectedOcrEngine = "WindowsOcr";
    public string SelectedOcrEngine
    {
        get => _selectedOcrEngine;
        set => SetProperty(ref _selectedOcrEngine, value);
    }

    private string _selectedOcrLanguage = "en";
    public string SelectedOcrLanguage
    {
        get => _selectedOcrLanguage;
        set => SetProperty(ref _selectedOcrLanguage, value);
    }

    private double _ocrMinConfidenceThreshold = 0.7;
    public double OcrMinConfidenceThreshold
    {
        get => _ocrMinConfidenceThreshold;
        set => SetProperty(ref _ocrMinConfidenceThreshold, Math.Clamp(value, 0.0, 1.0));
    }

    private int _ocrCaptureIntervalMs = 1000;
    public int OcrCaptureIntervalMs
    {
        get => _ocrCaptureIntervalMs;
        set => SetProperty(ref _ocrCaptureIntervalMs, Math.Max(100, value));
    }

    // ──────────────────────────────────────────────
    // 오버레이 표시 설정
    // ──────────────────────────────────────────────

    private double _overlayFontSize = 14.0;
    public double OverlayFontSize
    {
        get => _overlayFontSize;
        set => SetProperty(ref _overlayFontSize, Math.Clamp(value, 8, 36));
    }

    private double _overlayOpacity = 0.85;
    public double OverlayOpacity
    {
        get => _overlayOpacity;
        set => SetProperty(ref _overlayOpacity, Math.Clamp(value, 0.0, 1.0));
    }

    private string _overlayBackgroundColor = "#CC000000";
    public string OverlayBackgroundColor
    {
        get => _overlayBackgroundColor;
        set => SetProperty(ref _overlayBackgroundColor, value);
    }

    private string _overlayTextColor = "#FFFFFFFF";
    public string OverlayTextColor
    {
        get => _overlayTextColor;
        set => SetProperty(ref _overlayTextColor, value);
    }

    private int _overlayAutoHideDelayMs;
    public int OverlayAutoHideDelayMs
    {
        get => _overlayAutoHideDelayMs;
        set => SetProperty(ref _overlayAutoHideDelayMs, Math.Max(0, value));
    }

    // ──────────────────────────────────────────────
    // 캐시 설정
    // ──────────────────────────────────────────────

    private bool _isCacheEnabled = true;
    public bool IsCacheEnabled
    {
        get => _isCacheEnabled;
        set => SetProperty(ref _isCacheEnabled, value);
    }

    private int _maxCacheItems = 1000;
    public int MaxCacheItems
    {
        get => _maxCacheItems;
        set => SetProperty(ref _maxCacheItems, Math.Max(1, value));
    }

    private int _cacheExpirationMinutes = 60;
    public int CacheExpirationMinutes
    {
        get => _cacheExpirationMinutes;
        set => SetProperty(ref _cacheExpirationMinutes, Math.Max(1, value));
    }

    // ──────────────────────────────────────────────
    // 커맨드
    // ──────────────────────────────────────────────

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    // ──────────────────────────────────────────────
    // 생성자
    // ──────────────────────────────────────────────

    /// <summary>
    /// SettingsViewModel 생성자
    /// </summary>
    /// <param name="logger">로거</param>
    /// <param name="settingsManager">DPAPI 암호화/복호화 및 저장 담당</param>
    public SettingsViewModel(
        ILogger<SettingsViewModel> logger,
        AppSettingsManager settingsManager)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));

        SaveCommand = new RelayCommand(async () => await SaveAsync(), () => !_isSaving);
        CancelCommand = new RelayCommand(Cancel);
    }

    // ──────────────────────────────────────────────
    // 초기화 (설정 로드)
    // ──────────────────────────────────────────────

    /// <summary>
    /// 저장된 설정을 비동기로 로드하여 ViewModel 프로퍼티에 바인딩한다.
    /// </summary>
    public async Task LoadAsync()
    {
        try
        {
            var settings = await _settingsManager.LoadAsync().ConfigureAwait(false);
            ApplySettings(settings);
            _logger.LogInformation("설정 로드 완료.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "설정 로드 중 오류 발생. 기본값을 사용합니다.");
        }
    }

    private void ApplySettings(AppSettings settings)
    {
        SelectedProvider = settings.TranslationApi.Provider is { Length: > 0 }
            ? settings.TranslationApi.Provider
            : "OpenAI";

        SelectedTargetLanguage = settings.TranslationApi.TargetLanguage;

        OpenAiModelId = settings.TranslationApi.OpenAi.ModelId;
        OpenAiBaseUrl = settings.TranslationApi.OpenAi.BaseUrl;
        DeepLBaseUrl = settings.TranslationApi.DeepL.BaseUrl;
        OllamaModelId = settings.TranslationApi.Ollama.ModelId;
        OllamaBaseUrl = settings.TranslationApi.Ollama.BaseUrl;
        LmStudioModelId = settings.TranslationApi.LmStudio.ModelId;
        LmStudioBaseUrl = settings.TranslationApi.LmStudio.BaseUrl;

        HasExistingApiKey = !string.IsNullOrEmpty(settings.TranslationApi.EncryptedApiKey);
        if (HasExistingApiKey)
        {
            ApiKeyStatusMessage = "기존 API Key가 저장되어 있습니다.";
            ApiKeyStatusColor = "Green";
        }

        SelectedOcrEngine = settings.Ocr.Engine;
        SelectedOcrLanguage = settings.Ocr.RecognitionLanguage;
        OcrMinConfidenceThreshold = settings.Ocr.MinConfidenceThreshold;
        OcrCaptureIntervalMs = settings.Ocr.CaptureIntervalMs;

        OverlayFontSize = settings.Overlay.FontSize;
        OverlayOpacity = settings.Overlay.Opacity;
        OverlayBackgroundColor = settings.Overlay.BackgroundColor;
        OverlayTextColor = settings.Overlay.TextColor;
        OverlayAutoHideDelayMs = settings.Overlay.AutoHideDelayMs;

        IsCacheEnabled = settings.General.MaxCacheItems > 0;
        MaxCacheItems = settings.General.MaxCacheItems;
        CacheExpirationMinutes = settings.General.CacheExpirationMinutes;
    }

    // ──────────────────────────────────────────────
    // PasswordBox 연동 (코드비하인드에서 호출)
    // ──────────────────────────────────────────────

    /// <summary>
    /// PasswordBox.PasswordChanged 이벤트 시 코드비하인드에서 호출한다.
    /// 평문은 이 메서드에서만 잠시 보관하고, 저장 시 암호화 후 즉시 파기한다.
    /// </summary>
    /// <param name="plainPassword">PasswordBox에서 전달받은 평문 비밀번호</param>
    public void OnApiKeyChanged(string plainPassword)
    {
        // placeholder는 변경으로 처리하지 않음
        if (plainPassword == ApiKeyPlaceholder) return;

        _pendingPlainApiKey = plainPassword;
        _apiKeyChanged = true;

        ApiKeyStatusMessage = string.IsNullOrWhiteSpace(plainPassword)
            ? "API Key가 비어 있습니다."
            : "API Key가 입력되었습니다. 저장 시 암호화됩니다.";
        ApiKeyStatusColor = string.IsNullOrWhiteSpace(plainPassword) ? "Orange" : "Blue";
    }

    // ──────────────────────────────────────────────
    // 저장 / 취소
    // ──────────────────────────────────────────────

    private bool _isSaving;

    private async Task SaveAsync()
    {
        if (_isSaving) return;
        _isSaving = true;
        (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();

        try
        {
            var settings = await _settingsManager.LoadAsync().ConfigureAwait(false);
            ApplyToSettings(settings);

            if (_apiKeyChanged && !string.IsNullOrWhiteSpace(_pendingPlainApiKey))
            {
                _settingsManager.WithEncryptedApiKey(settings, _pendingPlainApiKey);
                _logger.LogInformation("API Key DPAPI 암호화 완료.");
            }
            else if (_apiKeyChanged && string.IsNullOrWhiteSpace(_pendingPlainApiKey))
            {
                // 빈 값 입력 시 기존 암호화 키 제거
                settings.TranslationApi.EncryptedApiKey = string.Empty;
                _logger.LogWarning("API Key가 비워졌습니다. 암호화 키를 삭제합니다.");
            }

            await _settingsManager.SaveAsync(settings).ConfigureAwait(false);

            // 메모리에서 평문 즉시 파기
            _pendingPlainApiKey = null;
            _apiKeyChanged = false;

            ApiKeyStatusMessage = "저장 완료.";
            ApiKeyStatusColor = "Green";

            _logger.LogInformation("설정 저장 완료.");
            CloseRequested?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "설정 저장 중 오류 발생.");
            ApiKeyStatusMessage = $"저장 실패: {ex.Message}";
            ApiKeyStatusColor = "Red";
        }
        finally
        {
            _isSaving = false;
            (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    private void ApplyToSettings(AppSettings settings)
    {
        settings.TranslationApi.Provider = SelectedProvider;
        settings.TranslationApi.TargetLanguage = SelectedTargetLanguage;
        settings.TranslationApi.OpenAi.ModelId = OpenAiModelId;
        settings.TranslationApi.OpenAi.BaseUrl = OpenAiBaseUrl;
        settings.TranslationApi.DeepL.BaseUrl = DeepLBaseUrl;
        settings.TranslationApi.Ollama.ModelId = OllamaModelId;
        settings.TranslationApi.Ollama.BaseUrl = OllamaBaseUrl;
        settings.TranslationApi.LmStudio.ModelId = LmStudioModelId;
        settings.TranslationApi.LmStudio.BaseUrl = LmStudioBaseUrl;

        settings.Ocr.Engine = SelectedOcrEngine;
        settings.Ocr.RecognitionLanguage = SelectedOcrLanguage;
        settings.Ocr.MinConfidenceThreshold = OcrMinConfidenceThreshold;
        settings.Ocr.CaptureIntervalMs = OcrCaptureIntervalMs;

        settings.Overlay.FontSize = OverlayFontSize;
        settings.Overlay.Opacity = OverlayOpacity;
        settings.Overlay.BackgroundColor = OverlayBackgroundColor;
        settings.Overlay.TextColor = OverlayTextColor;
        settings.Overlay.AutoHideDelayMs = OverlayAutoHideDelayMs;

        settings.General.MaxCacheItems = IsCacheEnabled ? MaxCacheItems : 0;
        settings.General.CacheExpirationMinutes = CacheExpirationMinutes;
    }

    private void Cancel()
    {
        _pendingPlainApiKey = null;
        _apiKeyChanged = false;
        _logger.LogInformation("설정 편집 취소.");
        CloseRequested?.Invoke(this, null);
    }

    // ──────────────────────────────────────────────
    // INotifyPropertyChanged
    // ──────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetProperty<T>(ref T backingField, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(backingField, value)) return false;
        backingField = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    // ──────────────────────────────────────────────
    // IDisposable
    // ──────────────────────────────────────────────

    public void Dispose()
    {
        if (!_disposed)
        {
            _pendingPlainApiKey = null;
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
