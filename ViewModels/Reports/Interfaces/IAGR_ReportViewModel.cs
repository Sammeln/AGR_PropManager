// File: ViewModels/Reports/TreeImportReportViewModel.cs
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace AGR_PropManager.ViewModels.Reports.Interfaces
{
    public interface IAGR_ReportViewModel
    {
        string Errors { get; set; }
        string Warnings { get; set; }
        bool HasErrors { get; set; }
        bool HasWarnings { get; set; }
        bool IsGenerating { get; set; }
        string StatusMessage { get; set; }
        ObservableCollection<IAGR_ReportItem> ReportData { get; }
        event EventHandler? CloseRequested;
        //ICommand ExportToExcelCommand { get; }
        //internal void Validate();
    }
}