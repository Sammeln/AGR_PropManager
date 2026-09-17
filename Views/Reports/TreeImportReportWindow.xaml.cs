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
using AGR_PropManager.ViewModels.Reports;

namespace AGR_PropManager.Views.Reports
{
    /// <summary>
    /// Логика взаимодействия для TreeImportReportWindow.xaml
    /// </summary>
    public partial class TreeImportReportWindow : Window
    {
        public TreeImportReportWindow()
        {
            InitializeComponent();
        }

        public TreeImportReportWindow(TreeImportReportViewModel viewModel)
        {
            InitializeComponent();

            DataContext = viewModel;

            // Подписываемся на событие закрытия
            if (viewModel != null)
            {
                viewModel.CloseRequested += (s, e) => this.Close();
            }
        }
    }
}
