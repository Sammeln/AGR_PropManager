using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using AGR_PropManager.Infrastructure.Commands;
using AGR_PropManager.ViewModels.Base;
using AGR_PropManager.ViewModels.Components;
using AGR_PropManager.ViewModels.TechProcess;
using Agrovent.DAL;
using AgroventInfrastructure.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AGR_PropManager.ViewModels.Windows
{
    public class OperationSelectionViewModel : BaseViewModel
    {
        private readonly ILogger? _logger;
        private readonly ObservableCollection<ComponentItemViewModel> _selectedComponents;
        private readonly IServiceScopeFactory? _scopeFactory;

        public OperationSelectionViewModel(
            ObservableCollection<ComponentItemViewModel> selectedComponents,
            IServiceScopeFactory scopeFactory,
            ILogger? logger = null)
        {
            _logger = logger;
            _selectedComponents = selectedComponents ?? new ObservableCollection<ComponentItemViewModel>();
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));

            TemplateOperationsView = CollectionViewSource.GetDefaultView(TemplateOperations);
            TemplateOperationsView.Filter = FilterTemplateOperations;
            _ = LoadTemplateOperationsAsync();
        }

        public OperationSelectionViewModel() { }

        private ObservableCollection<TemplateOperationItemViewModel> _templateOperations = new();
        public ObservableCollection<TemplateOperationItemViewModel> TemplateOperations => _templateOperations;
        public ICollectionView TemplateOperationsView { get; }

        private string? _searchText;
        public string? SearchText
        {
            get => _searchText;
            set { if (Set(ref _searchText, value)) TemplateOperationsView.Refresh(); }
        }

        private TemplateOperationItemViewModel? _selectedOperation;
        public TemplateOperationItemViewModel? SelectedOperation
        {
            get => _selectedOperation;
            set => Set(ref _selectedOperation, value);
        }

        private ICommand _CloseCommand;
        public ICommand CloseCommand => _CloseCommand ??= new RelayCommand(OnCloseCommandExecuted, p => true);
        private void OnCloseCommandExecuted(object p) => CloseRequested?.Invoke(this, EventArgs.Empty);

        private ICommand _setOperationCommand;
        public ICommand SetOperationCommand => _setOperationCommand
            ??= new RelayCommand<TemplateOperationItemViewModel>(OnSetOperationCommandExecuted, CanSetOperationCommandExecute);

        private bool CanSetOperationCommandExecute(TemplateOperationItemViewModel? p) => p != null && _selectedComponents.Any();

        /// <summary>
        /// BATCH WRITE: один scope + одна transaction на изменение всех выбранных компонентов.
        /// Внутри ProcessSingleComponent не создаётся новый scope.
        /// </summary>
        private async void OnSetOperationCommandExecuted(TemplateOperationItemViewModel? selectedOpVm)
        {
            if (selectedOpVm == null || !_selectedComponents.Any()) return;
            if (_selectedComponents.Any(x => x.ComponentType == AGR_ComponentType_e.Purchased)) return;
            if (_scopeFactory == null) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var unitOfWork = scope.ServiceProvider.GetRequiredService<UnitOfWork>();

                await unitOfWork.BeginTransactionAsync();
                try
                {
                    foreach (var component in _selectedComponents)
                        await ProcessSingleComponent(unitOfWork, component, selectedOpVm);

                    await unitOfWork.CompleteAsync();
                    await unitOfWork.CommitTransactionAsync();
                }
                catch
                {
                    await unitOfWork.RollbackTransactionAsync();
                    throw;
                }

                _logger?.LogInformation("Все изменения успешно сохранены в БД. Добавлено операций: {Count}.", _selectedComponents.Count);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Ошибка при добавлении операции в техпроцессы выделенных компонентов.");
            }
        }

        private async Task ProcessSingleComponent(
            UnitOfWork unitOfWork,
            ComponentItemViewModel compVm,
            TemplateOperationItemViewModel selectedOpVm)
        {
            var entityTechProcess = await unitOfWork.TechProcessRepository.GetOrCreateForComponentAsync(compVm.PartNumber);
            int nextSequenceNumber = entityTechProcess.Operations.Count + 1;

            var newOpEntity = await unitOfWork.TechProcessRepository.AddOperationAsync(
                entityTechProcess,
                selectedOpVm.TemplateOperation,
                nextSequenceNumber);

            var labour = SetLabour(compVm, selectedOpVm);
            newOpEntity.CostPerHour = labour;
            await unitOfWork.TechProcessRepository.UpdateOperationAsync(newOpEntity);

            // Constructor заполняет CostPerHour до ParentComponent, поэтому локальная
            // инициализация VM не вызывает повторный DB write внутри batch scope.
            var newOpVm = new TechOperationViewModel(newOpEntity)
            {
                ParentComponent = compVm,
                SequenceNumber = nextSequenceNumber
            };

            newOpVm.PropertyChanged += compVm.Item_PropertyChanged;
            compVm.Operations.Add(newOpVm);
        }

        public event EventHandler? CloseRequested;

        /// <summary>
        /// READ: scope -> query -> materialize -> dispose.
        /// </summary>
        private async Task LoadTemplateOperationsAsync()
        {
            if (_scopeFactory == null) return;

            try
            {
                List<TemplateOperationItemViewModel> snapshot;
                using (var scope = _scopeFactory.CreateScope())
                {
                    var unitOfWork = scope.ServiceProvider.GetRequiredService<UnitOfWork>();
                    var operations = (await unitOfWork.TechProcessRepository.GetTemplateOperationAsync()).ToList();
                    snapshot = operations.Select(op => new TemplateOperationItemViewModel(op)).ToList();
                }

                TemplateOperations.Clear();
                foreach (var operation in snapshot)
                    TemplateOperations.Add(operation);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Ошибка загрузки шаблонных операций.");
            }
        }

        private bool FilterTemplateOperations(object item)
        {
            if (item is not TemplateOperationItemViewModel templateOperation) return false;
            if (string.IsNullOrWhiteSpace(SearchText)) return true;

            string[] splitSearch = SearchText.Split(' ');
            if (templateOperation.Name is null) return true;
            if (splitSearch.All(s => templateOperation.Name.Contains(s, StringComparison.OrdinalIgnoreCase))) return true;
            if (splitSearch.All(s => templateOperation.WorkstationName.Contains(s, StringComparison.OrdinalIgnoreCase))) return true;
            return false;
        }

        private decimal SetLabour(ComponentItemViewModel component, TemplateOperationItemViewModel sector)
        {
            decimal cost = 0;
            float blankContSum = 0;
            float blankThick = 0;
            int? blankBends = 0;
            float blanklen = 0;

            try
            {
                _ = float.Parse(component.PropertiesCollection.FirstOrDefault(p => p.Name == AGR_PropertyNames.BlankVolume)?.Value ?? "0");
                _ = float.Parse(component.PropertiesCollection.FirstOrDefault(p => p.Name == AGR_PropertyNames.BlankMass)?.Value ?? "0");

                if (component.ComponentType == AGR_ComponentType_e.Part)
                    blanklen = float.Parse(component.PropertiesCollection.FirstOrDefault(p => p.Name == AGR_PropertyNames.BlankLen)?.Value ?? "0");

                if (component.ComponentType == AGR_ComponentType_e.SheetMetallPart)
                {
                    blankBends = component.BendCount;
                    blankContSum = float.Parse(component.ContourLength.ToString());
                    blankThick = float.Parse(component.PropertiesCollection.FirstOrDefault(p => p.Name == AGR_PropertyNames.BlankThick)?.Value ?? "0");
                }
            }
            catch { }

            try
            {
                switch (sector.WorkStationId)
                {
                    //Листогиб
                    case 13:
                    var tmpCost = blankBends * 0.3 + 0.25;
                    cost = Math.Round((decimal)tmpCost, 3, MidpointRounding.ToPositiveInfinity);
                    break;
                    //старый неактуальный trumpf
                    case 14:
                    //лазер
                    case 87:
                    if (sector.Name.Contains("Написать", StringComparison.OrdinalIgnoreCase)) cost = 0.17m;
                    else
                    {
                        if (blankThick <= 0.55f) cost = (decimal)(blankContSum / 1000 * 0.05);
                        else if (blankThick <= 0.7f) cost = (decimal)(blankContSum / 1000 * 0.08);
                        else if (blankThick <= 1f) cost = (decimal)(blankContSum / 1000 * 0.03);
                        else if (blankThick <= 1.5f) cost = (decimal)(blankContSum / 1000 * 0.09);
                        else if (blankThick <= 2f) cost = (decimal)(blankContSum / 1000 * 0.2);
                        else if (blankThick <= 3f) cost = (decimal)(blankContSum / 1000 * 0.4);
                        else cost = (decimal)(blankContSum / 1000 * 0.9);
                        cost = Math.Round(cost, 3, MidpointRounding.ToPositiveInfinity);
                    }
                    break;
                    //Отбортовка
                    case 71: cost = 18m; break;
                    //Покраска
                    case 70: cost = 3m; break;
                    //формовка
                    case 64: cost = 20m; break;
                    //пила пластик
                    case 75: cost = 0.6m; break;
                    //пила fe
                    case 79: cost = 1m; break;
                    //пила AL
                    case 78: cost = 2.9m; break;
                    //гильотина
                    case 76: cost = 0.15m; break;
                    //парвильно-отрезеой
                    case 74:
                    tmpCost = Math.Round(blanklen / 1000 * 0.025, 3, MidpointRounding.ToPositiveInfinity);
                    cost = (decimal)tmpCost;
                    break;
                    //Дырокол
                    case 83: cost = 5m; break;
                    //Вальцы Fl
                    case 81: cost = 3m; break;
                    //Вальцы Ob
                    case 82: cost = 3m; break;
                }
            catch { cost = 0; }

            return cost;
        }
    }
}
