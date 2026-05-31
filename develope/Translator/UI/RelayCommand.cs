using System.Windows.Input;

namespace Translator.UI;

/// <summary>
/// MVVM 패턴에서 커맨드를 위임하는 범용 ICommand 구현체.
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Func<Task> _executeAsync;
    private readonly Func<bool>? _canExecute;
    private bool _isExecuting;

    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged;

    /// <summary>
    /// 동기 액션으로 RelayCommand를 생성한다.
    /// </summary>
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(() => { execute(); return Task.CompletedTask; }, canExecute)
    {
    }

    /// <summary>
    /// 비동기 Func으로 RelayCommand를 생성한다.
    /// </summary>
    public RelayCommand(Func<Task> executeAsync, Func<bool>? canExecute = null)
    {
        _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
        _canExecute = canExecute;
    }

    /// <inheritdoc />
    public bool CanExecute(object? parameter)
        => !_isExecuting && (_canExecute?.Invoke() ?? true);

    /// <inheritdoc />
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;

        _isExecuting = true;
        RaiseCanExecuteChanged();

        try
        {
            await _executeAsync();
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    /// <summary>CanExecuteChanged 이벤트를 수동으로 발생시킨다.</summary>
    public void RaiseCanExecuteChanged()
        => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
