using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
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

        #region CTOR
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
        #endregion

        #region PROPS
        private ObservableCollection<TemplateOperationItemViewModel> _templateOperations = new();
        public ObservableCollection<TemplateOperationItemViewModel> TemplateOperations => _templateOperations;

        public ICollectionView TemplateOperationsView { get; }

        private string? _searchText;
        public string? SearchText
        {
            get => _searchText;
            set
            {
                if (Set(ref _searchText, value))
                    TemplateOperationsView.Refresh();
            }
        }

        private TemplateOperationItemViewModel? _selectedOperation;
        public TemplateOperationItemViewModel? SelectedOperation
        {
            get => _selectedOperation;
            set => Set(ref _selectedOperation, value);
        }
        #endregion

        #region Commands
        private ICommand _CloseCommand;
        public ICommand CloseCommand => _CloseCommand
            ??= new RelayCommand(OnCloseCommandExecuted, CanCloseCommandExecute);
        private bool CanCloseCommandExecute(object p) => true;
        private void OnCloseCommandExecuted(object p) => CloseRequested?.Invoke(this, EventArgs.Empty);

        private ICommand _setOperationCommand;
        public ICommand SetOperationCommand => _setOperationCommand
            ??= new RelayCommand<TemplateOperationItemViewModel>(
                OnSetOperationCommandExecuted,
                CanSetOperationCommandExecute);

        private bool CanSetOperationCommandExecute(TemplateOperationItemViewModel? p) =>
            p != null && _selectedComponents.Any();

        /// <summary>
        /// BATCH WRITE: один scope на всю операцию + одна transaction.
        /// Scope живёт ровно столько, сколько длится изменение всех выбранных компонентов.
        /// </summary>
        private async void OnSetOperationCommandExecuted(TemplateOperationItemViewModel? selectedOpVm)
        {
            if (selectedOpVm == null || !_selectedComponents.Any()) return;
            if (_selectedComponents.Any(x => x.ComponentType == AGR_ComponentType_e.Purchased)) return;
            if (_scopeFactory == null) return;

            _logger?.LogInformation($"Попытка добавить операцию '{selectedOpVm.Name}' в техпроцессы {_selectedComponents.Count} выделенных компонентов.");

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var unitOfWork = scope.ServiceProvider.GetRequiredService<UnitOfWork>();

                await unitOfWork.BeginTransactionAsync();
                try
                {
                    foreach (var compVm in _selectedComponents)
                        await ProcessSingleComponent(unitOfWork, compVm, selectedOpVm);

                    await unitOfWork.CompleteAsync();
                    await unitOfWork.CommitTransactionAsync();
                }
                catch
                {
                    await unitOfWork.RollbackTransactionAsync();
                    throw;
                }

                _logger?.LogInformation($"Все изменения успешно сохранены в БД. Добавлено операций: {_selectedComponents.Count}.");
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
            _logger?.LogDebug($"Обработка компонента {compVm.PartNumber}.");

            var entityTechProcess = await unitOfWork.TechProcessRepository
                .GetOrCreateForComponentAsync(compVm.PartNumber);

            _logger?.LogDebug($"Получен/создан техпроцесс ID: {entityTechProcess.Id} для {compVm.PartNumber}.");

            int nextSequenceNumber = entityTechProcess.Operations.Count + 1;

            var newOpEntity = await unitOfWork.TechProcessRepository.AddOperationAsync(
                entityTechProcess,
                selectedOpVm.TemplateOperation,
                nextSequenceNumber);

            var labour = SetLabour(compVm, selectedOpVm);
            newOpEntity.CostPerHour = labour;
            await unitOfWork.TechProcessRepository.UpdateOperationAsync(newOpEntity);

            _logger?.LogDebug($"Создана сущность Operation ID: {newOpEntity.Id} для техпроцесса {entityTechProcess.Id}.");

            var newOpVm = new TechOperationViewModel(newOpEntity)
            {
                ParentComponent = compVm,
                CostPerHour = labour,
                SequenceNumber = nextSequenceNumber
            };

            newOpVm.PropertyChanged += compVm.Item_PropertyChanged;
            compVm.Operations.Add(newOpVm);

            _logger?.LogDebug($"Операция '{newOpVm.Name}' (Seq: {newOpVm.SequenceNumber}) добавлена в ViewModel компонента '{compVm.PartNumber}'.");
        }
        #endregion

        public event EventHandler? CloseRequested;

        /// <summary>
        /// READ: новый scope -> запрос -> materialize -> dispose.
        /// </summary>
        private async Task LoadTemplateOperationsAsync()
        {
            if (_scopeFactory == null) return;

            try
            {
                List<AgroventInfrastructure.Entities.Components.Operation> opsFromDb;
                using (var scope = _scopeFactory.CreateScope())
                {
                    var unitOfWork = scope.ServiceProvider.GetRequiredService<UnitOfWork>();
                    opsFromDb = (await unitOfWork.TechProcessRepository.GetTemplateOperationAsync()).ToList();
                }

                TemplateOperations.Clear();
                foreach (var op in opsFromDb)
                    TemplateOperations.Add(new TemplateOperationItemViewModel(op));

                _logger?.LogInformation($"Загружено {TemplateOperations.Count} шаблонных операций.");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Ошибка загрузки шаблонных операций.");
            }
        }

        private bool FilterTemplateOperations(object item)
        {
            if (item is not TemplateOperationItemViewModel templateOperation)
                return false;

            if (string.IsNullOrWhiteSpace(SearchText))
                return true;

            string[] splitSearch = SearchText.Split(' ').ToArray();
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
            float blankVolume = 0;
            float blankMass = 0;

            try
            {
                blankVolume = float.Parse(component.PropertiesCollection.FirstOrDefault(
                    prop => prop.Name == AGR_PropertyNames.BlankVolume)?.Value ?? "0");
                blankMass = float.Parse(component.PropertiesCollection.FirstOrDefault(
                    prop => prop.Name == AGR_PropertyNames.BlankMass)?.Value ?? "0");

                if (component.ComponentType == AGR_ComponentType_e.Part)
                {
                    blanklen = float.Parse(component.PropertiesCollection.FirstOrDefault(
                        prop => prop.Name == AGR_PropertyNames.BlankLen)?.Value ?? "0");
                }

                if (component.ComponentType == AGR_ComponentType_e.SheetMetallPart)
                {
                    blankBends = component.BendCount;
                    blankContSum = float.Parse(component.ContourLength.ToString());
                    blankThick = float.Parse(component.PropertiesCollection.FirstOrDefault(
                        prop => prop.Name == AGR_PropertyNames.BlankThick)?.Value ?? "0");
                }
            }
            catch (Exception)
            {
            }

            try
            {
                switch (sector.WorkStationId)
                {
                    case 13:
                        var tmpCost = blankBends * 0.3 + 0.25;
                        cost = Math.Round((decimal)tmpCost, 3, MidpointRounding.ToPositiveInfinity);
                        break;

                    case 14:
                    case 87:
                        if (sector.Name.Contains("Написать", StringComparison.OrdinalIgnoreCase))
                        {
                            cost = 0.17m;
                        }
                        else
                        {
                            if (blankThick <= 0.55f)
                                cost = (decimal)(blankContSum / 1000 * 0.05);
                            else if (blankThick <= 0.7f)
                                cost = (decimal)(blankContSum / 1000 * 0.08);
                            else if (blankThick <= 1f)
                                cost = (decimal)(blankContSum / 1000 * 0.03);
                            else if (blankThick <= 1.5f)
                                cost = (decimal)(blankContSum / 1000 * 0.09);
                            else if (blankThick <= 2f)
                                cost = (decimal)(blankContSum / 1000 * 0.2);
                            else if (blankThick <= 3f)
                                cost = (decimal)(blankContSum / 1000 * 0.4);
                            else if (blankThick > 3f)
                                cost = (decimal)(blankContSum / 1000 * 0.9);

                            cost = Math.Round(cost, 3, MidpointRounding.ToPositiveInfinity);
                        }
                        break;

                    case 71:
                        cost = 18m;
                        break;
                    case 70:
                        cost = 3m;
                        break;
                    case 64:
                        cost = 20m;
                        break;
                    case 75:
                        cost = 0.6m;
                        break;
                    case 79:
                        cost = 1m;
                        break;
                    case 78:
                        cost = 2.9m;
                        break;
                    case 76:
                        cost = 0.15m;
                        break;
                    case 74:
                        tmpCost = Math.Round(blanklen / 1000 * 0.025, 3, MidpointRounding.ToPositiveInfinity);
                        cost = (decimal)tmpCost;
                        break;
                    default:
                        cost = 0;
                        break;
                }
            }
            catch (Exception)
            {
                cost = 0;
            }

            return cost;
        }
    }
}
