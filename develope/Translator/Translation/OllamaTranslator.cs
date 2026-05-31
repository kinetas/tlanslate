using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Translator.Config;
using Translator.Core.Interfaces;
using Translator.Core.Models;

namespace Translator.Translation;

/// <summary>
/// 로컬 Ollama 서버를 사용하는 번역 엔진 구현체
/// Ollama는 로컬 실행이므로 HTTP 허용
/// </summary>
public sealed class OllamaTranslator : ITranslator, IDisposable
{
    private readonly ILogger<OllamaTranslator> _logger;
    private readonly AppSettingsManager _settingsManager;
    private readonly HttpClient _httpClient;
    private bool _disposed;

    // Ollama Generate API 경로
    private const string GeneratePath = "/api/generate";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// OllamaTranslator 생성자
    /// </summary>
    public OllamaTranslator(
        ILogger<OllamaTranslator> logger,
        AppSettingsManager settingsManager,
        HttpClient httpClient)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// <inheritdoc/>
    public async Task<TranslationResult> TranslateAsync(
        string text,
        string targetLanguage,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("번역할 텍스트가 비어 있습니다.", nameof(text));
        if (string.IsNullOrWhiteSpace(targetLanguage))
            throw new ArgumentException("목표 언어 코드가 비어 있습니다.", nameof(targetLanguage));

        var settings = await _settingsManager.LoadAsync().ConfigureAwait(false);
        var ollamaSettings = settings.TranslationApi.Ollama;

        ValidateBaseUrl(ollamaSettings.BaseUrl);

        var modelId = ollamaSettings.ModelId;
        var requestUrl = $"{ollamaSettings.BaseUrl.TrimEnd('/')}{GeneratePath}";

        _logger.LogInformation(
            "Ollama 번역 요청: 모델={Model}, 목표언어={TargetLanguage}, 텍스트길이={Length}",
            modelId, targetLanguage, text.Length);

        var prompt = BuildTranslationPrompt(text, targetLanguage);
        var requestBody = new OllamaGenerateRequest
        {
            Model = modelId,
            Prompt = prompt,
            Stream = false
        };

        var requestJson = JsonSerializer.Serialize(requestBody, JsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
        request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Ollama API 네트워크 오류 발생. Ollama가 실행 중인지 확인하세요: {BaseUrl}", ollamaSettings.BaseUrl);
            throw;
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Ollama API 요청 타임아웃. 모델 응답이 지연되고 있습니다: {Model}", modelId);
            throw;
        }

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Ollama API 오류 응답: StatusCode={StatusCode}",
                (int)response.StatusCode);
            throw new HttpRequestException(
                $"Ollama API 오류: HTTP {(int)response.StatusCode}",
                null,
                response.StatusCode);
        }

        var translatedText = ParseTranslatedText(responseJson);

        _logger.LogInformation(
            "Ollama 번역 완료: 모델={Model}, 결과길이={Length}",
            modelId, translatedText.Length);

        return new TranslationResult(
            OriginalText: text,
            TranslatedText: translatedText,
            SourceLanguage: "auto",
            TargetLanguage: targetLanguage,
            TranslatedAt: DateTimeOffset.UtcNow);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> GetSupportedLanguagesAsync(
        CancellationToken cancellationToken = default)
    {
        // Ollama는 범용 LLM이므로 모델 능력에 따라 다양한 언어 지원
        IReadOnlyList<string> languages = new[]
        {
            "ko", "en", "ja", "zh", "fr", "de", "es", "it", "pt", "ru",
            "ar", "hi", "th", "vi", "id", "tr", "pl", "nl", "sv", "da"
        };
        return Task.FromResult(languages);
    }

    /// <summary>
    /// BaseUrl이 유효한 URI인지 검증합니다. 로컬이므로 HTTP도 허용합니다.
    /// </summary>
    private void ValidateBaseUrl(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            throw new InvalidOperationException($"Ollama BaseUrl이 유효한 URI가 아닙니다: {baseUrl}");

        if (!uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) &&
            !uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError("Ollama BaseUrl 스킴이 올바르지 않습니다: {Scheme}", uri.Scheme);
            throw new InvalidOperationException($"Ollama BaseUrl은 http 또는 https여야 합니다: '{uri.Scheme}'");
        }
    }

    /// <summary>
    /// 번역 프롬프트를 구성합니다.
    /// </summary>
    private static string BuildTranslationPrompt(string text, string targetLanguage)
    {
        return $"Translate the following text into {targetLanguage}. " +
               "Return ONLY the translated text without any explanation, notes, or additional content.\n\n" +
               $"Text to translate:\n{text}";
    }

    /// <summary>
    /// Ollama Generate API 응답에서 번역 텍스트를 추출합니다.
    /// </summary>
    private string ParseTranslatedText(string responseJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var responseText = doc.RootElement.GetProperty("response").GetString();

            if (string.IsNullOrEmpty(responseText))
                throw new InvalidOperationException("Ollama 응답에서 번역 텍스트를 찾을 수 없습니다.");

            return responseText.Trim();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Ollama 응답 JSON 파싱 실패");
            throw new InvalidOperationException("Ollama 응답 파싱 중 오류가 발생했습니다.", ex);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogError(ex, "Ollama 응답 구조가 예상과 다릅니다");
            throw new InvalidOperationException("Ollama 응답에서 'response' 필드를 찾을 수 없습니다.", ex);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Ollama Generate API 요청 바디 모델
    /// </summary>
    private sealed class OllamaGenerateRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("prompt")]
        public string Prompt { get; set; } = string.Empty;

        [JsonPropertyName("stream")]
        public bool Stream { get; set; } = false;
    }
}
