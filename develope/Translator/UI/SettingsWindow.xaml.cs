using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;

namespace Translator.UI;

/// <summary>
/// 애플리케이션 설정 창.
/// PasswordBox 이벤트를 ViewModel에 전달하는 역할만 담당하며,
/// 비즈니스 로직은 SettingsViewModel에 위임한다.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    /// <summary>
    /// SettingsWindow 생성자
    /// </summary>
    /// <param name="viewModel">주입된 ViewModel</param>
    public SettingsWindow(SettingsViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;
        InitializeComponent();

        _viewModel.CloseRequested += OnCloseRequested;
        Loaded += OnWindowLoaded;
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        // 기존 암호화된 API Key가 있을 때 PasswordBox에 placeholder 표시
        // 보안상 평문 복원은 하지 않음 — 마스크 처리만 표시
        if (_viewModel.HasExistingApiKey)
        {
            ApiKeyPasswordBox.Password = SettingsViewModel.ApiKeyPlaceholder;
        }
    }

    /// <summary>
    /// PasswordBox 비밀번호 변경 이벤트 핸들러.
    /// PasswordBox는 MVVM 바인딩 불가(평문 노출 위험)이므로
    /// 코드비하인드에서 ViewModel의 메서드를 직접 호출한다.
    /// </summary>
    private void ApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox)
        {
            _viewModel.OnApiKeyChanged(passwordBox.Password);
        }
    }

    private void OnCloseRequested(object? sender, bool? dialogResult)
    {
        DialogResult = dialogResult;
        Close();
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        _viewModel.CloseRequested -= OnCloseRequested;
        Loaded -= OnWindowLoaded;
        base.OnClosed(e);
    }
}
