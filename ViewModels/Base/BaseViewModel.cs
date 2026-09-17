using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AGR_PropManager.ViewModels.Base
{
    public class BaseViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected virtual bool Set<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected virtual bool Set(Action action, [CallerMemberName] string propertyName = null)
        {
            action.Invoke();
            OnPropertyChanged(propertyName);
            return true;
        }


    }

}
