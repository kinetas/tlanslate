using Microsoft.Extensions.Logging;
using Translator.Config;
using Translator.Core.Interfaces;
using Translator.Core.Models;

namespace Translator.Translation;

/// <summary>
/// 현재 설정의 Provider 값을 읽어 적절한 번역 엔진에 위임하는 동적 래퍼.
/// 앱 재시작 없이 설정 창에서 엔진을 바꾸면 즉시 반영된다.
/// </summary>
public sealed class TranslatorSelector : ITranslator
{
    private readonly AppSettingsManager _settingsManager;
    private readonly OpenAiTranslator _openAi;
    private readonly DeepLTranslator _deepL;
    private readonly OllamaTranslator _ollama;
    private readonly LmStudioTranslator _lmStudio;
    private readonly ILogger<TranslatorSelector> _logger;

    public TranslatorSelector(
        AppSettingsManager settingsManager,
        OpenAiTranslator openAi,
        DeepLTranslator deepL,
        OllamaTranslator ollama,
        LmStudioTranslator lmStudio,
        ILogger<TranslatorSelector> logger)
    {
        _settingsManager = settingsManager;
        _openAi = openAi;
        _deepL = deepL;
        _ollama = ollama;
        _lmStudio = lmStudio;
        _logger = logger;
    }

    public async Task<TranslationResult> TranslateAsync(
        string text,
        string targetLanguage,
        CancellationToken cancellationToken = default)
    {
        var target = await SelectAsync(cancellationToken);
        _logger.LogDebug("번역 엔진 선택: {Type}", target.GetType().Name);
        return await target.TranslateAsync(text, targetLanguage, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> TranslateBatchAsync(
        IReadOnlyList<string> texts,
        string targetLanguage,
        CancellationToken cancellationToken = default)
    {
        var target = await SelectAsync(cancellationToken);
        _logger.LogDebug("배치 번역 엔진 선택: {Type}, 텍스트 수={Count}", target.GetType().Name, texts?.Count ?? 0);
        return await target.TranslateBatchAsync(texts!, targetLanguage, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetSupportedLanguagesAsync(
        CancellationToken cancellationToken = default)
    {
        var target = await SelectAsync(cancellationToken);
        return await target.GetSupportedLanguagesAsync(cancellationToken);
    }

    private async Task<ITranslator> SelectAsync(CancellationToken ct)
    {
        var settings = await _settingsManager.LoadAsync().ConfigureAwait(false);
        var provider = settings.TranslationApi.Provider?.Trim() ?? string.Empty;
        return provider switch
        {
            "DeepL"    => _deepL,
            "Ollama"   => _ollama,
            "LMStudio" => _lmStudio,
            _          => _openAi
        };
    }
}
