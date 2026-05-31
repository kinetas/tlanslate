using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Translator.Config;
using Translator.Core.Interfaces;
using Translator.Core.Models;

namespace Translator.Translation;

/// <summary>
/// DeepL API를 사용하는 번역 엔진 구현체
/// </summary>
public sealed class DeepLTranslator : ITranslator, IDisposable
{
    private readonly ILogger<DeepLTranslator> _logger;
    private readonly AppSettingsManager _settingsManager;
    private readonly HttpClient _httpClient;
    private bool _disposed;

    // DeepL API는 HTTPS만 허용
    private const string RequiredScheme = "https";

    // DeepL 번역 API 경로
    private const string TranslatePath = "/translate";

    // DeepL이 지원하는 주요 언어 코드 목록
    private static readonly IReadOnlyList<string> SupportedLanguageCodes = new[]
    {
        "BG", "CS", "DA", "DE", "EL", "EN", "ES", "ET", "FI", "FR",
        "HU", "ID", "IT", "JA", "KO", "LT", "LV", "NB", "NL", "PL",
        "PT", "RO", "RU", "SK", "SL", "SV", "TR", "UK", "ZH"
    };

    /// <summary>
    /// DeepLTranslator 생성자
    /// </summary>
    public DeepLTranslator(
        ILogger<DeepLTranslator> logger,
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
        var deepLSettings = settings.TranslationApi.DeepL;

        EnforceHttpsBaseUrl(deepLSettings.BaseUrl);

        var apiKey = _settingsManager.DecryptApiKey(settings.TranslationApi.EncryptedApiKey);
        var requestUrl = $"{deepLSettings.BaseUrl.TrimEnd('/')}{TranslatePath}";
        var normalizedTargetLang = NormalizeLanguageCode(targetLanguage);

        _logger.LogInformation(
            "DeepL 번역 요청: 목표언어={TargetLanguage}, 텍스트길이={Length}",
            normalizedTargetLang, text.Length);

        // DeepL API는 form-urlencoded 형식 사용
        var formContent = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("text", text),
            new KeyValuePair<string, string>("target_lang", normalizedTargetLang)
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("DeepL-Auth-Key", apiKey);
        request.Content = formContent;

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "DeepL API 네트워크 오류 발생");
            throw;
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "DeepL API 요청 타임아웃");
            throw;
        }

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "DeepL API 오류 응답: StatusCode={StatusCode}",
                (int)response.StatusCode);
            throw new HttpRequestException(
                $"DeepL API 오류: HTTP {(int)response.StatusCode}",
                null,
                response.StatusCode);
        }

        var (translatedText, detectedSourceLang) = ParseTranslationResponse(responseJson);

        _logger.LogInformation(
            "DeepL 번역 완료: 감지언어={SourceLang}, 목표언어={TargetLang}, 결과길이={Length}",
            detectedSourceLang, normalizedTargetLang, translatedText.Length);

        return new TranslationResult(
            OriginalText: text,
            TranslatedText: translatedText,
            SourceLanguage: detectedSourceLang,
            TargetLanguage: normalizedTargetLang,
            TranslatedAt: DateTimeOffset.UtcNow);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> GetSupportedLanguagesAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(SupportedLanguageCodes);
    }

    /// <summary>
    /// BaseUrl이 HTTPS 스킴인지 검증합니다. 그렇지 않으면 예외를 던집니다.
    /// </summary>
    private void EnforceHttpsBaseUrl(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            throw new InvalidOperationException($"DeepL BaseUrl이 유효한 URI가 아닙니다: {baseUrl}");

        if (!uri.Scheme.Equals(RequiredScheme, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError(
                "DeepL API는 HTTPS가 필수입니다. 현재 스킴: {Scheme}", uri.Scheme);
            throw new InvalidOperationException(
                $"DeepL API는 HTTPS 연결만 허용됩니다. 현재 설정된 BaseUrl 스킴: '{uri.Scheme}'");
        }
    }

    /// <summary>
    /// 언어 코드를 DeepL 요구 형식(대문자)으로 정규화합니다.
    /// </summary>
    private static string NormalizeLanguageCode(string languageCode)
    {
        return languageCode.ToUpperInvariant() switch
        {
            "KO" => "KO",
            "EN" => "EN-US",
            "ZH" => "ZH-HANS",
            "PT" => "PT-BR",
            var code => code.ToUpperInvariant()
        };
    }

    /// <summary>
    /// DeepL API 응답 JSON에서 번역 결과와 감지된 원본 언어를 추출합니다.
    /// </summary>
    private (string TranslatedText, string DetectedSourceLanguage) ParseTranslationResponse(string responseJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var firstTranslation = doc.RootElement
                .GetProperty("translations")[0];

            var translatedText = firstTranslation.GetProperty("text").GetString();
            var detectedSourceLang = firstTranslation.GetProperty("detected_source_language").GetString();

            if (string.IsNullOrEmpty(translatedText))
                throw new InvalidOperationException("DeepL 응답에서 번역 텍스트를 찾을 수 없습니다.");

            return (translatedText.Trim(), detectedSourceLang ?? "unknown");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "DeepL 응답 JSON 파싱 실패");
            throw new InvalidOperationException("DeepL 응답 파싱 중 오류가 발생했습니다.", ex);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogError(ex, "DeepL 응답 구조가 예상과 다릅니다");
            throw new InvalidOperationException("DeepL 응답에서 예상된 필드를 찾을 수 없습니다.", ex);
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
