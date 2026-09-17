using AGR_PropManager.Infrastructure.Commands.Base;

namespace AGR_PropManager.Infrastructure.Commands
{
    public class RelayCommand : BaseCommand
    {
        private readonly Action<object> _Execute;
        private readonly Func<object, bool> _CanExecute;

        public RelayCommand(Action<object> Execute, Func<object, bool> CanExecute = null)
        {
            _Execute = Execute ?? throw new ArgumentNullException(nameof(Execute));
            _CanExecute = CanExecute;
        }
        public override bool CanExecute(object? parameter) => _CanExecute?.Invoke(parameter) ?? true;
        public override void Execute(object? parameter)
        {
            if (!CanExecute(parameter)) return;
            _Execute(parameter);
        }
    }
    public class RelayCommand<T> : BaseCommand
    {
        private readonly Action<T> _execute;
        private readonly Predicate<T> _canExecute;

        public RelayCommand(Action<T> execute, Predicate<T> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }
        public override bool CanExecute(object? parameter)
        {
            // Проверяем, соответствует ли тип параметра типу T
            if (parameter is T typedParam)
            {
                // Если параметр типа T, вызываем Predicate с ним
                return _canExecute?.Invoke(typedParam) ?? true;
            }
            return false;
        }
        public override void Execute(object? parameter)
        {
            if (!CanExecute(parameter)) return;

            if (parameter is T typedParam)
            {
                _execute(typedParam);
            }
        }
    }

}
