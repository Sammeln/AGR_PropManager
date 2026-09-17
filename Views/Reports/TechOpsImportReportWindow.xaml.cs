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
    /// Логика взаимодействия для TechOpsImportReportView.xaml
    /// </summary>
    public partial class TechOpsImportReportWindow : Window
    {
        public TechOpsImportReportWindow()
        {
            InitializeComponent();
        }

        public TechOpsImportReportWindow(TechOpsImportReportViewModel viewModel)
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
