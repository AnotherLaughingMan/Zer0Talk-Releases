using System;
using System.Windows.Input;

namespace Zer0Talk.RelayServer.Utilities;

public sealed class RelayCommand : ICommand
{
    private readonly Action? _execute;
    private readonly Action<object?>? _executeWithParameter;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _executeWithParameter = null;
        _canExecute = canExecute;
    }

    public RelayCommand(Action<object?> execute, Func<bool>? canExecute = null)
    {
        _execute = null;
        _executeWithParameter = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter)
    {
        if (_executeWithParameter != null)
        {
            _executeWithParameter(parameter);
            return;
        }

        _execute?.Invoke();
    }

    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
