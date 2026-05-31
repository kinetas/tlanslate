using System.ComponentModel;
using System.Windows;
using Microsoft.Extensions.Logging;
using Translator.UI.ViewModels;

namespace Translator.UI;

/// <summary>
/// 메인 창 코드비하인드.
/// 비즈니스 로직은 MainViewModel에 위임하며, 여기서는 WPF 이벤트와 트레이 아이콘만 처리합니다.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ILogger<MainWindow> _logger;
    private bool _forceClose;

    public MainWindow(
        MainViewModel viewModel,
        ILogger<MainWindow> logger)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        InitializeComponent();
        DataContext = _viewModel;

        _logger.LogDebug("MainWindow 초기화 완료.");
    }

    // ──────────────────────────────────────────────
    // 창 닫기: 트레이로 최소화 vs 완전 종료
    // ──────────────────────────────────────────────

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        _logger.LogInformation("메인 창 닫힘.");
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        _logger.LogInformation("설정 버튼 Click 이벤트 발생. DataContext={Dc}", DataContext?.GetType().Name ?? "null");
    }

    // ──────────────────────────────────────────────
    // 헬퍼
    // ──────────────────────────────────────────────

    private void RestoreWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        _logger.LogDebug("메인 창 복원.");
    }
}
