using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Translator.Core.Models;

namespace Translator.UI;

/// <summary>
/// 전체화면 영역 선택 UI의 ViewModel.
/// 마우스 드래그로 Rectangle을 그리고, 마우스 업 시 Region을 확정하여
/// RegionSelected 이벤트로 반환한다. ESC 키로 취소한다.
/// </summary>
public sealed class RegionSelectorViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ILogger<RegionSelectorViewModel> _logger;

    private double _startX;
    private double _startY;
    private double _selectionLeft;
    private double _selectionTop;
    private double _selectionWidth;
    private double _selectionHeight;
    private bool _isSelecting;
    private bool _disposed;

    // ──────────────────────────────────────────────
    // 이벤트
    // ──────────────────────────────────────────────

    /// <summary>
    /// 영역이 확정되었을 때 발생한다. 취소 시에는 발생하지 않는다.
    /// </summary>
    public event EventHandler<Region>? RegionSelected;

    /// <summary>
    /// 창 닫기 요청 이벤트. bool? = DialogResult (true: 확정, null: 취소)
    /// </summary>
    public event EventHandler<bool?>? CloseRequested;

    // ──────────────────────────────────────────────
    // 바인딩 프로퍼티
    // ──────────────────────────────────────────────

    /// <summary>선택 Rectangle의 Canvas.Left 값</summary>
    public double SelectionLeft
    {
        get => _selectionLeft;
        private set => SetProperty(ref _selectionLeft, value);
    }

    /// <summary>선택 Rectangle의 Canvas.Top 값</summary>
    public double SelectionTop
    {
        get => _selectionTop;
        private set => SetProperty(ref _selectionTop, value);
    }

    /// <summary>선택 Rectangle의 Width 값</summary>
    public double SelectionWidth
    {
        get => _selectionWidth;
        private set => SetProperty(ref _selectionWidth, Math.Max(0, value));
    }

    /// <summary>선택 Rectangle의 Height 값</summary>
    public double SelectionHeight
    {
        get => _selectionHeight;
        private set => SetProperty(ref _selectionHeight, Math.Max(0, value));
    }

    /// <summary>드래그 중 여부 — Rectangle 및 좌표 레이블의 Visibility에 바인딩</summary>
    public bool IsSelecting
    {
        get => _isSelecting;
        private set => SetProperty(ref _isSelecting, value);
    }

    /// <summary>하단 좌표 표시 텍스트</summary>
    public string CoordinateDisplayText =>
        $"X: {(int)SelectionLeft}, Y: {(int)SelectionTop}  |  W: {(int)SelectionWidth}, H: {(int)SelectionHeight}";

    // ──────────────────────────────────────────────
    // 커맨드
    // ──────────────────────────────────────────────

    /// <summary>ESC 키 바인딩용 취소 커맨드</summary>
    public ICommand CancelCommand { get; }

    // ──────────────────────────────────────────────
    // 생성자
    // ──────────────────────────────────────────────

    /// <summary>
    /// RegionSelectorViewModel 생성자
    /// </summary>
    /// <param name="logger">로거</param>
    public RegionSelectorViewModel(ILogger<RegionSelectorViewModel> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        CancelCommand = new RelayCommand(Cancel);
    }

    // ──────────────────────────────────────────────
    // 마우스 이벤트 처리 (코드비하인드에서 호출)
    // ──────────────────────────────────────────────

    /// <summary>마우스 왼쪽 버튼 누름 — 드래그 시작</summary>
    public void OnMouseDown(double x, double y)
    {
        _startX = x;
        _startY = y;
        SelectionLeft = x;
        SelectionTop = y;
        SelectionWidth = 0;
        SelectionHeight = 0;
        IsSelecting = true;
        _logger.LogDebug("영역 선택 시작: ({X}, {Y})", x, y);
    }

    /// <summary>마우스 이동 — 드래그 중 Rectangle 갱신</summary>
    public void OnMouseMove(double x, double y)
    {
        if (!IsSelecting) return;

        SelectionLeft = Math.Min(_startX, x);
        SelectionTop = Math.Min(_startY, y);
        SelectionWidth = Math.Abs(x - _startX);
        SelectionHeight = Math.Abs(y - _startY);

        OnPropertyChanged(nameof(CoordinateDisplayText));
    }

    /// <summary>마우스 왼쪽 버튼 뗌 — 영역 확정 및 이벤트 발생</summary>
    public void OnMouseUp(double x, double y)
    {
        if (!IsSelecting) return;

        OnMouseMove(x, y); // 최종 위치 반영

        IsSelecting = false;

        var regionX = (int)SelectionLeft;
        var regionY = (int)SelectionTop;
        var regionWidth = (int)SelectionWidth;
        var regionHeight = (int)SelectionHeight;

        if (regionWidth <= 0 || regionHeight <= 0)
        {
            _logger.LogWarning("선택 영역이 너무 작습니다. 취소합니다. W={W}, H={H}", regionWidth, regionHeight);
            Cancel();
            return;
        }

        var region = new Region(regionX, regionY, regionWidth, regionHeight);
        _logger.LogInformation("영역 선택 완료: {Region}", region);

        RegionSelected?.Invoke(this, region);
        CloseRequested?.Invoke(this, true);
    }

    // ──────────────────────────────────────────────
    // 내부 메서드
    // ──────────────────────────────────────────────

    private void Cancel()
    {
        IsSelecting = false;
        _logger.LogInformation("영역 선택 취소.");
        CloseRequested?.Invoke(this, null);
    }

    // ──────────────────────────────────────────────
    // INotifyPropertyChanged
    // ──────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetProperty<T>(ref T backingField, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(backingField, value)) return false;
        backingField = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    // ──────────────────────────────────────────────
    // IDisposable
    // ──────────────────────────────────────────────

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
