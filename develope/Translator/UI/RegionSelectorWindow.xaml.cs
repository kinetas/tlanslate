using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Logging;

namespace Translator.UI;

/// <summary>
/// 전체화면 반투명 오버레이에서 마우스 드래그로 번역 영역을 선택하는 창.
/// 비즈니스 로직은 RegionSelectorViewModel에 위임하며,
/// 코드비하인드는 마우스 이벤트를 ViewModel에 전달하는 역할만 담당한다.
/// </summary>
public partial class RegionSelectorWindow : Window
{
    private readonly RegionSelectorViewModel _viewModel;

    /// <summary>
    /// RegionSelectorWindow 생성자
    /// </summary>
    /// <param name="viewModel">주입된 ViewModel</param>
    public RegionSelectorWindow(RegionSelectorViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;
        InitializeComponent();

        _viewModel.CloseRequested += OnCloseRequested;
    }

    /// <inheritdoc />
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var position = e.GetPosition(this);
        _viewModel.OnMouseDown(position.X, position.Y);
    }

    /// <inheritdoc />
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            var position = e.GetPosition(this);
            _viewModel.OnMouseMove(position.X, position.Y);
        }
    }

    /// <inheritdoc />
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var position = e.GetPosition(this);
        _viewModel.OnMouseUp(position.X, position.Y);
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
        base.OnClosed(e);
    }
}
