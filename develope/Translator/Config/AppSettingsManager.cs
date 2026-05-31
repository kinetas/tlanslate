using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Translator.Core.Models;

namespace Translator.Config;

/// <summary>
/// AppSettings의 로드/저장 및 API Key DPAPI 암호화/복호화를 담당하는 관리자 클래스
/// </summary>
public class AppSettingsManager : IDisposable
{
    private readonly ILogger<AppSettingsManager> _logger;
    private readonly string _settingsFilePath;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// AppSettingsManager 생성자
    /// 설정 파일 경로: %APPDATA%\Translator\appsettings.json
    /// </summary>
    public AppSettingsManager(ILogger<AppSettingsManager> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appFolder = Path.Combine(appDataPath, "Translator");
        _settingsFilePath = Path.Combine(appFolder, "appsettings.json");
    }

    /// <summary>
    /// 설정 파일을 로드합니다. 파일이 없으면 기본값으로 생성합니다.
    /// </summary>
    /// <returns>로드된 AppSettings</returns>
    public async Task<AppSettings> LoadAsync()
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsFilePath)!;
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                _logger.LogInformation("설정 디렉토리 생성: {Directory}", directory);
            }

            if (!File.Exists(_settingsFilePath))
            {
                _logger.LogInformation("설정 파일이 없어 기본값으로 초기화합니다: {Path}", _settingsFilePath);
                var defaultSettings = new AppSettings();
                await SaveAsync(defaultSettings).ConfigureAwait(false);
                return defaultSettings;
            }

            await using var stream = File.OpenRead(_settingsFilePath);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions).ConfigureAwait(false);

            if (settings is null)
            {
                _logger.LogWarning("설정 파일 역직렬화 결과가 null입니다. 기본값을 사용합니다.");
                return new AppSettings();
            }

            _logger.LogInformation("설정 파일 로드 완료: {Path}", _settingsFilePath);
            return settings;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "설정 파일 JSON 파싱 오류. 기본값을 사용합니다: {Path}", _settingsFilePath);
            return new AppSettings();
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "설정 파일 읽기 오류: {Path}", _settingsFilePath);
            throw;
        }
    }

    /// <summary>
    /// 설정을 파일에 저장합니다.
    /// </summary>
    /// <param name="settings">저장할 AppSettings</param>
    public async Task SaveAsync(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            var directory = Path.GetDirectoryName(_settingsFilePath)!;
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var stream = File.Open(_settingsFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions).ConfigureAwait(false);
            _logger.LogInformation("설정 파일 저장 완료: {Path}", _settingsFilePath);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "설정 파일 저장 오류: {Path}", _settingsFilePath);
            throw;
        }
    }

    /// <summary>
    /// API Key를 Windows DPAPI(ProtectedData)로 암호화합니다.
    /// 현재 사용자 계정에 바인딩된 암호화를 사용합니다.
    /// </summary>
    /// <param name="plainApiKey">평문 API 키</param>
    /// <returns>Base64 인코딩된 암호화 데이터</returns>
    public string EncryptApiKey(string plainApiKey)
    {
        if (string.IsNullOrEmpty(plainApiKey))
            throw new ArgumentException("API 키가 비어 있습니다.", nameof(plainApiKey));

        var plainBytes = Encoding.UTF8.GetBytes(plainApiKey);
        var encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encryptedBytes);
    }

    /// <summary>
    /// DPAPI로 암호화된 API Key를 복호화합니다.
    /// </summary>
    /// <param name="encryptedApiKey">Base64 인코딩된 암호화 데이터</param>
    /// <returns>평문 API 키</returns>
    public string DecryptApiKey(string encryptedApiKey)
    {
        if (string.IsNullOrEmpty(encryptedApiKey))
            throw new ArgumentException("암호화된 API 키가 비어 있습니다.", nameof(encryptedApiKey));

        try
        {
            var encryptedBytes = Convert.FromBase64String(encryptedApiKey);
            var decryptedBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decryptedBytes);
        }
        catch (CryptographicException ex)
        {
            _logger.LogError(ex, "API 키 복호화 실패. 키가 손상되었거나 다른 사용자 계정으로 암호화된 데이터입니다.");
            throw;
        }
        catch (FormatException ex)
        {
            _logger.LogError(ex, "암호화된 API 키의 Base64 형식이 올바르지 않습니다.");
            throw;
        }
    }

    /// <summary>
    /// 설정의 API 키를 암호화하여 저장합니다.
    /// </summary>
    /// <param name="settings">현재 설정</param>
    /// <param name="plainApiKey">저장할 평문 API 키</param>
    /// <returns>API 키가 암호화된 새 설정 객체</returns>
    public AppSettings WithEncryptedApiKey(AppSettings settings, string plainApiKey)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var encrypted = EncryptApiKey(plainApiKey);
        settings.TranslationApi.EncryptedApiKey = encrypted;
        _logger.LogInformation("API 키 암호화 완료 (DPAPI CurrentUser 범위).");
        return settings;
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
