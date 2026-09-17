using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using AGR_PropManager.ViewModels.Windows;

namespace AGR_PropManager.Views
{
    /// <summary>
    /// Логика взаимодействия для TechProcessEditorWindow.xaml
    /// </summary>
    public partial class TechProcessEditorWindow : Window
    {
        public TechProcessEditorWindow()
        {
            InitializeComponent();
        }

        // Конструктор, принимающий ViewModel
        public TechProcessEditorWindow(TechProcessEditorViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            
            // Подписываемся на событие закрытия
            if (viewModel != null)
            {
                viewModel.CloseRequested += (s, e) => this.Close();
            }
        }
        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is TechProcessEditorViewModel vm)
            {
                await vm.InitializeAsync();
            }
        }
    }
}
