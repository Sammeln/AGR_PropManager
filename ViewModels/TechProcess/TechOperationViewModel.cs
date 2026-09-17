using System;
using AGR_PropManager.ViewModels.Base;
using AGR_PropManager.ViewModels.Components;
using AgroventInfrastructure.Entities.TechProcess;

namespace AGR_PropManager.ViewModels.TechProcess
{
    public class TechOperationViewModel : BaseViewModel
    {
        public Operation? Operation { get; }

        public TechOperationViewModel() { }

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

        private ComponentItemViewModel _ParentComponent;
        public ComponentItemViewModel ParentComponent
        {
            get => _ParentComponent;
            set => Set(ref _ParentComponent, value);
        }

        private string _WorkstationName;
        public string WorkstationName { get => _WorkstationName; set => Set(ref _WorkstationName, value); }

        private string _Name;
        public string Name { get => _Name; set => Set(ref _Name, value); }

        private decimal _CostPerHour;
        public decimal CostPerHour
        {
            get => _CostPerHour;
            set
            {
                if (!Set(ref _CostPerHour, value)) return;

                // Template VM не имеет связанной EF-сущности. Для обычной операции
                // обновляем только локальный snapshot, а ParentComponent выполняет DB write
                // через новый scope.
                if (Operation != null)
                    Operation.CostPerHour = value;

                ParentComponent?.OnOperationCostChanged(this);
            }
        }

        private int _SequenceNumber;
        public int SequenceNumber { get => _SequenceNumber; set => Set(ref _SequenceNumber, value); }

        private TechnologicalProcess _TechProcess;
        public TechnologicalProcess TechProcess
        {
            get => _TechProcess;
            set => Set(ref _TechProcess, value);
        }

        public event EventHandler? CostChanged;
    }
}
