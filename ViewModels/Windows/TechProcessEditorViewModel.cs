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
    /// Редактор технологического процесса.
    ///
    /// Важное правило доступа к БД:
    /// READ  -> scope -> query/materialize -> dispose.
    /// WRITE -> new scope -> load/attach -> modify -> SaveChanges -> dispose.
    /// BATCH WRITE выполняется одним scope и одной transaction (см. OperationSelectionViewModel).
    /// </summary>
    public class TechProcessEditorViewModel : BaseViewModel
    {
        private readonly ILogger? _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ComponentItemViewModel _selectedComponent;
        private CancellationTokenSource? _loadingCts;

        private ImportClassifierReportViewModel _importClassifierReportViewModel;
        private FullImportClassifierReportViewModel _importFullClassifierReportViewModel;
        private TreeImportReportViewModel _treeImportReportViewModel;
        private TechOpsImportReportViewModel _techOpsImportReportViewModel;

        public TechProcessEditorViewModel(
            ComponentItemViewModel selectedComponent,
            ILogger? logger,
            IServiceScopeFactory scopeFactory)
        {
            _selectedComponent = selectedComponent ?? throw new ArgumentNullException(nameof(selectedComponent));
            _logger = logger;
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        }

        public async Task InitializeAsync()
        {
            _loadingCts = new CancellationTokenSource();
            try
            {
                IsLoading = true;
                LoadingStatus = "Загрузка данных...";

                if (_selectedComponent.ComponentType == AGR_ComponentType_e.Assembly)
                    await LoadAssemblyStructureAsync(_selectedComponent.PartNumber, _loadingCts.Token);
                else if (_selectedComponent.ComponentType is AGR_ComponentType_e.Part or AGR_ComponentType_e.SheetMetallPart)
                    await LoadPartDataAsync(_selectedComponent.PartNumber, _loadingCts.Token);

                // ComponentsView должен быть создан до того, как XAML попытается
                // привязаться к нему. Дополнительно уведомляем binding после Source.
                Components_CVS.Source = Components;
                OnPropertyChanged(nameof(ComponentsView));
                RefreshGrouping();

                foreach (var item in Components)
                    item.PropertyChanged += Item_PropertyChanged;

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
                MessageBox.Show($"Ошибка при загрузке данных: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                IsLoading = false;
                LoadingStatus = "";
                _loadingCts?.Dispose();
                _loadingCts = null;
            }
        }

        #region Commands
        private ICommand _CloseCommand;
        public ICommand CloseCommand => _CloseCommand ??= new RelayCommand(OnCloseCommandExecuted, CanCloseCommandExecute);
        private bool CanCloseCommandExecute(object p) => true;
        private void OnCloseCommandExecuted(object p) => CloseRequested?.Invoke(this, EventArgs.Empty);

        private ICommand _SaveCommand;
        public ICommand SaveCommand => _SaveCommand ??= new RelayCommand(OnSaveCommandExecuted, CanSaveCommandExecute);
        private bool CanSaveCommandExecute(object p) => true;
        private void OnSaveCommandExecuted(object p)
        {
            // Все изменения БД сохраняются в момент конкретной write-операции.
            // Долгоживущего UnitOfWork здесь больше нет.
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private ICommand _InsertTechProcessCommand;
        public ICommand InsertTechProcessCommand => _InsertTechProcessCommand
            ??= new RelayCommand(OnInsertTechProcessCommandExecuted, CanInsertTechProcessCommandExecute);
        private bool CanInsertTechProcessCommandExecute(object p) => HasSelectedComponents;
        private void OnInsertTechProcessCommandExecuted(object p) => EditProcess();

        private void EditProcess()
        {
            var selectedComponents = Components.Where(c => c.IsSelected).ToList();
            if (!selectedComponents.Any()) return;

            _logger?.LogInformation($"Открытие окна выбора операции для {selectedComponents.Count} компонентов.");

            var operationSelectionViewModel = new OperationSelectionViewModel(
                new ObservableCollection<ComponentItemViewModel>(selectedComponents),
                _scopeFactory,
                _logger);

            var selectionWindow = new OperationSelectionWindow { DataContext = operationSelectionViewModel };
            operationSelectionViewModel.CloseRequested += (s, e) => selectionWindow.Close();
            selectionWindow.Closed += SelectionWindow_Closed;
            selectionWindow.ShowDialog();
        }

        private void SelectionWindow_Closed(object? sender, EventArgs e) => DeselectAllComponents();

        private ICommand _SelectComponentCommand;
        public ICommand SelectComponentCommand => _SelectComponentCommand
            ??= new RelayCommand(OnSelectComponentCommandExecuted, CanSelectComponentCommandExecute);
        private bool CanSelectComponentCommandExecute(object p) => true;
        private void OnSelectComponentCommandExecuted(object p)
        {
            foreach (var item in SelectedComponents)
                item.IsSelected = !item.IsSelected;
            NotifyHasSelectedComponentsChanged();
        }

        private ICommand _DeleteOperationCommand;
        public ICommand DeleteOperationCommand => _DeleteOperationCommand
            ??= new RelayCommand<TechOperationViewModel>(OnDeleteOperationCommandExecuted, p => p != null);

        /// <summary>
        /// WRITE: новый scope -> load -> delete -> SaveChanges -> dispose.
        /// </summary>
        private async void OnDeleteOperationCommandExecuted(TechOperationViewModel? operation)
        {
            if (operation?.ParentComponent == null) return;

            if (operation.TechProcess != null)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var dataContext = scope.ServiceProvider.GetRequiredService<DataContext>();
                    var entOp = await dataContext.Operations.FirstOrDefaultAsync(o =>
                        o.TechnologicalProcessId == operation.TechProcess.Id &&
                        o.SequenceNumber == operation.SequenceNumber);

                    if (entOp != null)
                    {
                        dataContext.Operations.Remove(entOp);
                        await dataContext.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Ошибка удаления операции.");
                    return;
                }
            }

            operation.ParentComponent.Operations.Remove(operation);
            OnPropertyChanged(nameof(ComponentItemViewModel.HasZeroTimeOperations));
        }

        private ICommand _SetPaintCommand;
        public ICommand SetPaintCommand => _SetPaintCommand
            ??= new RelayCommand(OnSetPaintCommandExecuted, CanSetPaintCommandExecute);

        public bool CanSetPaintCommandExecute(object p)
        {
            var selected = Components.Any(x => x.IsSelected) ? Components.Where(x => x.IsSelected) : SelectedComponents;
            return !selected.Any(x => x.ComponentType == AGR_ComponentType_e.Purchased);
        }

        private void OnSetPaintCommandExecuted(object p)
        {
            var selected = Components.Any(x => x.IsSelected) ? Components.Where(x => x.IsSelected).ToList() : SelectedComponents.ToList();
            if (p?.ToString() == "NoPaint")
            {
                foreach (var item in selected) item.Paint = null;
                DeselectAllComponents();
                return;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var scopedDataContext = scope.ServiceProvider.GetRequiredService<DataContext>();
                var selectVm = new AGR_SelectAvaArticleVM(scopedDataContext)
                {
                    SearchText = "Краска порошковая",
                    SelectedAvaType = "Товар"
                };
                var selectView = new AGR_SelectAvaArticleView { DataContext = selectVm, ShowActivated = true };
                selectView.ShowDialog();

                if (selectVm.IsDialogResultAccepted == true && selectVm.SelectedArticle != null)
                {
                    foreach (var item in selected)
                    {
                        item.Paint = selectVm.SelectedArticle.Name;
                        OnPropertyChanged(nameof(item.PartnumberOrArticle));
                    }
                }

                DeselectAllComponents();
                ValidateSpecification();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Ошибка при выборе покрытия.");
            }
        }

        #region Reports
        private ICommand _ShowImportClassifierReportCommand;
        public ICommand ShowImportClassifierReportCommand => _ShowImportClassifierReportCommand
            ??= new RelayCommand(OnShowImportClassifierReportCommandExecuted, p => true);
        private void OnShowImportClassifierReportCommandExecuted(object p)
        {
            try
            {
                _importClassifierReportViewModel = new ImportClassifierReportViewModel(Components);
                new ImportClassifierReportWindow(_importClassifierReportViewModel)
                {
                    Width = 1000, Height = 700, ResizeMode = ResizeMode.CanResizeWithGrip
                }.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при открытии отчета: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private ICommand _ShowImportFullClassifierReportCommand;
        public ICommand ShowImportFullClassifierReportCommand => _ShowImportFullClassifierReportCommand
            ??= new RelayCommand(OnShowImportFullClassifierReportCommandExecuted, p => true);
        private void OnShowImportFullClassifierReportCommandExecuted(object p)
        {
            try
            {
                _importFullClassifierReportViewModel = new FullImportClassifierReportViewModel(Components);
                new ImportFullClassifierReportWindow(_importFullClassifierReportViewModel)
                {
                    Width = 1000, Height = 700, ResizeMode = ResizeMode.CanResizeWithGrip
                }.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при открытии отчета: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private ICommand _ShowTreeImportReportCommand;
        public ICommand ShowTreeImportReportCommand => _ShowTreeImportReportCommand
            ??= new RelayCommand(OnShowTreeImportReportCommandExecuted, p => true);
        private void OnShowTreeImportReportCommandExecuted(object p)
        {
            var mainComponent = Components.FirstOrDefault();
            if (mainComponent == null)
            {
                MessageBox.Show("Нет доступных компонентов.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                var vm = new TreeImportReportViewModel(mainComponent, _scopeFactory);
                var reportWindow = new TreeImportReportWindow(vm)
                {
                    Width = 1200, Height = 800, ResizeMode = ResizeMode.CanResizeWithGrip
                };
                reportWindow.ShowDialog();

                if (vm.IsExcelSaved == true)
                {
                    var result = MessageBox.Show("Отчет успешно сохранен в Excel. Экспортировать файлы в рабочий каталог?", "Экспорт файлов", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (result == DialogResult.Yes) ExportStorageFilesCommand?.Execute(null);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при открытии отчета: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private ICommand _ShowTechOpsImportReportCommand;
        public ICommand ShowTechOpsImportReportCommand => _ShowTechOpsImportReportCommand
            ??= new RelayCommand(OnShowTechOpsImportReportCommandExecuted, p => true);
        private void OnShowTechOpsImportReportCommandExecuted(object p)
        {
            if (!Components.Any())
            {
                MessageBox.Show("Нет доступных компонентов.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                _techOpsImportReportViewModel = new TechOpsImportReportViewModel(Components);
                new TechOpsImportReportWindow(_techOpsImportReportViewModel)
                {
                    Width = 1600, Height = 800, ResizeMode = ResizeMode.CanResizeWithGrip
                }.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при открытии отчета: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        #endregion

        private ICommand _ExportStorageFilesCommand;
        public ICommand ExportStorageFilesCommand => _ExportStorageFilesCommand
            ??= new RelayCommand(OnExportStorageFilesCommandExecuted, p => ComponentsView?.Cast<object>().Any() == true);

        private async void OnExportStorageFilesCommandExecuted(object p)
        {
            try
            {
                string rootFolder = AGR_Options.ProductionRootFolderPath;
                if (string.IsNullOrWhiteSpace(rootFolder))
                {
                    MessageBox.Show("Путь к папке производства не задан.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var componentsSnapshot = ComponentsView.Cast<object>()
                    .OfType<ComponentItemViewModel>()
                    .Where(x => !x.IsPurchased)
                    .ToList();

                if (!componentsSnapshot.Any())
                {
                    MessageBox.Show("Нет непокупных компонентов для обработки.", "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                int copiedFilesCount = 0;
                int skippedFilesCount = 0;
                var errors = new List<string>();

                await Task.Run(() =>
                {
                    foreach (var component in componentsSnapshot)
                    {
                        var compVersion = component.ComponentVersionEntity;
                        if (compVersion?.Files == null) continue;

                        var targetFiles = compVersion.Files
                            .Where(f => f.FileType == AGR_FileType_e.StorageModel || f.FileType == AGR_FileType_e.StorageDrawing)
                            .ToList();
                        if (!targetFiles.Any()) continue;

                        string destinationFolder = Path.Combine(rootFolder, component.PartNumber);
                        try
                        {
                            Directory.CreateDirectory(destinationFolder);
                            foreach (var file in targetFiles)
                            {
                                string sourcePath = file.FilePath;
                                if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                                {
                                    skippedFilesCount++;
                                    continue;
                                }

                                try
                                {
                                    File.Copy(sourcePath, Path.Combine(destinationFolder, Path.GetFileName(sourcePath)), true);
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

                string message = $"Экспорт файлов завершен.\n\nСкопировано: {copiedFilesCount}\nПропущено (нет на диске): {skippedFilesCount}";
                if (errors.Any())
                    message += $"\n\nОшибки ({errors.Count}):\n" + string.Join("\n", errors.Take(10));
                MessageBox.Show(message, "Результат экспорта", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Критическая ошибка при экспорте: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        #endregion

        #region PROPS
        private bool _isLoading;
        public bool IsLoading { get => _isLoading; set => Set(ref _isLoading, value); }

        private string _loadingStatus = "";
        public string LoadingStatus { get => _loadingStatus; set => Set(ref _loadingStatus, value); }

        private string _SelectedGroupingMode = "По типу";
        public string SelectedGroupingMode
        {
            get => _SelectedGroupingMode;
            set { if (Set(ref _SelectedGroupingMode, value)) RefreshGrouping(); }
        }

        public bool HasSelectedComponents => Components.Any(c => c.IsSelected);
        private void NotifyHasSelectedComponentsChanged() => OnPropertyChanged(nameof(HasSelectedComponents));

        private ObservableCollection<ComponentItemViewModel> _Components = new();
        public ObservableCollection<ComponentItemViewModel> Components { get => _Components; set => Set(ref _Components, value); }

        private CollectionViewSource Components_CVS = new();
        public ICollectionView ComponentsView => Components_CVS.View;

        private ObservableCollection<ComponentItemViewModel> _SelectedComponents = new();
        public ObservableCollection<ComponentItemViewModel> SelectedComponents { get => _SelectedComponents; set => Set(ref _SelectedComponents, value); }

        private bool _hasErrors;
        public bool HasErrors { get => _hasErrors; set => Set(ref _hasErrors, value); }

        private string _errors = "";
        public string Errors { get => _errors; set => Set(ref _errors, value); }

        private bool _HasWarnings;
        public bool HasWarnings { get => _HasWarnings; set => Set(ref _HasWarnings, value); }

        private string _Warnings = "";
        public string Warnings { get => _Warnings; set => Set(ref _Warnings, value); }
        #endregion

        #region Load
        private void RefreshGrouping()
        {
            if (Components_CVS.View == null) return;
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
            }
            ComponentsView.Refresh();
        }

        private bool FilterGroupedItemByMaterial(object item)
            => item is ComponentItemViewModel compItem && compItem.ComponentType != AGR_ComponentType_e.Purchased;

        /// <summary>
        /// READ: один scope на полный запрос структуры. После materialize scope закрывается,
        /// а ViewModel работает только с загруженным снимком данных.
        /// </summary>
        private async Task LoadAssemblyStructureAsync(string partNumber, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(partNumber)) return;

            LoadingStatus = "Загрузка структуры сборки...";
            Components.Clear();

            using var scope = _scopeFactory.CreateScope();
            var dataContext = scope.ServiceProvider.GetRequiredService<DataContext>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<UnitOfWork>();

            var assemblyVersion = await dataContext.ComponentVersions
                .Include(cv => cv.Component).ThenInclude(c => c.TechnologicalProcess).ThenInclude(tp => tp.Operations)
                .Include(cv => cv.Material)
                .Include(cv => cv.Properties)
                .Include(cv => cv.Files)
                .Include(cv => cv.AvaArticle)
                .Where(cv => cv.Component.PartNumber == partNumber)
                .OrderByDescending(cv => cv.Version)
                .FirstOrDefaultAsync(cancellationToken);

            if (assemblyVersion == null) return;
            cancellationToken.ThrowIfCancellationRequested();

            var assemblyStructureEntries = (await unitOfWork.ComponentRepository
                .GetAssemblyStructureRecursive(partNumber, assemblyVersion.Version)).ToList();

            var groupedEntries = assemblyStructureEntries
                .GroupBy(s => s.ChildComponentVersion.Component.PartNumber)
                .Select(g => new { PartNumber = g.Key, FirstComponentVersion = g.First().ChildComponentVersion, TotalQuantity = g.Sum(s => s.Quantity) })
                .ToList();

            var partNumbers = groupedEntries.Select(x => x.PartNumber).Distinct().ToList();
            var techProcesses = await dataContext.TechProcesses
                .Include(tp => tp.Operations)
                .Where(tp => partNumbers.Contains(tp.PartNumber))
                .ToListAsync(cancellationToken);

            LoadingStatus = $"Обработка {groupedEntries.Count} компонентов...";

            var result = new List<ComponentItemViewModel> { CreateComponentViewModel(assemblyVersion, _scopeFactory) };
            var assemblyVm = result[0];
            if (assemblyVersion.Component.TechnologicalProcess?.Operations?.Any() == true)
            {
                assemblyVm.Operations = new ObservableCollection<TechOperationViewModel>(assemblyVersion.Component.TechnologicalProcess.Operations
                    .Select(op => new TechOperationViewModel(op) { ParentComponent = assemblyVm })
                    .OrderBy(op => op.SequenceNumber));
            }

            foreach (var entry in groupedEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var compVer = entry.FirstComponentVersion;
                var comp = compVer.Component;
                var props = compVer.Properties;
                var material = compVer.Material;

                var bendCountProp = props.FirstOrDefault(p => p.Name == AGR_PropertyNames.BlankBends)?.Value;
                var outer = props.FirstOrDefault(p => p.Name == AGR_PropertyNames.BlankOuterContour)?.Value;
                var inner = props.FirstOrDefault(p => p.Name == AGR_PropertyNames.BlankInnerContour)?.Value;
                var contour = (decimal.TryParse(inner, out var innerLen) ? innerLen : 0)
                    + (decimal.TryParse(outer, out var outerLen) ? outerLen : 0);

                var vm = new ComponentItemViewModel(_scopeFactory, compVer)
                {
                    PartNumber = comp.PartNumber,
                    Name = compVer.Name,
                    Quantity = entry.TotalQuantity,
                    Version = compVer.Version,
                    Material = material?.BaseMaterial ?? "",
                    Paint = material?.Paint ?? "",
                    BendCount = int.TryParse(bendCountProp, out var bc) ? bc : 0,
                    ContourLength = contour,
                    ComponentType = compVer.ComponentType,
                    PreviewImage = compVer.PreviewImage != null ? LoadImageFromBytes(compVer.PreviewImage) : null,
                    Article = compVer.AvaArticleArticle.ToString(),
                    AvaArticle = compVer.AvaArticle
                };

                var techProcess = techProcesses.FirstOrDefault(tp => tp.PartNumber == entry.PartNumber);
                if (techProcess?.Operations?.Any() == true)
                {
                    vm.Operations = new ObservableCollection<TechOperationViewModel>(techProcess.Operations
                        .Select(op => new TechOperationViewModel(op) { ParentComponent = vm })
                        .OrderBy(op => op.SequenceNumber));
                    vm.TechnologicalProcessModel.Operations = vm.Operations;
                }

                foreach (var prop in props)
                    vm.PropertiesCollection.Add(new AGR_PropertyViewModel(prop));
                result.Add(vm);
            }

            foreach (var vm in result.OrderBy(c => c.ComponentType switch
            {
                AGR_ComponentType_e.Assembly => 0,
                AGR_ComponentType_e.SheetMetallPart => 1,
                AGR_ComponentType_e.Part => 2,
                AGR_ComponentType_e.Purchased => 3,
                _ => 4
            })) Components.Add(vm);
        }

        /// <summary>
        /// READ: scope -> load -> materialize -> dispose.
        /// </summary>
        private async Task LoadPartDataAsync(string partNumber, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(partNumber)) return;
            LoadingStatus = "Загрузка данных детали...";
            Components.Clear();

            using var scope = _scopeFactory.CreateScope();
            var dataContext = scope.ServiceProvider.GetRequiredService<DataContext>();

            var partVersion = await dataContext.ComponentVersions
                .Include(cv => cv.Component).ThenInclude(c => c.TechnologicalProcess).ThenInclude(tp => tp.Operations)
                .Include(cv => cv.Material)
                .Include(cv => cv.Properties)
                .Include(cv => cv.Files)
                .Include(cv => cv.AvaArticle)
                .Where(cv => cv.Component.PartNumber == partNumber)
                .OrderByDescending(cv => cv.Version)
                .FirstOrDefaultAsync(token);

            if (partVersion == null) return;
            var props = partVersion.Properties;
            var bend = props.FirstOrDefault(p => p.Name == AGR_PropertyNames.BlankBends)?.Value;
            var outer = props.FirstOrDefault(p => p.Name == AGR_PropertyNames.BlankOuterContour)?.Value;
            var inner = props.FirstOrDefault(p => p.Name == AGR_PropertyNames.BlankInnerContour)?.Value;
            var contour = (decimal.TryParse(inner, out var innerLen) ? innerLen : 0)
                + (decimal.TryParse(outer, out var outerLen) ? outerLen : 0);

            var vm = new ComponentItemViewModel(_scopeFactory, partVersion)
            {
                PartNumber = partVersion.Component.PartNumber,
                Name = partVersion.Name,
                Version = partVersion.Version,
                Quantity = 1,
                Material = partVersion.Material?.BaseMaterial,
                Paint = partVersion.Material?.Paint,
                ComponentType = partVersion.ComponentType,
                PreviewImage = partVersion.PreviewImage != null ? LoadImageFromBytes(partVersion.PreviewImage) : null,
                Article = partVersion.AvaArticleArticle.ToString(),
                AvaArticle = partVersion.AvaArticle,
                BendCount = int.TryParse(bend, out var bc) ? bc : 0,
                ContourLength = contour
            };

            if (partVersion.Component.TechnologicalProcess?.Operations?.Any() == true)
            {
                vm.Operations = new ObservableCollection<TechOperationViewModel>(partVersion.Component.TechnologicalProcess.Operations
                    .Select(op => new TechOperationViewModel(op) { ParentComponent = vm })
                    .OrderBy(op => op.SequenceNumber));
            }

            foreach (var prop in props)
                vm.PropertiesCollection.Add(new AGR_PropertyViewModel(prop));
            Components.Add(vm);
        }

        private ComponentItemViewModel CreateComponentViewModel(ComponentVersion version, IServiceScopeFactory scopeFactory)
            => new ComponentItemViewModel(scopeFactory, version)
            {
                PartNumber = version.Component.PartNumber,
                Name = version.Name,
                Version = version.Version,
                Quantity = 1,
                Material = version.Material?.BaseMaterial,
                Paint = version.Material?.Paint,
                ComponentType = version.ComponentType,
                PreviewImage = version.PreviewImage != null ? LoadImageFromBytes(version.PreviewImage) : null,
                Article = version.AvaArticleArticle.ToString(),
                AvaArticle = version.AvaArticle
            };

        private BitmapImage? LoadImageFromBytes(byte[] imageData)
        {
            try
            {
                using var ms = new MemoryStream(imageData);
                var image = new BitmapImage();
                image.BeginInit(); image.StreamSource = ms; image.CacheOption = BitmapCacheOption.OnLoad; image.EndInit(); image.Freeze();
                return image;
            }
            catch { return null; }
        }
        #endregion

        #region Validation
        private async void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            await Task.Delay(300);
            await ValidateSpecificationAsync();
        }

        public void ValidateSpecification()
        {
            Errors = null; HasErrors = false; Warnings = null; HasWarnings = false;
            var errorList = new List<string>();
            var warningList = new List<string>();

            foreach (var item in Components)
            {
                if (item.Quantity == 0) errorList.Add($"У компонента {item.PartNumber}.{item.Name} не указано количество");
                if (!item.IsProduced) continue;

                if (item.IsPart && string.IsNullOrEmpty(item.Material))
                    errorList.Add($"У компонента {item.PartNumber}.{item.Name} не указан материал");
                if (item.IsSheetMetallPart)
                {
                    if (item.ContourLength == 0) errorList.Add($"У компонента {item.PartNumber}.{item.Name} не указана сумма контуров резки");
                    if (item.BendCount == 0) warningList.Add($"У компонента {item.PartNumber}.{item.Name} не указано количество сгибов");
                }

                if (item.Operations.Count == 0)
                    errorList.Add($"У компонента {item.PartNumber}.{item.Name} нет ни одной операции в техпроцессе");
                else foreach (var operation in item.Operations)
                    if (operation.CostPerHour == 0)
                        errorList.Add($"У компонента {item.PartNumber}.{item.Name} не заполнена трудоемкость для операции #{operation.SequenceNumber}.{operation.Name}");

                if (!string.IsNullOrEmpty(item.Paint) && item.Paint != "Без покраски" && !item.Operations.Any(x => x.Name == "Покраска"))
                    errorList.Add($"У компонента {item.PartNumber}.{item.Name} указан цвет покраски но нет операции покраски");
                if ((item.Paint == "Без покраски" || string.IsNullOrEmpty(item.Paint)) && item.Operations.Any(x => x.Name == "Покраска"))
                    errorList.Add($"У компонента {item.PartNumber}.{item.Name} не указан цвет покраски но есть операции покраски");
            }

            if (errorList.Any()) { Errors = string.Join("\n", errorList); HasErrors = true; }
            if (warningList.Any()) { Warnings = string.Join("\n", warningList); HasWarnings = true; }
        }

        public async Task ValidateSpecificationAsync()
        {
            var snapshot = Components.ToList();
            await Task.Run(() =>
            {
                var errors = new List<string>();
                var warnings = new List<string>();
                foreach (var item in snapshot)
                {
                    if (item.Quantity == 0) errors.Add($"У компонента {item.PartNumber}.{item.Name} не указано количество");
                    if (!item.IsProduced) continue;
                    if (item.IsPart && string.IsNullOrEmpty(item.Material)) errors.Add($"У компонента {item.PartNumber}.{item.Name} не указан материал");
                    if (item.IsSheetMetallPart)
                    {
                        if (item.ContourLength == 0) errors.Add($"У компонента {item.PartNumber}.{item.Name} не указана сумма контуров резки");
                        if (item.BendCount == 0) warnings.Add($"У компонента {item.PartNumber}.{item.Name} не указано количество сгибов");
                    }
                    if (item.Operations.Count == 0) errors.Add($"У компонента {item.PartNumber}.{item.Name} нет ни одной операции в техпроцессе");
                    else foreach (var op in item.Operations)
                        if (op.CostPerHour == 0) errors.Add($"У компонента {item.PartNumber}.{item.Name} не заполнена трудоемкость для операции #{op.SequenceNumber}.{op.Name}");
                    if (!string.IsNullOrEmpty(item.Paint) && item.Paint != "Без покраски" && !item.Operations.Any(x => x.Name == "Покраска"))
                        errors.Add($"У компонента {item.PartNumber}.{item.Name} указан цвет покраски но нет операции покраски");
                    if ((item.Paint == "Без покраски" || string.IsNullOrEmpty(item.Paint)) && item.Operations.Any(x => x.Name == "Покраска"))
                        errors.Add($"У компонента {item.PartNumber}.{item.Name} не указан цвет покраски но есть операции покраски");
                }

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    Errors = errors.Any() ? string.Join("\n", errors) : null;
                    HasErrors = errors.Any();
                    Warnings = warnings.Any() ? string.Join("\n", warnings) : null;
                    HasWarnings = warnings.Any();
                });
            });
        }
        #endregion

        private void DeselectAllComponents()
        {
            foreach (var component in Components) component.IsSelected = false;
            NotifyHasSelectedComponentsChanged();
        }

        public event EventHandler? CloseRequested;
    }
}
