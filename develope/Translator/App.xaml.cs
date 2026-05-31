using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Translator.Config;
using Translator.Core.Interfaces;
using Translator.Core.Models;
using Translator.Core.Services;
using Translator.OCR;
using Translator.Overlay;
using Translator.Translation;
using Translator.UI;

namespace Translator;

/// <summary>
/// WPF 애플리케이션 진입점.
/// Microsoft.Extensions.DependencyInjection 기반 DI 컨테이너를 구성하고,
/// 설정값에 따라 ITranslator 팩토리를 등록합니다.
/// </summary>
public partial class App : Application
{
    private IServiceProvider? _serviceProvider;
    private ILogger<App>? _logger;

    /// <summary>
    /// 전체 애플리케이션에서 사용할 서비스 프로바이더 (읽기 전용 접근)
    /// </summary>
    public static IServiceProvider Services => ((App)Current)._serviceProvider
        ?? throw new InvalidOperationException("DI 컨테이너가 초기화되지 않았습니다.");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 처리되지 않은 예외 핸들러 등록
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            // 1단계: 설정을 먼저 로드하여 DI 팩토리 패턴에 사용
            var bootstrapServices = BuildBootstrapServices();
            var settingsManager = bootstrapServices.GetRequiredService<AppSettingsManager>();
            var settings = await settingsManager.LoadAsync().ConfigureAwait(false);

            // 2단계: 완전한 DI 컨테이너 구성
            var services = new ServiceCollection();
            ConfigureServices(services, settings);
            _serviceProvider = services.BuildServiceProvider();

            _logger = _serviceProvider.GetRequiredService<ILogger<App>>();
            _logger.LogInformation("Translator 앱 시작. 번역 엔진: {Provider}", settings.TranslationApi.Provider);

            // 3단계: 메인 창 표시
            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();

            if (settings.General.StartMinimized)
            {
                mainWindow.WindowState = WindowState.Minimized;
                if (settings.General.UseSystemTray)
                {
                    mainWindow.Hide();
                }
            }

            mainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"앱 초기화 중 오류가 발생했습니다:\n{ex.Message}",
                "초기화 오류",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.LogInformation("Translator 앱 종료. ExitCode={Code}", e.ApplicationExitCode);

        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }

        base.OnExit(e);
    }

    // ──────────────────────────────────────────────
    // DI 구성
    // ──────────────────────────────────────────────

    /// <summary>
    /// 설정 로드용 임시 부트스트랩 서비스 컨테이너.
    /// AppSettingsManager만 등록합니다.
    /// </summary>
    private static ServiceProvider BuildBootstrapServices()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddConsole();
            builder.AddDebug();
        });
        services.AddSingleton<AppSettingsManager>();
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// 전체 서비스를 DI 컨테이너에 등록합니다.
    /// </summary>
    private static void ConfigureServices(IServiceCollection services, AppSettings settings)
    {
        // ── 로깅 ──────────────────────────────────────────────────────────────
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddConsole();
            builder.AddDebug();
        });

        // ── 설정 (Singleton) ──────────────────────────────────────────────────
        services.AddSingleton(settings);
        services.AddSingleton(settings.TranslationApi);
        services.AddSingleton(settings.Ocr);
        services.AddSingleton(settings.Overlay);
        services.AddSingleton(settings.General);
        services.AddSingleton<AppSettingsManager>();

        // ── ITranslator 팩토리 패턴 (설정값 기반 런타임 선택) ────────────────
        services.AddTransient<OpenAiTranslator>();
        services.AddTransient<DeepLTranslator>();
        services.AddTransient<OllamaTranslator>();
        services.AddTransient<LmStudioTranslator>();

        services.AddSingleton<ITranslator>(sp =>
        {
            var provider = settings.TranslationApi.Provider?.Trim() ?? string.Empty;
            var logger = sp.GetRequiredService<ILogger<App>>();

            ITranslator translator = provider switch
            {
                "DeepL" => sp.GetRequiredService<DeepLTranslator>(),
                "Ollama" => sp.GetRequiredService<OllamaTranslator>(),
                "LMStudio" => sp.GetRequiredService<LmStudioTranslator>(),
                _ => sp.GetRequiredService<OpenAiTranslator>()  // 기본값: OpenAI
            };

            logger.LogInformation("ITranslator 팩토리: {Provider} → {Type}",
                provider, translator.GetType().Name);

            return translator;
        });

        // ── OCR / 화면 캡처 ───────────────────────────────────────────────────
        services.AddSingleton<IScreenCaptureService, ScreenCaptureService>();
        services.AddSingleton<IOcrProvider, PaddleOcrProvider>();
        services.AddSingleton<OCRManager>();

        // ── 캐시 ─────────────────────────────────────────────────────────────
        services.AddSingleton<ICacheService, FileCacheService>();

        // ── 오버레이 ──────────────────────────────────────────────────────────
        services.AddSingleton<OverlayWindow>();
        services.AddSingleton<OverlayRenderer>();
        services.AddSingleton<IOverlayRenderer>(sp => sp.GetRequiredService<OverlayRenderer>());
        services.AddSingleton<OverlayService>();

        // ── 번역 파이프라인 (Singleton) ────────────────────────────────────────
        services.AddSingleton<TranslationPipelineManager>();

        // ── UI ViewModel / Window ─────────────────────────────────────────────
        // IServiceProvider는 Microsoft.Extensions.DI 내부적으로 자동 등록되므로
        // MainViewModel 생성자에서 직접 주입받을 수 있습니다.
        services.AddSingleton<MainViewModel>();
        services.AddTransient<MainWindow>();

        services.AddTransient<RegionSelectorViewModel>();
        services.AddTransient<RegionSelectorWindow>();

        services.AddTransient<SettingsViewModel>();
        services.AddTransient<SettingsWindow>();
    }

    // ──────────────────────────────────────────────
    // 예외 핸들러
    // ──────────────────────────────────────────────

    private void OnDispatcherUnhandledException(
        object sender,
        System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.LogCritical(e.Exception, "처리되지 않은 Dispatcher 예외.");
        e.Handled = true;
        MessageBox.Show(
            $"예상치 못한 오류가 발생했습니다:\n{e.Exception.Message}",
            "오류",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            _logger?.LogCritical(ex, "처리되지 않은 AppDomain 예외. IsTerminating={IsTerminating}",
                e.IsTerminating);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "미관찰 Task 예외.");
        e.SetObserved();
    }
}
