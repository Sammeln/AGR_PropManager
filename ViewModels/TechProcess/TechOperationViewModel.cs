using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AGR_PropManager.ViewModels.Base;
using AGR_PropManager.ViewModels.Components;
using AgroventInfrastructure.Interfaces.Entities.TechProcess;
using AgroventInfrastructure.Entities.TechProcess;

namespace AGR_PropManager.ViewModels.TechProcess
{
    public class TechOperationViewModel : BaseViewModel
    {
        public Operation? Operation { get; }

        #region CTOR
        public TechOperationViewModel()
        {

        }

        public TechOperationViewModel(Operation operation)
        {

            Operation = operation;
            WorkstationName = operation.WorkstationName;
            Name = operation.Name;
            CostPerHour = operation.CostPerHour;
            SequenceNumber = operation.SequenceNumber;
            TechProcess = operation.TechnologicalProcess;
        }

        public TechOperationViewModel(TemplateOperationItemViewModel operation)
        {
            WorkstationName = operation.WorkstationName;
            Name = operation.Name;
            CostPerHour = operation.CostPerHour;
        }
        #endregion

        #region PROPS


        #region Property - 
        private ComponentItemViewModel _ParentComponent;
        public ComponentItemViewModel ParentComponent
        {
            get => _ParentComponent;
            set => Set(ref _ParentComponent, value);
        }
        #endregion 

        #region WorkstationName
        private string _WorkstationName;
        public string WorkstationName { get => _WorkstationName; set => Set(ref _WorkstationName, value); }
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
            set
            {
                if (Set(ref _CostPerHour, value))
                {
                    Operation.CostPerHour = value;
                    ParentComponent?.OnOperationCostChanged(this);
                }
            }
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

        #region Property - TechProcess
        private TechnologicalProcess _TechProcess;
        public TechnologicalProcess TechProcess
        {
            get => _TechProcess;
            set => Set(ref _TechProcess, value);
        }
        #endregion 
        #endregion

        public event EventHandler? CostChanged;

    }
}
