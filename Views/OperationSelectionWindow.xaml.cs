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
using NPOI.HSSF.Record.Chart;

namespace AGR_PropManager.Views
{
    /// <summary>
    /// Логика взаимодействия для OperationSelectionWindow.xaml
    /// </summary>
    public partial class OperationSelectionWindow : Window
    {
        public OperationSelectionWindow()
        {
            InitializeComponent();
        }
        // Конструктор, принимающий ViewModel
        public OperationSelectionWindow(OperationSelectionViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            // Подписываемся на событие закрытия
            if (viewModel != null)
            {
                viewModel.CloseRequested += (s, e) => this.Close();
            }
            this.ShowDialog();
        }

    }

}
