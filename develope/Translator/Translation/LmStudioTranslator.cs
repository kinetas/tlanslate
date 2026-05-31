using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Translator.Config;
using Translator.Core.Interfaces;
using Translator.Core.Models;

namespace Translator.Translation;

/// <summary>
/// LMStudio 로컬 서버(OpenAI 호환 API)를 사용하는 번역 엔진 구현체
/// LMStudio는 로컬 실행이므로 HTTP 허용
/// </summary>
public sealed class LmStudioTranslator : ITranslator, IDisposable
{
    private readonly ILogger<LmStudioTranslator> _logger;
    private readonly AppSettingsManager _settingsManager;
    private readonly HttpClient _httpClient;
    private bool _disposed;

    // OpenAI 호환 Chat Completions API 경로
    private const string ChatCompletionsPath = "/chat/completions";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// LmStudioTranslator 생성자
    /// </summary>
    public LmStudioTranslator(
        ILogger<LmStudioTranslator> logger,
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
        var lmStudioSettings = settings.TranslationApi.LmStudio;

        ValidateBaseUrl(lmStudioSettings.BaseUrl);

        var modelId = lmStudioSettings.ModelId;
        var requestUrl = $"{lmStudioSettings.BaseUrl.TrimEnd('/')}{ChatCompletionsPath}";

        _logger.LogInformation(
            "LMStudio 번역 요청: 모델={Model}, 목표언어={TargetLanguage}, 텍스트길이={Length}",
            modelId, targetLanguage, text.Length);

        var requestBody = BuildChatCompletionRequest(modelId, text, targetLanguage);
        var requestJson = JsonSerializer.Serialize(requestBody, JsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
        // LMStudio는 로컬 서버이므로 API Key가 없어도 동작하나, 설정에 있으면 Bearer 헤더로 전달
        if (!string.IsNullOrEmpty(settings.TranslationApi.EncryptedApiKey))
        {
            try
            {
                var apiKey = _settingsManager.DecryptApiKey(settings.TranslationApi.EncryptedApiKey);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LMStudio API 키 복호화 실패. API 키 없이 요청을 진행합니다.");
            }
        }

        request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "LMStudio API 네트워크 오류 발생. LMStudio가 실행 중인지 확인하세요: {BaseUrl}", lmStudioSettings.BaseUrl);
            throw;
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "LMStudio API 요청 타임아웃. 모델 응답이 지연되고 있습니다: {Model}", modelId);
            throw;
        }

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "LMStudio API 오류 응답: StatusCode={StatusCode}",
                (int)response.StatusCode);
            throw new HttpRequestException(
                $"LMStudio API 오류: HTTP {(int)response.StatusCode}",
                null,
                response.StatusCode);
        }

        var translatedText = ParseTranslatedText(responseJson);

        _logger.LogInformation(
            "LMStudio 번역 완료: 모델={Model}, 결과길이={Length}",
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
        // LMStudio는 범용 LLM 런타임이므로 로드된 모델에 따라 지원 언어가 달라짐
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
            throw new InvalidOperationException($"LMStudio BaseUrl이 유효한 URI가 아닙니다: {baseUrl}");

        if (!uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) &&
            !uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError("LMStudio BaseUrl 스킴이 올바르지 않습니다: {Scheme}", uri.Scheme);
            throw new InvalidOperationException($"LMStudio BaseUrl은 http 또는 https여야 합니다: '{uri.Scheme}'");
        }
    }

    /// <summary>
    /// OpenAI 호환 Chat Completions 요청 바디를 구성합니다.
    /// </summary>
    private static object BuildChatCompletionRequest(string modelId, string text, string targetLanguage)
    {
        return new
        {
            model = modelId,
            messages = new[]
            {
                new
                {
                    role = "system",
                    content = $"You are a professional translator. Translate the user's text into {targetLanguage}. " +
                              "Return ONLY the translated text without any explanation, notes, or additional content."
                },
                new
                {
                    role = "user",
                    content = text
                }
            },
            temperature = 0.1
        };
    }

    /// <summary>
    /// OpenAI 호환 Chat Completions 응답 JSON에서 번역 텍스트를 추출합니다.
    /// </summary>
    private string ParseTranslatedText(string responseJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrEmpty(content))
                throw new InvalidOperationException("LMStudio 응답에서 번역 텍스트를 찾을 수 없습니다.");

            return content.Trim();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "LMStudio 응답 JSON 파싱 실패");
            throw new InvalidOperationException("LMStudio 응답 파싱 중 오류가 발생했습니다.", ex);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogError(ex, "LMStudio 응답 구조가 예상과 다릅니다");
            throw new InvalidOperationException("LMStudio 응답에서 예상된 필드를 찾을 수 없습니다.", ex);
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
}
