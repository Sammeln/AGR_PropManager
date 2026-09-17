using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using AGR_PropManager.Infrastructure.Commands;
using AGR_PropManager.ViewModels.Base;
using AGR_PropManager.ViewModels.Components;
using AGR_PropManager.ViewModels.Reports;
using AGR_PropManager.ViewModels.TechProcess;
using AGR_PropManager.Views;
using AGR_PropManager.Views.Reports;
using Agrovent.DAL;
using AgroventInfrastructure;
using AgroventInfrastructure.Entities.Components;
using AgroventInfrastructure.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MessageBox = System.Windows.Forms.MessageBox;

namespace AGR_PropManager.ViewModels.Windows
{
    /// <summary>
    /// ViewModel для диалогового окна редактора технологического процесса
    /// Содержит функционал группировки и редактирования компонентов
    /// </summary>
    public class TechProcessEditorViewModel : BaseViewModel
    {
        private readonly DataContext _dataContext;
        private readonly UnitOfWork _unitOfWork;
        private readonly ILogger _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ComponentItemViewModel _selectedComponent;
        private CancellationTokenSource _loadingCts;

        private ImportClassifierReportViewModel _importClassifierReportViewModel;
        private FullImportClassifierReportViewModel _importFullClassifierReportViewModel;
        private TreeImportReportViewModel _treeImportReportViewModel;
        private TechOpsImportReportViewModel _techOpsImportReportViewModel;
        public TechProcessEditorViewModel(
            ComponentItemViewModel selectedComponent,
            DataContext dataContext,
            ILogger? logger,
            UnitOfWork unitOfWork,
            IServiceScopeFactory scopeFactory)
        {
            _selectedComponent = selectedComponent;
            _dataContext = dataContext;
            _logger = logger;
            _unitOfWork = unitOfWork;
            _scopeFactory = scopeFactory;

            // НЕ вызываем Initialize здесь!
            // Вместо этого используем метод, который можно вызвать из View
        }
        // Вызываем этот метод из View после загрузки окна
        public async Task InitializeAsync()
        {
            _loadingCts = new CancellationTokenSource();
            try
            {
                IsLoading = true;
                LoadingStatus = "Загрузка данных...";

                if (_selectedComponent.ComponentType == AGR_ComponentType_e.Assembly)
                {
                    await LoadAssemblyStructureAsync(_selectedComponent.PartNumber, _loadingCts.Token);
                }
                else if (_selectedComponent.ComponentType == AGR_ComponentType_e.Part ||
                         _selectedComponent.ComponentType == AGR_ComponentType_e.SheetMetallPart)
                {
                    await LoadPartDataAsync(_selectedComponent.PartNumber, _loadingCts.Token);
                }

                // Настраиваем CollectionViewSource
                Components_CVS.Source = Components;
                RefreshGrouping();

                foreach (var item in Components)
                {
                    item.PropertyChanged += Item_PropertyChanged;
                }

                LoadingStatus = "Проверка данных...";
                await ValidateSpecificationAsync();
            }
            catch (OperationCanceledException)
            {
                _logger?.LogInformation("Загрузка была отменена");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Ошибка при инициализации");
                MessageBox.Show($"Ошибка при загрузке данных: {ex.Message}",
                    "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                IsLoading = false;
                LoadingStatus = "";
                _loadingCts?.Dispose();
            }
        }

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

        #region SaveCommand
        private ICommand _SaveCommand;
        public ICommand SaveCommand => _SaveCommand
            ??= new RelayCommand(OnSaveCommandExecuted, CanSaveCommandExecute);
        private bool CanSaveCommandExecute(object p) => true;
        private async void OnSaveCommandExecuted(object p)
        {
            await SaveChangesAsync();
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        #endregion

        #region InsertTechProcessCommand
        private ICommand _InsertTechProcessCommand;
        public ICommand InsertTechProcessCommand => _InsertTechProcessCommand
            ??= new RelayCommand(OnInsertTechProcessCommandExecuted, CanInsertTechProcessCommandExecute);
        private bool CanInsertTechProcessCommandExecute(object p) => HasSelectedComponents;
        private void OnInsertTechProcessCommandExecuted(object p)
        {
            EditProcess();
        }

        #region Метод редактирования процесса (открывает окно выбора операции)
        private void EditProcess()
        {
            var selectedComponents = Components.Where(c => c.IsSelected).ToList();
            if (!selectedComponents.Any()) return;

            _logger?.LogInformation($"Открытие окна выбора операции для {selectedComponents.Count} компонентов.");

            using (var scope = _scopeFactory.CreateScope())
            {
                var unitOfWork = scope.ServiceProvider.GetRequiredService<UnitOfWork>();

                var operationSelectionViewModel = new OperationSelectionViewModel(
                    new ObservableCollection<ComponentItemViewModel>(selectedComponents),
                    unitOfWork,
                    _logger);

                var selectionWindow = new OperationSelectionWindow() { DataContext = operationSelectionViewModel };
                operationSelectionViewModel.CloseRequested += (s, e) => selectionWindow.Close();
                selectionWindow.Closed += SelectionWindow_Closed;
                selectionWindow.ShowDialog();
            }
        }

        private void SelectionWindow_Closed(object? sender, EventArgs e)
        {
            DeselectAllComponents();
        }
        #endregion
        #endregion

        #region SelectComponentCommand
        private ICommand _SelectComponentCommand;
        public ICommand SelectComponentCommand => _SelectComponentCommand
            ??= new RelayCommand(OnSelectComponentCommandExecuted, CanSelectComponentCommandExecute);
        private bool CanSelectComponentCommandExecute(object p) => true;
        private void OnSelectComponentCommandExecuted(object p)
        {
            foreach (var item in SelectedComponents)
            {
                item.IsSelected = !item.IsSelected;
            }
        }
        #endregion

        #region DeleteOperationCommand (для удаления операций из главного DataGrid)
        private ICommand _DeleteOperationCommand;
        public ICommand DeleteOperationCommand => _DeleteOperationCommand
            ??= new RelayCommand<TechOperationViewModel>(OnDeleteOperationCommandExecuted, CanDeleteOperationCommandExecute);
        private bool CanDeleteOperationCommandExecute(TechOperationViewModel? p) => p != null;
        private async void OnDeleteOperationCommandExecuted(TechOperationViewModel? operation)
        {
            if (operation == null || operation.ParentComponent == null) return;

            if (operation.TechProcess != null)
            {
                var entOp = _dataContext.Operations.FirstOrDefault(o =>
                    o.TechnologicalProcessId == operation.TechProcess.Id &&
                    o.SequenceNumber == operation.SequenceNumber);
                if (entOp != null)
                {
                    _dataContext.Operations.Remove(entOp);
                    await _dataContext.SaveChangesAsync();
                    operation.ParentComponent.Operations.Remove(operation);
                }
            }
            else
            {
                operation.ParentComponent.Operations.Remove(operation);
            }
            OnPropertyChanged(nameof(ComponentItemViewModel.HasZeroTimeOperations));
        }
        #endregion

        #region SetPaintCommand
        private ICommand _SetPaintCommand;
        public ICommand SetPaintCommand => _SetPaintCommand
            ??= new RelayCommand(OnSetPaintCommandExecuted, CanSetPaintCommandExecute);
        public bool CanSetPaintCommandExecute(object p)
        {
            var _selectedComponents = Components.Any(x => x.IsSelected) ?
                             Components.Where(x => x.IsSelected)
                            : SelectedComponents;
            if (_selectedComponents.Any(
                p => p.ComponentType == AGR_ComponentType_e.Purchased))
            {
                return false;
            }
            return true;
        }
        private void OnSetPaintCommandExecuted(object p)
        {
            var _selectedComponents = Components.Any(x => x.IsSelected) ?
                             Components.Where(x => x.IsSelected)
                            : SelectedComponents;
            if (p.ToString() == "NoPaint")
            {
                foreach (var item in _selectedComponents)
                {
                    item.Paint = null;
                }
                DeselectAllComponents();
                return;
            }

            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var scopedDataContext = scope.ServiceProvider.GetRequiredService<DataContext>();

                    // Создаем ViewModel
                    var selectVm = new AGR_SelectAvaArticleVM(scopedDataContext);
                    selectVm.SearchText = "Краска порошковая";
                    selectVm.SelectedAvaType = "Товар";

                    // Создаем View и устанавливаем DataContext
                    var selectView = new AGR_SelectAvaArticleView { DataContext = selectVm, ShowActivated = true };

                    // Открываем окно модально
                    selectView.ShowDialog();

                    // Если окно закрыто с результатом OK и элемент выбран
                    if (selectVm.IsDialogResultAccepted == true && selectVm.SelectedArticle != null)
                    {
                        foreach (var item in _selectedComponents)
                        {
                            item.Paint = selectVm.SelectedArticle.Name;
                            OnPropertyChanged(nameof(item.PartnumberOrArticle));
                            _logger?.LogInformation("Выбран AvaArticle {Article} для компонента {PartNumber}", selectVm.SelectedArticle.Article, item.PartNumber);

                        }

                        // Обновляем свойства, если это влияет на них (например, BaseMaterialCount)
                        //Task.Run(async () => await UpdatePropertiesAsync()).ConfigureAwait(false); // Вызов асинхронного метода
                    }
                    else
                    {
                        _logger?.LogDebug("Окно выбора AvaArticle закрыто без выбора.");
                    }
                    DeselectAllComponents();
                    ValidateSpecification();
                }
            }
            catch (Exception ex)
            {
                //_logger?.LogError(ex, "Ошибка при открытии окна выбора AvaArticle для компонента {PartNumber}", PartNumber);
            }
        }
        #endregion

        #region REPORTS

        #region ShowImportClassifierReportCommand
        private ICommand _ShowImportClassifierReportCommand;
        public ICommand ShowImportClassifierReportCommand => _ShowImportClassifierReportCommand
            ??= new RelayCommand(OnShowImportClassifierReportCommandExecuted, CanShowImportClassifierReportCommandExecute);
        private bool CanShowImportClassifierReportCommandExecute(object p) => true;
        private void OnShowImportClassifierReportCommandExecuted(object p)
        {
            var mainComponents = Components;

            try
            {
                _importClassifierReportViewModel = new ImportClassifierReportViewModel(mainComponents);

                var reportWindow = new ImportClassifierReportWindow(_importClassifierReportViewModel)
                {
                    Width = 1000,
                    Height = 700,
                    ResizeMode = ResizeMode.CanResizeWithGrip,
                };

                reportWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при открытии отчета: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        #endregion

        #region ShowImportFullClassifierReportCommand
        private ICommand _ShowImportFullClassifierReportCommand;
        public ICommand ShowImportFullClassifierReportCommand => _ShowImportFullClassifierReportCommand
            ??= new RelayCommand(OnShowImportFullClassifierReportCommandExecuted, CanShowImportFullClassifierReportCommandExecute);
        private bool CanShowImportFullClassifierReportCommandExecute(object p) => true;
        private void OnShowImportFullClassifierReportCommandExecuted(object p)
        {
            var mainComponents = Components;

            try
            {
                _importFullClassifierReportViewModel = new FullImportClassifierReportViewModel(mainComponents);

                var reportWindow = new ImportFullClassifierReportWindow(_importFullClassifierReportViewModel)
                {
                    Width = 1000,
                    Height = 700,
                    ResizeMode = ResizeMode.CanResizeWithGrip,
                };

                reportWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при открытии отчета: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        #endregion


        #region ShowTreeImportReportCommand
        private ICommand _ShowTreeImportReportCommand;
        public ICommand ShowTreeImportReportCommand => _ShowTreeImportReportCommand
            ??= new RelayCommand(OnShowTreeImportReportCommandExecuted, CanShowTreeImportReportCommandExecute);
        private bool CanShowTreeImportReportCommandExecute(object p) => true;
        private void OnShowTreeImportReportCommandExecuted(object p)
        {
            var mainComponent = Components?.FirstOrDefault();
            if (mainComponent == null)
            {
                MessageBox.Show("Нет доступных компонентов.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var unitOfWork = scope.ServiceProvider.GetRequiredService<UnitOfWork>();

                _treeImportReportViewModel = new TreeImportReportViewModel(mainComponent, unitOfWork);

                var reportWindow = new TreeImportReportWindow(_treeImportReportViewModel)
                {
                    Width = 1200,
                    Height = 800,
                    ResizeMode = ResizeMode.CanResizeWithGrip
                };
                reportWindow.ShowDialog(); // блокирует — scope освободится сразу после закрытия

                if (_treeImportReportViewModel.IsExcelSaved == true)
                {
                    DialogResult result = MessageBox.Show("Отчет успешно сохранен в Excel. Экспортировать файлы в рабочий каталог?", "Экспорт файлов", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (result == DialogResult.Yes)
                    {
                        ExportStorageFilesCommand?.Execute(null);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при открытии отчета: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        #endregion

        #region ShowTechOpsImportReportCommand
        private ICommand _ShowTechOpsImportReportCommand;
        public ICommand ShowTechOpsImportReportCommand => _ShowTechOpsImportReportCommand
            ??= new RelayCommand(OnShowTechOpsImportReportCommandExecuted, CanShowTechOpsImportReportCommandExecute);
        private bool CanShowTechOpsImportReportCommandExecute(object p) => true;
        private void OnShowTechOpsImportReportCommandExecuted(object p)
        {

            if (Components == null || !Components.Any())
            {
                MessageBox.Show("Нет доступных компонентов.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                _techOpsImportReportViewModel = new TechOpsImportReportViewModel(Components);

                // Display the view in a new window
                var reportWindow = new TechOpsImportReportWindow(_techOpsImportReportViewModel)
                {
                    Width = 1600,
                    Height = 800,
                    ResizeMode = ResizeMode.CanResizeWithGrip
                };
                reportWindow.ShowDialog(); // Modal
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при открытии отчета: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        #endregion

        #endregion

        #region ExportStorageFilesCommand
        private ICommand _ExportStorageFilesCommand;
        public ICommand ExportStorageFilesCommand => _ExportStorageFilesCommand
            ??= new RelayCommand(OnExportStorageFilesCommandExecuted, CanExportStorageFilesCommandExecute);

        private bool CanExportStorageFilesCommandExecute(object p) => ComponentsView?.Cast<object>().Any() == true;
        private async void OnExportStorageFilesCommandExecuted(object p)
        {
            try
            {
                // 1. Получаем корневую папку для производства
                string rootFolder = AGR_Options.ProductionRootFolderPath;
                if (string.IsNullOrWhiteSpace(rootFolder))
                {
                    MessageBox.Show("Путь к папке производства не задан (AGR_Options.ProductionRootFolderPath).",
                        "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // 2. Делаем снимок непокупных компонентов для безопасной работы в фоновом потоке
                var componentsSnapshot = new List<ComponentItemViewModel>();
                foreach (var item in ComponentsView)
                {
                    if (item is ComponentItemViewModel component && !component.IsPurchased)
                    {
                        componentsSnapshot.Add(component);
                    }
                }

                if (!componentsSnapshot.Any())
                {
                    MessageBox.Show("Нет непокупных компонентов для обработки.", "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                int copiedFilesCount = 0;
                int skippedFilesCount = 0;
                var errors = new List<string>();

                // 3. Выполняем ресурсоемкую операцию копирования в фоновом потоке, чтобы не блокировать UI
                await Task.Run(() =>
                {
                    foreach (var component in componentsSnapshot)
                    {
                        // Обращаемся к версии компонента через созданное нами свойство
                        var compVersion = component.ComponentVersionEntity;
                        if (compVersion?.Files == null || !compVersion.Files.Any()) continue;

                        // Фильтруем файлы по нужным типам (StorageModel и StorageDrawing)
                        var targetFiles = compVersion.Files
                            .Where(f => f.FileType == AGR_FileType_e.StorageModel ||
                                        f.FileType == AGR_FileType_e.StorageDrawing)
                            .ToList();

                        if (!targetFiles.Any()) continue;

                        // Формируем путь: AGR_Options.ProductionRootFolderPath\PartNumber
                        string destinationFolder = Path.Combine(rootFolder, component.PartNumber);

                        try
                        {
                            // Создаем папку компонента, если она не существует
                            if (!Directory.Exists(destinationFolder))
                            {
                                Directory.CreateDirectory(destinationFolder);
                            }

                            foreach (var file in targetFiles)
                            {
                                // ВАЖНО: Замените 'file.FilePath' на реальное имя свойства в вашем классе ComponentFile, 
                                // в котором хранится физический путь к файлу на диске (например, file.Path, file.FileName или file.Url)
                                string sourcePath = file.FilePath;

                                // Проверяем, существует ли файл на диске
                                if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                                {
                                    skippedFilesCount++;
                                    continue;
                                }

                                // Формируем итоговый путь: ...\PartNumber\имя_файла
                                string destinationPath = Path.Combine(destinationFolder, Path.GetFileName(sourcePath));

                                try
                                {
                                    // Копируем файл с перезаписью
                                    File.Copy(sourcePath, destinationPath, overwrite: true);
                                    copiedFilesCount++;
                                }
                                catch (Exception ex)
                                {
                                    errors.Add($"Не удалось скопировать '{Path.GetFileName(sourcePath)}': {ex.Message}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"Ошибка создания папки для {component.PartNumber}: {ex.Message}");
                        }
                    }
                });

                // 4. Возвращаемся в UI поток и показываем результат пользователю
                string message = $"Экспорт файлов завершен.\n\n" +
                                 $"✅ Скопировано: {copiedFilesCount}\n" +
                                 $"⚠️ Пропущено (нет на диске): {skippedFilesCount}";

                if (errors.Any())
                {
                    message += $"\n\n❌ Ошибки ({errors.Count}):\n" + string.Join("\n", errors.Take(10));
                    if (errors.Count > 10) message += "\n... и еще ошибки.";
                }

                MessageBox.Show(message, "Результат экспорта", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Критическая ошибка при экспорте: {ex.Message}",
                    "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        #endregion

        #endregion

        #region PROPS

        // Индикатор загрузки
        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => Set(ref _isLoading, value);
        }

        private string _loadingStatus = "";
        public string LoadingStatus
        {
            get => _loadingStatus;
            set => Set(ref _loadingStatus, value);
        }
        #region SelectedGroupingMode
        private string _SelectedGroupingMode = "По типу"; // Значение по умолчанию
        public string SelectedGroupingMode
        {
            get => _SelectedGroupingMode;
            set
            {
                if (Set(ref _SelectedGroupingMode, value))
                {
                    RefreshGrouping();
                }
            }
        }
        #endregion

        #region HasSelectedComponents
        public bool HasSelectedComponents => Components.Any(c => c.IsSelected);
        private void NotifyHasSelectedComponentsChanged()
        {
            OnPropertyChanged(nameof(HasSelectedComponents));
        }
        #endregion

        #region Коллекция компонентов
        private ObservableCollection<ComponentItemViewModel> _Components = new ObservableCollection<ComponentItemViewModel>();
        public ObservableCollection<ComponentItemViewModel> Components
        {
            get => _Components;
            set => Set(ref _Components, value);
        }
        #endregion

        #region CollectionViewSource
        private CollectionViewSource Components_CVS = new CollectionViewSource();
        public ICollectionView ComponentsView => Components_CVS?.View;
        #endregion

        #region Коллекция выбранных компонентов
        private ObservableCollection<ComponentItemViewModel> _SelectedComponents = new ObservableCollection<ComponentItemViewModel>();
        public ObservableCollection<ComponentItemViewModel> SelectedComponents
        {
            get => _SelectedComponents;
            set => Set(ref _SelectedComponents, value);
        }
        #endregion

        #region HasErrors
        private bool _hasErrors;
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

        #endregion

        #region Methods

        #region Метод обновления группировки
        private void RefreshGrouping()
        {
            Components_CVS.GroupDescriptions.Clear();
            ComponentsView.Filter = null;

            switch (SelectedGroupingMode)
            {
                case "По материалу":
                Components_CVS.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ComponentItemViewModel.Material)));
                ComponentsView.Filter = FilterGroupedItemByMaterial;
                break;
                case "По типу":
                Components_CVS.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ComponentItemViewModel.ComponentType)));
                break;
                default:
                break;
            }

            Components_CVS.View.Refresh();
        }
        #endregion

        #region Фильтр при группировке по материалу
        private bool FilterGroupedItemByMaterial(object item)
        {
            if (item is not ComponentItemViewModel compItem) return false;
            if (compItem.ComponentType is AGR_ComponentType_e.Purchased) return false;

            return true;
        }
        #endregion

        #region Сохранение изменений
        private async Task SaveChangesAsync()
        {
            try
            {
                await _unitOfWork.CompleteAsync();
                _logger?.LogInformation("Изменения сохранены.");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Ошибка при сохранении изменений.");
            }
        }
        #endregion

        #region Снятие выделения со всех компонентов
        private void DeselectAllComponents()
        {
            foreach (var component in Components)
            {
                component.IsSelected = false;
            }
            NotifyHasSelectedComponentsChanged();
        }
        #endregion
        private async Task LoadAssemblyStructureAsync(string partNumber, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(partNumber))
            {
                _logger.LogWarning("PartNumber пуст при попытке загрузки структуры.");
                return;
            }

            try
            {
                _logger.LogInformation($"Загрузка структуры для PartNumber: {partNumber}");
                LoadingStatus = "Загрузка структуры сборки...";

                Components.Clear();

                // 1. Загружаем данные из БД (это уже асинхронно)
                var assemblyVersion = await _dataContext.ComponentVersions
                    .Include(cv => cv.Component)
                        .ThenInclude(c => c.TechnologicalProcess)
                    .Include(c => c.Material)
                    .Include(cv => cv.Properties)
                    .Include(cv => cv.Files)
                    .Include(cv => cv.AvaArticle)
                    .Where(cv => cv.Component.PartNumber == partNumber)
                    .OrderByDescending(cv => cv.Version)
                    .FirstOrDefaultAsync(cancellationToken);

                if (assemblyVersion == null)
                {
                    _logger.LogWarning($"Сборка с PartNumber {partNumber} не найдена.");
                    return;
                }

                cancellationToken.ThrowIfCancellationRequested();
                LoadingStatus = "Загрузка техпроцессов...";

                var assemblyStructureEntries = await _unitOfWork.ComponentRepository
                    .GetAssemblyStructureRecursive(partNumber, assemblyVersion.Version);

                var groupedEntries = assemblyStructureEntries
                    .GroupBy(s => s.ChildComponentVersion.Component.PartNumber)
                    .Select(g => new
                    {
                        PartNumber = g.Key,
                        FirstComponentVersion = g.First().ChildComponentVersion,
                        TotalQuantity = g.Sum(s => s.Quantity)
                    })
                    .ToList();

                var partNumbersInStructure = groupedEntries.Select(entry => entry.PartNumber).Distinct().ToList();
                var techProcesses = await _dataContext.TechProcesses
                    .Include(tp => tp.Operations)
                    .Where(tp => partNumbersInStructure.Contains(tp.PartNumber))
                    .ToListAsync(cancellationToken);

                // 2. 🔥 КЛЮЧЕВОЕ ИЗМЕНЕНИЕ: Выносим создание ViewModel в фоновый поток!
                LoadingStatus = $"Обработка {groupedEntries.Count} компонентов...";

                var viewModels = await Task.Run(() =>
                {
                    var result = new List<ComponentItemViewModel>();

                    // Создаем главную сборку
                    var vmMainAssembly = CreateComponentViewModel(assemblyVersion, _dataContext, _unitOfWork);
                    var assemblyTP = _unitOfWork.TechProcessRepository.GetByPartNumberAsync(vmMainAssembly.PartNumber).Result;

                    if (assemblyTP?.Operations.Any() == true)
                    {
                        var operationsList = assemblyTP.Operations
                            .Select(op => new TechOperationViewModel(op) { ParentComponent = vmMainAssembly })
                            .OrderBy(o => o.SequenceNumber)
                            .ToList();
                        vmMainAssembly.Operations = new ObservableCollection<TechOperationViewModel>(operationsList);
                    }

                    result.Add(vmMainAssembly);

                    // Создаем остальные компоненты
                    foreach (var groupedEntry in groupedEntries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var compVer = groupedEntry.FirstComponentVersion;
                        var comp = compVer.Component;
                        var partNum = comp.PartNumber;
                        var totalQty = groupedEntry.TotalQuantity;
                        var props = compVer.Properties;
                        var materials = compVer.Material;

                        var materialProp = materials?.BaseMaterial;
                        var paintProp = materials?.Paint;
                        var bendCountProp = props.FirstOrDefault(p => p.Name == AGR_PropertyNames.BlankBends)?.Value;
                        var blankOuterContourProp = props.FirstOrDefault(p => p.Name == AGR_PropertyNames.BlankOuterContour)?.Value;
                        var blankInnerContourProp = props.FirstOrDefault(p => p.Name == AGR_PropertyNames.BlankInnerContour)?.Value;

                        var contourSum = (decimal.TryParse(blankInnerContourProp, out decimal innerLen) ? innerLen : 0) +
                                         (decimal.TryParse(blankOuterContourProp, out decimal outerLen) ? outerLen : 0);

                        var previewImage = compVer.PreviewImage != null ? LoadImageFromBytes(compVer.PreviewImage) : null;

                        var techProcess = techProcesses.FirstOrDefault(tp => tp.PartNumber == partNum);

                        var vm = new ComponentItemViewModel(_dataContext, _unitOfWork, compVer)
                        {
                            PartNumber = partNum,
                            Name = compVer.Name,
                            Quantity = totalQty,
                            Version = compVer.Version,
                            Material = materialProp ?? "",
                            Paint = paintProp ?? "",
                            BendCount = int.TryParse(bendCountProp, out int bc) ? bc : 0,
                            ContourLength = contourSum,
                            ComponentType = compVer.ComponentType,
                            PreviewImage = previewImage,
                            Article = compVer.AvaArticleArticle.ToString(),
                            AvaArticle = compVer.AvaArticle
                        };

                        var operationsList = techProcess?.Operations
                            .Select(op => new TechOperationViewModel(op) { ParentComponent = vm })
                            .OrderBy(o => o.SequenceNumber)
                            .ToList();

                        if (operationsList?.Any() == true)
                        {
                            vm.Operations = new ObservableCollection<TechOperationViewModel>(operationsList);
                            vm.TechnologicalProcessModel.Operations = vm.Operations;
                        }

                        var propsVM = props.Select(prop => new AGR_PropertyViewModel(prop)).ToList();
                        foreach (var prop in propsVM)
                        {
                            vm.PropertiesCollection.Add(prop);
                        }

                        result.Add(vm);
                    }

                    return result;
                }, cancellationToken);

                var sortedComponents = viewModels
                    .OrderBy(c => c.ComponentType switch
                    {
                        AGR_ComponentType_e.Assembly => 0,
                        AGR_ComponentType_e.SheetMetallPart => 1,
                        AGR_ComponentType_e.Part => 2,
                        AGR_ComponentType_e.Purchased => 3,
                        _ => 4
                    })
                    .ToList();

                // 3. Возвращаемся в UI поток и добавляем готовые ViewModel
                foreach (var vm in sortedComponents)
                {
                    Components.Add(vm);
                }

                Components_CVS.Source = Components;
                _logger.LogInformation($"Загружено {Components.Count} компонентов для сборки {partNumber}.");

                RefreshGrouping();
                ComponentsView.Refresh();
                OnPropertyChanged(nameof(ComponentsView));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при загрузке структуры сборки {partNumber}");
                throw;
            }
        }

        // Вспомогательный метод для создания ViewModel
        private ComponentItemViewModel CreateComponentViewModel(
            ComponentVersion version,
            DataContext dataContext,
            UnitOfWork unitOfWork)
        {
            return new ComponentItemViewModel(dataContext, unitOfWork, version)
            {
                PartNumber = version.Component.PartNumber,
                Name = version.Name,
                Version = version.Version,
                Quantity = 1,
                Material = "",
                Paint = version.Material?.Paint,
                BendCount = 0,
                ContourLength = 0,
                ComponentType = version.ComponentType,
                PreviewImage = LoadImageFromBytes(version.PreviewImage),
                Article = version.AvaArticleArticle.ToString(),
                AvaArticle = version.AvaArticle,
            };
        }
        private async void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Добавляем debounce для валидации при частых изменениях
            await Task.Delay(300); // Ждем 300мс перед валидацией
            await ValidateSpecificationAsync();
        }
        private async Task LoadAssemblyStructureAsync(string partNumber)
        {
            if (string.IsNullOrWhiteSpace(partNumber))
            {
                _logger.LogWarning("PartNumber пуст при попытке загрузки структуры.");
                return;
            }

            try
            {
                _logger.LogInformation($"Загрузка структуры для PartNumber: {partNumber}");

                // Очищаем текущую коллекцию
                Components.Clear();

                // Найдем версию сборки по PartNumber (берем последнюю по версии)
                var assemblyVersion = await _dataContext.ComponentVersions
                    .Include(cv => cv.Component) // Загружаем связанный компонент
                        .ThenInclude(c => c.TechnologicalProcess)
                    .Include(c => c.Material)
                    .Include(cv => cv.Properties) // Загружаем свойства
                    .Where(cv => cv.Component.PartNumber == partNumber)
                    .OrderByDescending(cv => cv.Version) // Берем последнюю версию
                    .FirstOrDefaultAsync();

                if (assemblyVersion == null)
                {
                    _logger.LogWarning($"Сборка с PartNumber {partNumber} не найдена.");
                    return;
                }

                //добавляем саму сборку
                var vmMainAssembly = new ComponentItemViewModel(_dataContext, _unitOfWork, assemblyVersion)
                {
                    PartNumber = assemblyVersion.Component.PartNumber,
                    Name = assemblyVersion.Name,
                    Version = assemblyVersion.Version,
                    Quantity = 1,
                    Material = "",
                    Paint = assemblyVersion.Material?.Paint,
                    BendCount = 0,
                    ContourLength = 0,
                    ComponentType = assemblyVersion.ComponentType,
                    PreviewImage = LoadImageFromBytes(assemblyVersion.PreviewImage),
                    Article = assemblyVersion.AvaArticleArticle.ToString(),
                    AvaArticle = assemblyVersion.AvaArticle
                };
                var assemblyTP = await _unitOfWork.TechProcessRepository.GetByPartNumberAsync(vmMainAssembly.PartNumber);
                if (assemblyTP != null)
                {
                    if (assemblyTP.Operations.Count != 0)
                    {
                        var operationsList = assemblyTP.Operations.Select(
                            op => new TechOperationViewModel(op)
                            {
                                ParentComponent = vmMainAssembly
                            })
                            .OrderBy(o => o.SequenceNumber).ToList();
                        vmMainAssembly.Operations = new ObservableCollection<TechOperationViewModel>(operationsList);
                    }
                }


                Components.Add(vmMainAssembly);

                // Найдем все элементы структуры для этой версии сборки
                var assemblyStructureEntries = await _unitOfWork.ComponentRepository.GetAssemblyStructureRecursive(partNumber, assemblyVersion.Version);

                var groupedEntries = assemblyStructureEntries
                    .GroupBy(s => s.ChildComponentVersion.Component.PartNumber) // Группируем по PartNumber
                    .Select(g => new
                    {
                        PartNumber = g.Key,
                        FirstComponentVersion = g.First().ChildComponentVersion,
                        TotalQuantity = g.Sum(s => s.Quantity) // Суммируем Quantity
                    })
                    .ToList();

                // Загрузим все связанные TechnologicalProcesses за один запрос
                var partNumbersInStructure = groupedEntries.Select(entry => entry.PartNumber).Distinct().ToList();
                var techProcesses = await _dataContext.TechProcesses // Предполагаем, что DbSet называется TechProcesses
                    .Include(tp => tp.Operations)
                    .Where(tp => partNumbersInStructure.Contains(tp.PartNumber))
                    .ToListAsync();

                foreach (var groupedEntry in groupedEntries)
                {
                    var compVer = groupedEntry.FirstComponentVersion;
                    var comp = compVer.Component;
                    var partNum = comp.PartNumber;
                    var totalQty = groupedEntry.TotalQuantity; // Используем суммарное количество
                    var props = compVer.Properties;
                    var materials = compVer.Material; // Предполагаем, что Material связан с ComponentVersion

                    // Загрузим нужные свойства из ComponentProperties (берем из первой версии в группе)
                    // Используем AGR_PropertyNames для получения свойств
                    var materialProp = materials?.BaseMaterial;
                    var paintProp = materials?.Paint;
                    var bendCountProp = props.FirstOrDefault(p => p.Name == AGR_PropertyNames.BlankBends)?.Value; // Получаем .Value
                    var blankOuterContourProp = props.FirstOrDefault(p => p.Name == AGR_PropertyNames.BlankOuterContour)?.Value; // Получаем .Value
                    var blankInnerContourProp = props.FirstOrDefault(p => p.Name == AGR_PropertyNames.BlankInnerContour)?.Value; // Получаем .Value

                    // Парсим длину контура
                    var contourSum = (decimal.TryParse(blankInnerContourProp, out decimal innerLen) ? innerLen : 0) +
                                     (decimal.TryParse(blankOuterContourProp, out decimal outerLen) ? outerLen : 0);

                    var previewImage = compVer.PreviewImage != null ? LoadImageFromBytes(compVer.PreviewImage) : null;

                    // Найдем соответствующий техпроцесс по PartNumber
                    var techProcess = techProcesses.FirstOrDefault(tp => tp.PartNumber == partNum);


                    var vm = new ComponentItemViewModel(_dataContext, _unitOfWork)
                    {
                        PartNumber = partNum,
                        Name = compVer.Name,
                        Quantity = totalQty,
                        Version = compVer.Version,
                        Material = materialProp ?? "",
                        Paint = paintProp ?? "",
                        BendCount = int.TryParse(bendCountProp, out int bc) ? bc : 0,
                        ContourLength = contourSum,
                        ComponentType = compVer.ComponentType,
                        PreviewImage = previewImage,
                        Article = compVer.AvaArticleArticle.ToString(),
                        AvaArticle = compVer.AvaArticle
                    };
                    var operationsList = techProcess?.Operations
                        .Select(op => new TechOperationViewModel(op)
                        {
                            ParentComponent = vm
                        })
                        .OrderBy(o => o.SequenceNumber)
                        .ToList();

                    // Заполняем Operations и TechProcessSummary из найденного TechnologicalProcess
                    if (operationsList != null && operationsList.Count != 0)
                    {
                        vm.Operations = new ObservableCollection<TechOperationViewModel>(operationsList);
                        vm.TechnologicalProcessModel.Operations = vm.Operations;
                    }

                    var propsVM = props.Select(prop => new AGR_PropertyViewModel(prop)).ToList();
                    if (propsVM != null)
                    {
                        foreach (var prop in propsVM)
                        {
                            vm.PropertiesCollection.Add(prop);
                        }
                    }

                    Components.Add(vm);
                }

                // Обновляем CollectionViewSource Source, чтобы он отслеживал изменения в коллекции
                Components_CVS.Source = Components;

                _logger.LogInformation($"Загружено {Components.Count} уникальных компонентов (суммарные количества) для сборки {partNumber}.");

                // Обновляем группировку
                RefreshGrouping();
                ComponentsView.Refresh();

                OnPropertyChanged(nameof(ComponentsView));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при загрузке структуры сборки {partNumber}");
            }
        }
        private async Task LoadPartDataAsync(string partNumber, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(partNumber))
            {
                _logger.LogWarning("PartNumber пуст при попытке загрузки данных.");
                return;
            }

            _logger.LogInformation($"Загрузка данных для PartNumber: {partNumber}");

            // Очищаем текущую коллекцию
            Components.Clear();

            // Найдем версию сборки по PartNumber (берем последнюю по версии)
            var partVersion = await _dataContext.ComponentVersions
                .Include(cv => cv.Component) // Загружаем связанный компонент
                    .ThenInclude(c => c.TechnologicalProcess)
                .Include(c => c.Material)
                .Include(cv => cv.Properties) // Загружаем свойства
                .Where(cv => cv.Component.PartNumber == partNumber)
                .OrderByDescending(cv => cv.Version) // Берем последнюю версию
                .FirstOrDefaultAsync();

            if (partVersion == null)
            {
                _logger.LogWarning($"Деталь с PartNumber {partNumber} не найдена.");
                return;
            }
            var props = partVersion.Properties;
            var bendCountProp = props.FirstOrDefault(p => p.Name == AGR_PropertyNames.BlankBends)?.Value; // Получаем .Value
            var blankOuterContourProp = props.FirstOrDefault(p => p.Name == AGR_PropertyNames.BlankOuterContour)?.Value; // Получаем .Value
            var blankInnerContourProp = props.FirstOrDefault(p => p.Name == AGR_PropertyNames.BlankInnerContour)?.Value; // Получаем .Value

            // Парсим длину контура
            var contourSum = (decimal.TryParse(blankInnerContourProp, out decimal innerLen) ? innerLen : 0) +
                             (decimal.TryParse(blankOuterContourProp, out decimal outerLen) ? outerLen : 0);

            //добавляем саму сборку
            var vmMainPart = new ComponentItemViewModel(_dataContext, _unitOfWork)
            {
                PartNumber = partVersion.Component.PartNumber,
                Name = partVersion.Name,
                Version = partVersion.Version,
                Quantity = 1,
                Material = partVersion.Material?.BaseMaterial,
                Paint = partVersion.Material?.Paint,
                ComponentType = partVersion.ComponentType,
                PreviewImage = LoadImageFromBytes(partVersion.PreviewImage),
                Article = partVersion.AvaArticleArticle.ToString(),
                AvaArticle = partVersion.AvaArticle,
                BendCount = int.TryParse(bendCountProp, out int bc) ? bc : 0,
                ContourLength = contourSum

            };

            var assemblyTP = await _unitOfWork.TechProcessRepository.GetByPartNumberAsync(vmMainPart.PartNumber);
            if (assemblyTP != null)
            {
                if (assemblyTP.Operations.Count != 0)
                {
                    var operationsList = assemblyTP.Operations.Select(
                        op => new TechOperationViewModel(op)
                        {
                            ParentComponent = vmMainPart
                        })
                        .OrderBy(o => o.SequenceNumber).ToList();
                    vmMainPart.Operations = new ObservableCollection<TechOperationViewModel>(operationsList);
                }
            }

            Components.Add(vmMainPart);

            // Обновляем CollectionViewSource Source, чтобы он отслеживал изменения в коллекции
            Components_CVS.Source = Components;

            _logger.LogInformation($"Загружено {Components.Count} уникальных компонентов (суммарные количества) для сборки {partNumber}.");

            // Обновляем группировку
            RefreshGrouping();
            ComponentsView.Refresh();

            OnPropertyChanged(nameof(ComponentsView));
        }

        #region Вспомогательный метод для загрузки изображения
        private BitmapImage? LoadImageFromBytes(byte[] imageData)
        {
            try
            {
                using var ms = new System.IO.MemoryStream(imageData);
                var image = new BitmapImage();
                image.BeginInit();
                image.StreamSource = ms;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze(); // Оптимизация для UI Thread
                return image;
            }
            catch
            {
                return null;
            }
        }
        #endregion
        public void ValidateSpecification()
        {
            Errors = null;
            HasErrors = false;
            Warnings = null;
            HasWarnings = false;

            var errorList = new List<string>();
            var warningsList = new List<string>();

            //у каждой производимой детали должен быть указан материал
            foreach (var item in Components)
            {

                //проверяем чтобы было указано количество
                if (item.Quantity == 0)
                    errorList.Add($"У компонента {item.PartNumber}.{item.Name} не указано количество");

                if (item.IsProduced)
                {
                    if (item.IsPart)
                    {
                        //проверяем указан ли материал
                        if (string.IsNullOrEmpty(item.Material))
                        {
                            errorList.Add($"У компонента {item.PartNumber}.{item.Name} не указан материал");
                        }
                        //проверяем обязательные свойства листовой детали
                        if (item.IsSheetMetallPart)
                        {
                            //у каждой листовой детали должен быть указан контур
                            //если свойство есть но пустое, добавляем ошибку
                            if (string.IsNullOrEmpty(item.ContourLength.ToString())
                                || string.IsNullOrWhiteSpace(item.ContourLength.ToString())
                                || item.ContourLength == 0)
                            {
                                errorList.Add($"У компонента {item.PartNumber}.{item.Name} не указана сумма контуров резки");
                            }

                            //у каждой листовой детали должно быть указано количество сгибов
                            if (string.IsNullOrEmpty(item.BendCount.ToString()) || string.IsNullOrWhiteSpace(item.BendCount.ToString()))
                            {
                                errorList.Add($"У компонента {item.PartNumber}.{item.Name} не указано количество сгибов");
                            }
                        }
                    }
                    //у всех производимых должен быть техпроцесс
                    //if (item.TechnologicalProcessModel == null)
                    //errorList.Add($"У компонента {item.PartNumber}.{item.Name} нет техпроцесса");

                    //в каждом техпроцессе должна быть как минимум 1 операция
                    if (item.Operations.Count == 0)
                        errorList.Add($"У компонента {item.PartNumber}.{item.Name} нет ни одной операции в техпроцессе");
                    else
                    {
                        foreach (var operation in item.Operations)
                        {
                            //у каждой операции техпроцесса должна быть заполнена трудоемкость, трудоемкость не должна равняться нулю
                            if (string.IsNullOrEmpty(operation.CostPerHour.ToString()) || operation.CostPerHour == 0)
                                errorList.Add($"У компонента {item.PartNumber}.{item.Name} не заполнена трудоемкость для операции #{operation.SequenceNumber}.{operation.Name}");
                        }
                    }
                    //Если у производимого указан цвет, обязательно должна быть операция покраски
                    if (!string.IsNullOrEmpty(item.Paint) && item.Paint != "Без покраски")
                    {
                        if (!item.Operations.Any(x => x.Name == "Покраска"))
                        {
                            errorList.Add($"У компонента {item.PartNumber}.{item.Name} указан цвет покраски но нет операции покраски");
                        }
                    }
                    if (item.Paint == "Без покраски" || string.IsNullOrEmpty(item.Paint))
                    {
                        //Операция покраски не может быть указана если не указан цвет
                        if (!item.Operations.Any(x => x.Name == "Покраска"))
                        {
                            errorList.Add($"У компонента {item.PartNumber}.{item.Name} не указан цвет покраски но есть операции покраски");
                        }
                    }
                }
            }


            if (errorList.Any())
            {
                Errors = string.Join("\n", errorList);
                HasErrors = true;
            }
            if (warningsList.Any())
            {
                Warnings = string.Join("\n", warningsList);
                HasWarnings = true;
            }
        }
        public async Task ValidateSpecificationAsync()
        {
            // 1. Сбрасываем ошибки сразу в UI потоке (чтобы интерфейс сразу отреагировал)
            Errors = null;
            HasErrors = false;

            Warnings = null;
            HasWarnings = false;

            // 2. ВАЖНО: Делаем "снимок" (копию) коллекции.
            // Если во время проверки в фоновом потоке пользователь или UI изменит 
            var componentsSnapshot = Components?.ToList();
            if (componentsSnapshot == null || !componentsSnapshot.Any()) return;

            // 3. Выносим ресурсоемкий перебор в фоновый поток пула потоков

            var result = await Task.Run(() =>
            {
                var localErrorList = new List<string>();
                var localWarningList = new List<string>();

                foreach (var item in componentsSnapshot)
                {

                    if (item.Quantity == 0)
                        localErrorList.Add($"У компонента {item.PartNumber}.{item.Name} не указано количество");

                    if (item.IsProduced)
                    {
                        if (item.IsPart)
                        {
                            if (string.IsNullOrEmpty(item.Material))
                            {
                                localErrorList.Add($"У компонента {item.PartNumber}.{item.Name} не указан материал");
                            }

                            if (item.IsSheetMetallPart)
                            {
                                // Убираем лишние ToString() и IsNullOrEmpty для чисел
                                if (item.ContourLength == 0)
                                {
                                    localErrorList.Add($"У компонента {item.PartNumber}.{item.Name} не указана сумма контуров резки");
                                }

                                if (item.BendCount == 0) // Аналогично для сгибов
                                {
                                    localWarningList.Add($"У компонента {item.PartNumber}.{item.Name} не указано количество сгибов");
                                }
                            }
                        }

                        if (item.Operations.Count == 0)
                        {
                            localErrorList.Add($"У компонента {item.PartNumber}.{item.Name} нет ни одной операции в техпроцессе");
                        }
                        else
                        {
                            foreach (var operation in item.Operations)
                            {
                                if (operation.CostPerHour == 0)
                                    localErrorList.Add($"У компонента {item.PartNumber}.{item.Name} не заполнена трудоемкость для операции #{operation.SequenceNumber}.{operation.Name}");
                            }
                        }

                        if (!string.IsNullOrEmpty(item.Paint) && item.Paint != "Без покраски")
                        {
                            if (!item.Operations.Any(x => x.Name == "Покраска"))
                            {
                                localErrorList.Add($"У компонента {item.PartNumber}.{item.Name} указан цвет покраски но нет операции покраски");
                            }
                        }

                        if (item.Paint == "Без покраски" || string.IsNullOrEmpty(item.Paint))
                        {
                            if (item.Operations.Any(x => x.Name == "Покраска"))
                            {
                                localErrorList.Add($"У компонента {item.PartNumber}.{item.Name} не указан цвет покраски но есть операции покраски");
                            }
                        }
                    }
                }

                return (localErrorList, localWarningList);
            });

            var errorList = result.localErrorList;
            var warningList = result.localWarningList;

            if (errorList.Any())
            {
                Errors = string.Join("\n", errorList);
                HasErrors = true;
            }
            if (errorList.Any())
            {
                Warnings = string.Join("\n", warningList);
                HasWarnings = true;
            }
        }
        #endregion

        public event EventHandler? CloseRequested;
    }
}
