using System.Collections.ObjectModel;
using AGR_PropManager.ViewModels.Base;
using AgroventInfrastructure.Entities.TechProcess;

namespace AGR_PropManager.ViewModels.TechProcess
{
    public class TechProcessViewModel : BaseViewModel
    {
        private TechnologicalProcess m_techProcess;

        public TechProcessViewModel()
        {
                
        }

        public TechProcessViewModel(TechnologicalProcess technologicalProcess)
        {
            m_techProcess = technologicalProcess;

            PartNumber = technologicalProcess.PartNumber;
            Operations = new ObservableCollection<TechOperationViewModel>(
                technologicalProcess.Operations.Select( op =>
                    new TechOperationViewModel( op ))
                );
        }

        #region PROPS
        #region Property - PartNumber
        private string _PartNumber;
        public string PartNumber
        {
            get => _PartNumber;
            set => Set(ref _PartNumber, value);
        }
        #endregion

        #region Property - Operations
        private ObservableCollection<TechOperationViewModel> _Operations = new();
        public ObservableCollection<TechOperationViewModel> Operations
        {
            get => _Operations;
            set => Set(ref _Operations, value);
        }
        #endregion 
        #endregion

    }
}