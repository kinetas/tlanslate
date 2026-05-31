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
/// OpenAI API를 사용하는 번역 엔진 구현체
/// </summary>
public sealed class OpenAiTranslator : ITranslator, IDisposable
{
    private readonly ILogger<OpenAiTranslator> _logger;
    private readonly AppSettingsManager _settingsManager;
    private readonly HttpClient _httpClient;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // OpenAI Chat Completions API는 HTTPS만 허용
    private const string RequiredScheme = "https";

    /// <summary>
    /// OpenAiTranslator 생성자
    /// </summary>
    public OpenAiTranslator(
        ILogger<OpenAiTranslator> logger,
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
        var openAiSettings = settings.TranslationApi.OpenAi;

        EnforceHttpsBaseUrl(openAiSettings.BaseUrl);

        var apiKey = _settingsManager.DecryptApiKey(settings.TranslationApi.EncryptedApiKey);
        var modelId = openAiSettings.ModelId;
        var requestUrl = $"{openAiSettings.BaseUrl.TrimEnd('/')}/chat/completions";

        _logger.LogInformation(
            "OpenAI 번역 요청: 모델={Model}, 목표언어={TargetLanguage}, 텍스트길이={Length}",
            modelId, targetLanguage, text.Length);

        var requestBody = BuildChatCompletionRequest(modelId, text, targetLanguage);
        var requestJson = JsonSerializer.Serialize(requestBody, JsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "OpenAI API 네트워크 오류 발생");
            throw;
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "OpenAI API 요청 타임아웃");
            throw;
        }

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "OpenAI API 오류 응답: StatusCode={StatusCode}",
                (int)response.StatusCode);
            throw new HttpRequestException(
                $"OpenAI API 오류: HTTP {(int)response.StatusCode}",
                null,
                response.StatusCode);
        }

        var translatedText = ParseTranslatedText(responseJson);

        _logger.LogInformation(
            "OpenAI 번역 완료: 모델={Model}, 결과길이={Length}",
            modelId, translatedText.Length);

        return new TranslationResult(
            OriginalText: text,
            TranslatedText: translatedText,
            SourceLanguage: "auto",
            TargetLanguage: targetLanguage,
            TranslatedAt: DateTimeOffset.UtcNow);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> TranslateBatchAsync(
        IReadOnlyList<string> texts,
        string targetLanguage,
        CancellationToken cancellationToken = default)
    {
        if (texts is null || texts.Count == 0)
            return Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(targetLanguage))
            throw new ArgumentException("목표 언어 코드가 비어 있습니다.", nameof(targetLanguage));

        const int chunkSize = 50;
        var results = new string[texts.Count];

        for (int offset = 0; offset < texts.Count; offset += chunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chunk = texts.Skip(offset).Take(chunkSize).ToList();
            var chunkResults = await TranslateBatchChunkAsync(chunk, targetLanguage, cancellationToken)
                .ConfigureAwait(false);

            for (int i = 0; i < chunkResults.Length; i++)
                results[offset + i] = chunkResults[i];
        }

        return results;
    }

    /// <summary>
    /// 최대 50개의 텍스트 청크를 번호|||텍스트 형식으로 일괄 번역합니다.
    /// </summary>
    private async Task<string[]> TranslateBatchChunkAsync(
        IReadOnlyList<string> chunk,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        var settings = await _settingsManager.LoadAsync().ConfigureAwait(false);
        var openAiSettings = settings.TranslationApi.OpenAi;

        EnforceHttpsBaseUrl(openAiSettings.BaseUrl);

        var apiKey = _settingsManager.DecryptApiKey(settings.TranslationApi.EncryptedApiKey);
        var modelId = openAiSettings.ModelId;
        var requestUrl = $"{openAiSettings.BaseUrl.TrimEnd('/')}/chat/completions";

        var userContent = string.Join("\n", chunk.Select((t, i) => $"{i + 1}|||{t}"));

        var requestBody = new
        {
            model = modelId,
            messages = new[]
            {
                new
                {
                    role = "system",
                    content = $"You are a professional translator. Translate each numbered text into {targetLanguage}. " +
                              "Respond with the same number of lines. Each line must follow the format: number|||translated text. " +
                              "Do not add any explanation or extra content."
                },
                new
                {
                    role = "user",
                    content = userContent
                }
            },
            temperature = 0.1
        };

        var requestJson = JsonSerializer.Serialize(requestBody, JsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "OpenAI 배치 번역 네트워크 오류");
            throw;
        }

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("OpenAI 배치 번역 API 오류: StatusCode={StatusCode}", (int)response.StatusCode);
            throw new HttpRequestException(
                $"OpenAI API 오류: HTTP {(int)response.StatusCode}", null, response.StatusCode);
        }

        var rawContent = ParseTranslatedText(responseJson);
        return ParseBatchResponse(rawContent, chunk);
    }

    /// <summary>
    /// "번호|||번역텍스트" 형식의 응답을 파싱합니다. 파싱 실패 항목은 원문으로 대체합니다.
    /// </summary>
    private static string[] ParseBatchResponse(string rawContent, IReadOnlyList<string> originalTexts)
    {
        var results = new string[originalTexts.Count];
        var lines = rawContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var parts = line.Split("|||", 2, StringSplitOptions.None);
            if (parts.Length == 2
                && int.TryParse(parts[0].Trim(), out int idx)
                && idx >= 1 && idx <= originalTexts.Count)
            {
                results[idx - 1] = parts[1].Trim();
            }
        }

        for (int i = 0; i < results.Length; i++)
        {
            if (string.IsNullOrEmpty(results[i]))
                results[i] = originalTexts[i];
        }

        return results;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> GetSupportedLanguagesAsync(
        CancellationToken cancellationToken = default)
    {
        // OpenAI는 범용 LLM이므로 주요 언어 코드를 정적으로 반환
        IReadOnlyList<string> languages = new[]
        {
            "ko", "en", "ja", "zh", "fr", "de", "es", "it", "pt", "ru",
            "ar", "hi", "th", "vi", "id", "tr", "pl", "nl", "sv", "da"
        };
        return Task.FromResult(languages);
    }

    /// <summary>
    /// BaseUrl이 HTTPS 스킴인지 검증합니다. 그렇지 않으면 예외를 던집니다.
    /// </summary>
    private void EnforceHttpsBaseUrl(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            throw new InvalidOperationException($"OpenAI BaseUrl이 유효한 URI가 아닙니다: {baseUrl}");

        if (!uri.Scheme.Equals(RequiredScheme, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError(
                "OpenAI API는 HTTPS가 필수입니다. 현재 스킴: {Scheme}", uri.Scheme);
            throw new InvalidOperationException(
                $"OpenAI API는 HTTPS 연결만 허용됩니다. 현재 설정된 BaseUrl 스킴: '{uri.Scheme}'");
        }
    }

    /// <summary>
    /// Chat Completions 요청 바디를 구성합니다.
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
    /// Chat Completions 응답 JSON에서 번역된 텍스트를 추출합니다.
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
                throw new InvalidOperationException("OpenAI 응답에서 번역 텍스트를 찾을 수 없습니다.");

            return content.Trim();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "OpenAI 응답 JSON 파싱 실패");
            throw new InvalidOperationException("OpenAI 응답 파싱 중 오류가 발생했습니다.", ex);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogError(ex, "OpenAI 응답 구조가 예상과 다릅니다");
            throw new InvalidOperationException("OpenAI 응답에서 예상된 필드를 찾을 수 없습니다.", ex);
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
