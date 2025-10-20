using System;
using System.Windows.Input;

namespace Agent.UI.Wpf.ViewModels
{
    /// <summary>
    /// 
    /// 	파라미터를 받는 ICommand 구현체입니다.
    /// 	View에서 SelectedItems 등을 CommandParameter로 전달할 때 사용합니다.
    /// </summary>
    public sealed class ParameterizedRelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        public ParameterizedRelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

        public void Execute(object? parameter) => _execute(parameter);

        public event EventHandler? CanExecuteChanged;
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
