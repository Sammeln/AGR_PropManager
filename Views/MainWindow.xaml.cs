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
using Microsoft.Extensions.Hosting;

namespace AGR_PropManager.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow(MainWindowViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            
            // Загружаем данные классификатора при инициализации окна
            if (viewModel != null)
            {
                _ = viewModel.LoadClassifierDataAsync();
            }
        }
    }
}