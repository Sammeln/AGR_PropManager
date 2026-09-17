using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AGR_PropManager.ViewModels.Base;
using AgroventInfrastructure.Entities.TechProcess;

namespace AGR_PropManager.ViewModels.TechProcess
{
    public class TemplateOperationItemViewModel : BaseViewModel
    {
        public TemplateOperation TemplateOperation { get; }
        #region CTOR
        public TemplateOperationItemViewModel()
        {

        }

        public TemplateOperationItemViewModel(TemplateOperation operation)
        {
            TemplateOperation = operation;
            WorkstationName = operation.Workstation.Name;
            WorkStationId = operation.WorkstationId;
            Name = operation.Name;
            CostPerHour = operation.CostPerHour;
            
        }
        #endregion

        #region PROPS
        #region WorkstationName
        private string _WorkstationName;
        public string WorkstationName { get => _WorkstationName; set => Set(ref _WorkstationName, value); }
        #endregion


        #region Property - WorkStationId
        private int _WorkStationId;
        public int WorkStationId
        {
            get => _WorkStationId;
            set => Set(ref _WorkStationId, value);
        }
        #endregion 

        #region Property - Name
        private string _Name;
        public string Name
        {
            get => _Name;
            set => Set(ref _Name, value);
        }
        #endregion

        #region Property - CostPerHour

        private decimal _CostPerHour;
        public decimal CostPerHour
        {
            get => _CostPerHour;
            set => Set(ref _CostPerHour, value);
        }
        #endregion

        #region Property - _SequenceNumber
        private int _SequenceNumber;
        public int SequenceNumber
        {
            get => _SequenceNumber;
            set => Set(ref _SequenceNumber, value);
        }
        #endregion  
        #endregion

    }
}
