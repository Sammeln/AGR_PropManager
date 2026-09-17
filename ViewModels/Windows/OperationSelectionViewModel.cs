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
using Agrovent.DAL.Services.Repositories;
using AgroventInfrastructure.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AGR_PropManager.ViewModels.Windows
{
    public class OperationSelectionViewModel : BaseViewModel
    {
        private readonly ILogger? _logger;
        private readonly ObservableCollection<ComponentItemViewModel> _selectedComponents;
        private readonly UnitOfWork _unitOfWork;

        #region CTOR
        public OperationSelectionViewModel(
            ObservableCollection<ComponentItemViewModel> selectedComponents,
            UnitOfWork unitOfWork,
            ILogger? logger = null)
        {
            _logger = logger;
            _selectedComponents = selectedComponents ?? new ObservableCollection<ComponentItemViewModel>();
            _unitOfWork = unitOfWork;

            TemplateOperationsView = CollectionViewSource.GetDefaultView(TemplateOperations);
            TemplateOperationsView.Filter = FilterTemplateOperations;
            LoadTemplateOperationsAsync();
        }

        // Конструктор по умолчанию (для Design-time)
        public OperationSelectionViewModel() { }
        #endregion

        #region PROPS
        private ObservableCollection<TemplateOperationItemViewModel> _templateOperations = new();
        public ObservableCollection<TemplateOperationItemViewModel> TemplateOperations => _templateOperations;

        public ICollectionView TemplateOperationsView { get; }

        #region SearchText
        private string? _searchText;
        public string? SearchText
        {
            get => _searchText;
            set
            {
                if (Set(ref _searchText, value))
                {
                    TemplateOperationsView.Refresh();
                }
            }
        }
        #endregion

        private TemplateOperationItemViewModel? _selectedOperation;
        public TemplateOperationItemViewModel? SelectedOperation
        {
            get => _selectedOperation;
            set => Set(ref _selectedOperation, value);
        }

        #endregion

        #region Commands

        #region CloseCommand
        private ICommand _CloseCommand;
        public ICommand CloseCommand => _CloseCommand
            ??= new RelayCommand(OnCloseCommandExecuted, CanCloseCommandExecute);
        private bool CanCloseCommandExecute(object p) => true;
        private void OnCloseCommandExecuted(object p)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        #endregion

        #region SetOperationCommand
        private ICommand _setOperationCommand;
        public ICommand SetOperationCommand => _setOperationCommand
            ??= new RelayCommand<TemplateOperationItemViewModel>(
                OnSetOperationCommandExecuted,
                CanSetOperationCommandExecute
            );

        private bool CanSetOperationCommandExecute(TemplateOperationItemViewModel? p) =>
            p != null && _selectedComponents.Any();

        private async void OnSetOperationCommandExecuted(TemplateOperationItemViewModel? selectedOpVm)
        {
            if (selectedOpVm == null || !_selectedComponents.Any()) return;
            if (_selectedComponents.Any(x => x.ComponentType == AGR_ComponentType_e.Purchased)) return;

            _logger?.LogInformation($"Попытка добавить операцию '{selectedOpVm.Name}' в техпроцессы {_selectedComponents.Count} выделенных компонентов.");

            try
            {
                // Начинаем транзакцию
                await _unitOfWork.BeginTransactionAsync();
                // Обрабатываем каждый компонент последовательно, чтобы избежать параллельных операций с контекстом
                foreach (var compVm in _selectedComponents)
                {
                    await ProcessSingleComponent(compVm, selectedOpVm);
                }
                
                // Сохраняем все изменения в рамках одной транзакции
                await _unitOfWork.CompleteAsync();

                // Фиксируем транзакцию
                await _unitOfWork.CommitTransactionAsync();

                _logger?.LogInformation($"Все изменения успешно сохранены в БД. Добавлено операций: {_selectedComponents.Count}.");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Ошибка при добавлении операции в техпроцессы выделенных компонентов.");
            }
        }
        private async Task ProcessSingleComponent(ComponentItemViewModel compVm, TemplateOperationItemViewModel selectedOpVm)
        {
            _logger?.LogDebug($"Обработка компонента {compVm.PartNumber}.");

            // 1. Получить/создать техпроцесс через репозиторий
            var entityTechProcess = await _unitOfWork.TechProcessRepository.GetOrCreateForComponentAsync(compVm.PartNumber);
            _logger?.LogDebug($"Получен/создан техпроцесс ID: {entityTechProcess.Id} для {compVm.PartNumber}.");

            // 2. Вычислить SequenceNumber
            int nextSequenceNumber = entityTechProcess.Operations.Count + 1;

            // 3. Добавить операцию через репозиторий
            var newOpEntity = await _unitOfWork.TechProcessRepository.AddOperationAsync(
                entityTechProcess,
                selectedOpVm.TemplateOperation,
                nextSequenceNumber
            );

            // Обновляем CostPerHour в сущности операции
            newOpEntity.CostPerHour = SetLabour(compVm, selectedOpVm);
            await _unitOfWork.TechProcessRepository.UpdateOperationAsync(newOpEntity);

            _logger?.LogDebug($"Создана сущность Operation ID: {newOpEntity.Id} для техпроцесса {entityTechProcess.Id}.");

            // 4. Создать ViewModel для новой операции и добавить в коллекцию UI
            var newOpVm = new TechOperationViewModel(newOpEntity);
            newOpVm.ParentComponent = compVm; // Устанавливаем связь с родительским компонентом
            newOpVm.CostPerHour = SetLabour(compVm, selectedOpVm);
            newOpVm.SequenceNumber = nextSequenceNumber;

            newOpVm.PropertyChanged += compVm.Item_PropertyChanged;
            compVm.Operations.Add(newOpVm);

            _logger?.LogDebug($"Операция '{newOpVm.Name}' (Seq: {newOpVm.SequenceNumber}) добавлена в ViewModel компонента '{compVm.PartNumber}'.");
        }

        #endregion

        #endregion

        public event EventHandler? CloseRequested;

        private async void LoadTemplateOperationsAsync()
        {
            TemplateOperations.Clear();

            var opsFromDb = await _unitOfWork.TechProcessRepository.GetTemplateOperationAsync();

            foreach (var op in opsFromDb)
            {
                TemplateOperations.Add(new TemplateOperationItemViewModel(op));
            }

            _logger?.LogInformation($"Загружено {TemplateOperations.Count} шаблонных операций.");
        }

        private bool FilterTemplateOperations(object item)
        {
            if (item is not TemplateOperationItemViewModel templateOperation)
                return false;

            if (string.IsNullOrWhiteSpace(SearchText))
                return true;

            string[] splitSearch = SearchText.Split(' ').ToArray();

            if (templateOperation.Name is null) return true;
            if (splitSearch.All(s => templateOperation.Name.Contains(s.ToString(), StringComparison.OrdinalIgnoreCase))) return true;
            if (splitSearch.All(s => templateOperation.WorkstationName.Contains(s.ToString(), StringComparison.OrdinalIgnoreCase))) return true;

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
                // Логирование ошибки может быть добавлено
            }

            try
            {
                switch (sector.WorkStationId)
                {
                    // Гибка. Участок 13.
                    case 13:
                    var tmpCost = blankBends * 0.3 + 0.25;
                    cost = Math.Round((decimal)tmpCost,3,MidpointRounding.ToPositiveInfinity);
                    break;

                    // Вырубка на трумпфе/лазере. Участок 14.
                    case 14:
                    case 87:
                    if (sector.Name.Contains("Написать", StringComparison.OrdinalIgnoreCase))
                    {
                        cost = 0.17m;
                    }
                    else
                    {
                        if (blankThick <= 0.55f)
                        {
                            tmpCost = blankContSum / 1000 * 0.05;
                            cost = (decimal)tmpCost;
                        }
                        else if (blankThick <= 0.7f)
                        {
                            tmpCost = blankContSum / 1000 * 0.08;
                            cost = (decimal)tmpCost;
                        }
                        else if (blankThick <= 1f)
                        {
                            tmpCost = blankContSum / 1000 * 0.03;
                            cost = (decimal)tmpCost;
                        }
                        else if (blankThick <= 1.5f)
                        {
                            tmpCost = blankContSum / 1000 * 0.09;
                            cost = (decimal)tmpCost;
                        }
                        else if (blankThick <= 2f)
                        {
                            tmpCost = blankContSum / 1000 * 0.2;
                            cost = (decimal)tmpCost;
                        }
                        else if (blankThick <= 3f)
                        {
                            tmpCost = blankContSum / 1000 * 0.4;
                            cost = (decimal)tmpCost;
                        }
                        else if (blankThick > 3f)
                        {
                            tmpCost = blankContSum / 1000 * 0.9;
                            cost = (decimal)tmpCost;
                        }
                        cost = Math.Round(cost, 3, MidpointRounding.ToPositiveInfinity);
                    }
                    break;

                    // Отбортовка. Участок 71.
                    case 71:
                    cost = 18m;
                    break;

                    // Покрасочная камера. Участок 70.
                    case 70:
                    cost = 3m;
                    break;

                    // Формовка. Участок 64.
                    case 64:
                    cost = 20;
                    break;

                    // Пила пластик. Участок 75.
                    case 75:
                    cost = 0.6m;
                    break;

                    // Пила FE. Участок 79.
                    case 79:
                    cost = 1m;
                    break;

                    // Пила AL. Участок 78.
                    case 78:
                    cost = 2.9m;
                    break;

                    // Гильотина. Участок 76.
                    case 76:
                    cost = 0.15m;
                    break;

                    // Правильно-отрезной. Участок 74.
                    case 74:
                    tmpCost = Math.Round((blanklen / 1000 * 0.025), 3, MidpointRounding.ToPositiveInfinity);
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
