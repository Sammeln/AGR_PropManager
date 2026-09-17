using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using AGR_PropManager.ViewModels.Base;
using AGR_PropManager.ViewModels.Reports.Interfaces;

namespace AGR_PropManager.ViewModels.Reports
{
    public class AGR_BaseReport : BaseViewModel, IAGR_ReportViewModel
    {
        #region Properties

        #region HasErrors
        private bool _hasErrors = true;
        public bool HasErrors
        {
            get => _hasErrors;
            set => Set(ref _hasErrors, value);
        }
        #endregion

        #region Errors

        private string _errors;
        public string Errors
        {
            get => _errors;
            set => Set(ref _errors, value);
        }
        #endregion

        #region Property - HasWarnings
        private bool _HasWarnings;
        public bool HasWarnings
        {
            get => _HasWarnings;
            set => Set(ref _HasWarnings, value);
        }
        #endregion 

        #region Property - Warnings
        private string _Warnings;
        public string Warnings
        {
            get => _Warnings;
            set => Set(ref _Warnings, value);
        }
        #endregion 

        public ObservableCollection<IAGR_ReportItem> ReportData { get; set; }

        #region StatusMessage
        private string _statusMessage;
        public string StatusMessage
        {
            get => _statusMessage;
            set => Set(ref _statusMessage, value);
        } 
        #endregion

        #region IsGenerating
        private bool _isGenerating;
        public bool IsGenerating
        {
            get => _isGenerating;
            set => Set(ref _isGenerating, value);
        } 
        #endregion

        #endregion
        internal void CloseWindow()
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler? CloseRequested;
    }
}
