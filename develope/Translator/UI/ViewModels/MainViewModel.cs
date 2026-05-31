using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Translator.Core.Models;
using Translator.Core.Services;
using Translator.UI;

namespace Translator.UI.ViewModels;

/// <summary>
/// 메인 창의 ViewModel.
/// TranslationPipelineManager를 주입받아 번역 시작/중지 등 핵심 커맨드를 제공합니다.
/// IsRunning, SelectedEngine, ActiveRegionsCount 등의 상태를 View에 바인딩합니다.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly TranslationPipelineManager _pipelineManager;
    private readonly AppSettings _settings;
    private readonly ILogger<MainViewModel> _logger;
    private readonly IServiceProvider _serviceProvider;

    private CancellationTokenSource? _translationCts;
    private readonly ObservableCollection<Region> _activeRegions = new();
    private bool _disposed;

    // ──────────────────────────────────────────────
    // 바인딩 프로퍼티
    // ──────────────────────────────────────────────

    private bool _isRunning;
    /// <summary>번역이 현재 실행 중인지 여부</summary>
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(StatusText));
                (StartTranslationCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (StopTranslationCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    private string _selectedEngine = "OpenAI";
    /// <summary>현재 선택된 번역 엔진 이름</summary>
    public string SelectedEngine
    {
        get => _selectedEngine;
        private set => SetProperty(ref _selectedEngine, value);
    }

    /// <summary>활성화된 번역 영역 수</summary>
    public int ActiveRegionsCount => _activeRegions.Count;

    /// <summary>상태 표시줄 텍스트</summary>
    public string StatusText => IsRunning ? "번역 중..." : "대기 중";

    /// <summary>활성 영역 목록 (읽기 전용 뷰)</summary>
    public IReadOnlyCollection<Region> ActiveRegions => _activeRegions;

    // ──────────────────────────────────────────────
    // 커맨드
    // ──────────────────────────────────────────────

    /// <summary>번역 시작 커맨드. 영역이 1개 이상 있고 실행 중이 아닐 때 활성화됩니다.</summary>
    public ICommand StartTranslationCommand { get; }

    /// <summary>번역 중지 커맨드. 실행 중일 때만 활성화됩니다.</summary>
    public ICommand StopTranslationCommand { get; }

    /// <summary>영역 선택 창 열기 커맨드</summary>
    public ICommand OpenRegionSelectorCommand { get; }

    /// <summary>설정 창 열기 커맨드</summary>
    public ICommand OpenSettingsCommand { get; }

    /// <summary>선택된 영역 초기화 커맨드</summary>
    public ICommand ClearRegionsCommand { get; }

    // ──────────────────────────────────────────────
    // 이벤트
    // ──────────────────────────────────────────────

    /// <summary>창 닫기 요청 이벤트</summary>
    public event EventHandler? CloseRequested;

    // ──────────────────────────────────────────────
    // 생성자
    // ──────────────────────────────────────────────

    public MainViewModel(
        TranslationPipelineManager pipelineManager,
        AppSettings settings,
        IServiceProvider serviceProvider,
        ILogger<MainViewModel> logger)
    {
        _pipelineManager = pipelineManager ?? throw new ArgumentNullException(nameof(pipelineManager));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // 설정에서 현재 엔진명 적용
        SelectedEngine = string.IsNullOrWhiteSpace(settings.TranslationApi.Provider)
            ? "OpenAI"
            : settings.TranslationApi.Provider;

        _activeRegions.CollectionChanged += (_, _) =>
            OnPropertyChanged(nameof(ActiveRegionsCount));

        StartTranslationCommand = new RelayCommand(
            execute: () => _ = StartTranslationAsync(),
            canExecute: () => !IsRunning && _activeRegions.Count > 0);

        StopTranslationCommand = new RelayCommand(
            execute: StopTranslation,
            canExecute: () => IsRunning);

        OpenRegionSelectorCommand = new RelayCommand(OpenRegionSelector);
        OpenSettingsCommand = new RelayCommand(OpenSettings);
        ClearRegionsCommand = new RelayCommand(ClearRegions, () => !IsRunning);
    }

    // ──────────────────────────────────────────────
    // 번역 시작 / 중지
    // ──────────────────────────────────────────────

    private async Task StartTranslationAsync()
    {
        if (IsRunning || _activeRegions.Count == 0)
            return;

        _translationCts = new CancellationTokenSource();
        IsRunning = true;

        _logger.LogInformation("번역 시작. 엔진={Engine}, 영역 수={Count}",
            SelectedEngine, _activeRegions.Count);

        try
        {
            var regions = _activeRegions.ToList();
            var token = _translationCts.Token;

            // 모든 영역을 설정된 간격으로 반복 처리
            while (!token.IsCancellationRequested)
            {
                await _pipelineManager.ProcessRegionsAsync(regions, token).ConfigureAwait(false);
                await Task.Delay(_settings.Ocr.CaptureIntervalMs, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("번역 루프 취소됨.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "번역 루프 중 예외 발생.");
        }
        finally
        {
            IsRunning = false;
            _translationCts?.Dispose();
            _translationCts = null;
            _pipelineManager.ClearOverlay();
            _logger.LogInformation("번역 중지 완료.");
        }
    }

    private void StopTranslation()
    {
        if (!IsRunning) return;
        _logger.LogInformation("번역 중지 요청.");
        _translationCts?.Cancel();
    }

    // ──────────────────────────────────────────────
    // 영역 선택
    // ──────────────────────────────────────────────

    private void OpenRegionSelector()
    {
        try
        {
            // RegionSelectorWindow 생성자가 ViewModel을 주입받으므로 DI가 자동으로 연결합니다.
            var window = _serviceProvider.GetRequiredService<RegionSelectorWindow>();
            var vm = (RegionSelectorViewModel)window.DataContext;

            vm.RegionSelected += OnRegionSelected;
            vm.CloseRequested += (_, _) =>
            {
                vm.RegionSelected -= OnRegionSelected;
            };

            window.ShowDialog();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "영역 선택 창 열기 실패.");
        }
    }

    private void OnRegionSelected(object? sender, Region region)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _activeRegions.Add(region);
            (StartTranslationCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ClearRegionsCommand as RelayCommand)?.RaiseCanExecuteChanged();
            _logger.LogInformation("영역 추가: ({X},{Y},{W},{H})",
                region.X, region.Y, region.Width, region.Height);
        });
    }

    // ──────────────────────────────────────────────
    // 설정
    // ──────────────────────────────────────────────

    private void OpenSettings()
    {
        try
        {
            // SettingsWindow 생성자가 SettingsViewModel을 주입받으므로 DI가 자동으로 연결합니다.
            var window = _serviceProvider.GetRequiredService<SettingsWindow>();
            var vm = (SettingsViewModel)window.DataContext;

            _ = vm.LoadAsync();

            vm.CloseRequested += (_, result) =>
            {
                if (result == true)
                {
                    // 엔진명 갱신 (재시작 없이 UI 반영)
                    SelectedEngine = _settings.TranslationApi.Provider is { Length: > 0 }
                        ? _settings.TranslationApi.Provider
                        : "OpenAI";
                    _logger.LogInformation("설정 변경 적용. 엔진={Engine}", SelectedEngine);
                }
            };

            window.ShowDialog();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "설정 창 열기 실패.");
        }
    }

    // ──────────────────────────────────────────────
    // 영역 초기화
    // ──────────────────────────────────────────────

    private void ClearRegions()
    {
        if (IsRunning) return;
        _activeRegions.Clear();
        _pipelineManager.ClearOverlay();
        (StartTranslationCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ClearRegionsCommand as RelayCommand)?.RaiseCanExecuteChanged();
        _logger.LogInformation("모든 영역이 초기화되었습니다.");
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
        if (_disposed) return;
        _disposed = true;

        _translationCts?.Cancel();
        _translationCts?.Dispose();
        _translationCts = null;

        GC.SuppressFinalize(this);
    }
}
