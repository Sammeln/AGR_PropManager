// File: ViewModels/MainWindowViewModel.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using AGR_PropManager.ViewModels.Base;
using System.Windows.Input;
using AGR_PropManager.Infrastructure.Commands;
using Agrovent.DAL;
using System.Windows.Media.Imaging;
using AgroventInfrastructure.Enums;
using AGR_PropManager.Views;
using AGR_PropManager.ViewModels.Components;
using AGR_PropManager.ViewModels.TechProcess;
using System.Windows;
using System.Windows.Controls;
using AGR_PropManager.ViewModels.Reports;
using Microsoft.Extensions.DependencyInjection;
using AgroventInfrastructure.Entities.Components;

namespace AGR_PropManager.ViewModels.Windows
{
    public class MainWindowViewModel : BaseViewModel
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger _logger;

        #region CTOR
        public MainWindowViewModel(IServiceScopeFactory scopeFactory, ILogger<MainWindowViewModel>? logger)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            ClassifierItems = new ObservableCollection<ClassifierItemViewModel>();
            ClassifierItemsView = CollectionViewSource.GetDefaultView(ClassifierItems);
            ApplyFilter();
        }

        public MainWindowViewModel() { }
        #endregion

        #region Commands

        private ICommand _LoadClassifierDataCommand;
        public ICommand LoadClassifierDataCommand => _LoadClassifierDataCommand
            ??= new RelayCommand(async (_) => await LoadClassifierDataAsync(), _ => !IsLoading);

        private ICommand _OpenItemTechProcessEditorCommand;
        public ICommand OpenItemTechProcessEditorCommand => _OpenItemTechProcessEditorCommand
            ??= new RelayCommand<ClassifierItemViewModel>(OnOpenItemTechProcessEditorCommandExecuted, CanOpenItemTechProcessEditorCommandExecute);

        private bool CanOpenItemTechProcessEditorCommandExecute(ClassifierItemViewModel? p) => p != null;

        private void OnOpenItemTechProcessEditorCommandExecuted(ClassifierItemViewModel? classifierItem)
        {
            if (classifierItem == null) return;

            _logger.LogInformation($"Открытие редактора процесса для компонента {classifierItem.PartNumber}.");

            // ViewModel редактора не получает ни DataContext, ни UnitOfWork.
            // Они создаются внутри короткоживущих scope для конкретных операций.
            var component = new ComponentItemViewModel(_scopeFactory)
            {
                PartNumber = classifierItem.PartNumber,
                Name = classifierItem.Name,
                PreviewImage = classifierItem.PreviewImage,
                ComponentType = classifierItem.ComponentType
            };

            var editorViewModel = new TechProcessEditorViewModel(component, _logger, _scopeFactory);
            var editorWindow = new TechProcessEditorWindow(editorViewModel)
            {
                Owner = Application.Current.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            editorWindow.ShowDialog();
        }

        #endregion

        #region PROPS

        private bool _IsLoading;
        public bool IsLoading
        {
            get => _IsLoading;
            set => Set(ref _IsLoading, value);
        }

        private string _SearchText = "";
        public string SearchText
        {
            get => _SearchText;
            set
            {
                if (Set(ref _SearchText, value))
                    ApplyFilter();
            }
        }

        private ObservableCollection<ClassifierItemViewModel> _ClassifierItems = new();
        public ObservableCollection<ClassifierItemViewModel> ClassifierItems
        {
            get => _ClassifierItems;
            set => Set(ref _ClassifierItems, value);
        }

        public ICollectionView ClassifierItemsView { get; private set; }

        #endregion

        #region Methods

        public async Task LoadClassifierDataAsync()
        {
            if (IsLoading) return;

            IsLoading = true;
            try
            {
                _logger.LogInformation("Загрузка данных классификатора...");

                IEnumerable<ComponentVersion> latestVersions;

                // READ: scope -> query -> materialize -> dispose.
                using (var scope = _scopeFactory.CreateScope())
                {
                    var unitOfWork = scope.ServiceProvider.GetRequiredService<UnitOfWork>();
                    latestVersions = (await unitOfWork.ComponentRepository.GetAllLatestComponentVersionsAsync()).ToList();
                }

                var items = await Task.Run(() =>
                {
                    var list = new List<ClassifierItemViewModel>();
                    foreach (var cv in latestVersions.Where(x => x.ComponentType != AGR_ComponentType_e.Purchased))
                    {
                        list.Add(new ClassifierItemViewModel
                        {
                            Id = cv.Id,
                            PartNumber = cv.Component.PartNumber,
                            Name = cv.Name,
                            SavedDate = cv.CreatedAt,
                            PreviewImage = cv.PreviewImage != null ? LoadImageFromBytes(cv.PreviewImage) : null,
                            ComponentType = cv.ComponentType,
                            ComponentAvaType = cv.AvaType
                        });
                    }
                    return list;
                });

                ClassifierItems = new ObservableCollection<ClassifierItemViewModel>(items);
                ClassifierItemsView = CollectionViewSource.GetDefaultView(ClassifierItems);
                ApplyFilter();
                OnPropertyChanged(nameof(ClassifierItemsView));

                _logger.LogInformation($"Загружено {ClassifierItems.Count} записей классификатора.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при загрузке данных классификатора");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void ApplyFilter()
        {
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                ClassifierItemsView.Filter = null;
            }
            else
            {
                string searchTextLower = SearchText.ToLower();
                ClassifierItemsView.Filter = item =>
                {
                    if (item is not ClassifierItemViewModel classifierItem) return false;

                    bool matchesPartNumber = !string.IsNullOrEmpty(classifierItem.PartNumber)
                        && classifierItem.PartNumber.ToLower().Contains(searchTextLower);
                    bool matchesName = !string.IsNullOrEmpty(classifierItem.Name)
                        && classifierItem.Name.ToLower().Contains(searchTextLower);

                    return matchesPartNumber || matchesName;
                };
            }

            ClassifierItemsView.Refresh();
        }

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
                image.Freeze();
                return image;
            }
            catch
            {
                return null;
            }
        }

        #endregion
    }
}
