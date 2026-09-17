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
using Agrovent.DAL.Services.Repositories;
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
            // Инициализируем пустую коллекцию и View
            ClassifierItems = new ObservableCollection<ClassifierItemViewModel>();
            ClassifierItemsView = CollectionViewSource.GetDefaultView(ClassifierItems);
            ApplyFilter();
        }

        // Пустой конструктор для дизайна
        public MainWindowViewModel() { }
        #endregion

        #region Commands

        #region LoadClassifierDataCommand
        private ICommand _LoadClassifierDataCommand;
        public ICommand LoadClassifierDataCommand => _LoadClassifierDataCommand
            ??= new RelayCommand(async (_) => await LoadClassifierDataAsync(), _ => !IsLoading); // Блокируем повторный вызов во время загрузки
        #endregion

        #region OpenItemTechProcessEditorCommand
        // ... (Ваш код для OpenItemTechProcessEditorCommand остается без изменений) ...
        private ICommand _OpenItemTechProcessEditorCommand;
        public ICommand OpenItemTechProcessEditorCommand => _OpenItemTechProcessEditorCommand
            ??= new RelayCommand<ClassifierItemViewModel>(OnOpenItemTechProcessEditorCommandExecuted, CanOpenItemTechProcessEditorCommandExecute);
        private bool CanOpenItemTechProcessEditorCommandExecute(ClassifierItemViewModel? p) => p != null;
        private void OnOpenItemTechProcessEditorCommandExecuted(ClassifierItemViewModel? classifierItem)
        {
            if (classifierItem == null) return;
            _logger.LogInformation($"Открытие редактора процесса для компонента {classifierItem.PartNumber}.");

            using (var scope = _scopeFactory.CreateScope())
            {
                var dataContext = scope.ServiceProvider.GetRequiredService<DataContext>();
                var unitOfWork = scope.ServiceProvider.GetRequiredService<UnitOfWork>();

                var component = new ComponentItemViewModel(dataContext, unitOfWork)
                {
                    PartNumber = classifierItem.PartNumber,
                    Name = classifierItem.Name,
                    PreviewImage = classifierItem.PreviewImage,
                    ComponentType = classifierItem.ComponentType
                };

                var editorViewModel = new TechProcessEditorViewModel(component, dataContext, _logger, unitOfWork, _scopeFactory);
                var editorWindow = new TechProcessEditorWindow(editorViewModel)
                {
                    Owner = Application.Current.MainWindow,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                editorWindow.ShowDialog(); // блокирует — scope не закроется, пока редактор открыт
            } // здесь DataContext редактора освобождается
        }
        #endregion

        #endregion

        #region PROPS

        #region IsLoading (Новое свойство для индикации загрузки)
        private bool _IsLoading;
        public bool IsLoading
        {
            get => _IsLoading;
            set => Set(ref _IsLoading, value);
        }
        #endregion

        #region SearchText
        private string _SearchText = "";
        public string SearchText
        {
            get => _SearchText;
            set
            {
                if (Set(ref _SearchText, value))
                {
                    ApplyFilter();
                }
            }
        }
        #endregion

        #region Коллекция ClassifierItems
        private ObservableCollection<ClassifierItemViewModel> _ClassifierItems = new ObservableCollection<ClassifierItemViewModel>();
        public ObservableCollection<ClassifierItemViewModel> ClassifierItems
        {
            get => _ClassifierItems;
            set => Set(ref _ClassifierItems, value);
        }
        #endregion

        #region CollectionViewSource для ClassifierItems
        // Делаем set приватным, чтобы иметь возможность переназначить View при обновлении коллекции
        public ICollectionView ClassifierItemsView { get; private set; }
        #endregion

        #endregion

        #region Methods

        #region Метод загрузки данных классификатора
        public async Task LoadClassifierDataAsync()
        {
            if (IsLoading) return; // Защита от двойного клика
            IsLoading = true;
            try
            {
                _logger.LogInformation("Загрузка данных классификатора...");
                IEnumerable<ComponentVersion> latestVersions;
                // 1. Асинхронный запрос к БД (не блокирует UI)
                using (var scope = _scopeFactory.CreateScope())
                {
                    var unitOfWork = scope.ServiceProvider.GetRequiredService<UnitOfWork>();
                   latestVersions = await unitOfWork.ComponentRepository.GetAllLatestComponentVersionsAsync();
                } // DataContext закрывается здесь, до тяжёлого маппинга картинок

                // 2. Тяжелая работа (маппинг и декодирование картинок) в фоновом потоке!
                // BitmapImage можно создавать в фоновом потоке, так как в LoadImageFromBytes вызывается image.Freeze()
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

                // 3. Возвращаемся в UI поток. 
                // Вместо медленного добавления по одному, создаем новую коллекцию и присваиваем её.
                ClassifierItems = new ObservableCollection<ClassifierItemViewModel>(items);

                // Пересоздаем View для новой коллекции и уведомляем UI
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
                IsLoading = false; // Скрываем индикатор загрузки в любом случае
            }
        }
        #endregion

        #region Применение фильтра
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
        #endregion

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
                image.Freeze(); // КРИТИЧЕСКИ ВАЖНО: позволяет использовать картинку из фонового потока
                return image;
            }
            catch
            {
                return null;
            }
        }
        #endregion

        #endregion
    }
}
