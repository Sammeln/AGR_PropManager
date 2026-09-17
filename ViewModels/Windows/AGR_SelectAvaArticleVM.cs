// File: ViewModels/Windows/AGR_SelectAvaArticleVM.cs
using Microsoft.EntityFrameworkCore; // Для AsNoTracking
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data; // Для CollectionViewSource
using System.Windows.Input;
using Agrovent.DAL;
using System.Windows;
using AgroventInfrastructure.Interfaces;
using AgroventInfrastructure.Entities.Components;
using AgroventInfrastructure.Enums;
using AGR_PropManager.ViewModels.Base;
using AGR_PropManager.Infrastructure.Commands;

namespace AGR_PropManager.ViewModels.Windows
{
    public class AGR_SelectAvaArticleVM : BaseViewModel
    {
        private readonly DataContext _dataContext;
        private readonly ILogger<AGR_SelectAvaArticleVM> _logger;

        #region CTOR
        public AGR_SelectAvaArticleVM(DataContext dataContext)
        {
            _dataContext = dataContext ?? throw new ArgumentNullException(nameof(dataContext));
            LoadData();

            // Создаем CollectionViewSource и привязываем его к коллекции
            //Articles_CVS.Filter += (sender, e) => FilterAvaArticles(e.Item); // Подписываемся на событие фильтрации

            // Инициализация SearchText
            SearchText = string.Empty; // Убедимся, что изначально не фильтрует
        }

        public AGR_SelectAvaArticleVM()
        {

        }

        #endregion

        #region CollectionViewSource
        private CollectionViewSource Articles_CVS = new CollectionViewSource();
        public ICollectionView AvaArticlesView => Articles_CVS?.View;
        #endregion

        #region Filter

        #region ArticleFilter

        public void AddArticleFilter()
        {
            if (CanRemoveArticleFilter)
            {
                Articles_CVS.Filter -= new FilterEventHandler(FilterByArticle);
                Articles_CVS.Filter += new FilterEventHandler(FilterByArticle);
            }
            else
            {
                Articles_CVS.Filter += new FilterEventHandler(FilterByArticle);
                CanRemoveArticleFilter = true;
            }
        }
        public void RemoveArticleFilter()
        {
            Articles_CVS.Filter -= new FilterEventHandler(FilterByArticle);
            SearchText = null;
            CanRemoveArticleFilter = false;
        }
        private void FilterByArticle(object sender, FilterEventArgs e)
        {
            // see Notes on Filter Methods:
            var src = e.Item as AvaArticleModel;
            if (src == null)
            {
                e.Accepted = false;
                return;
            }
            if (string.IsNullOrEmpty(src.Name))
            {
                e.Accepted = false;
                return;
            }

            string[] splitSearch = SearchText.Split(' ').ToArray();

            if (!string.IsNullOrEmpty(SelectedAvaType))
            {
                if (SelectedAvaType == "Все типы") e.Accepted = true;
                else
                {
                    if (src.Type != SelectedAvaType) e.Accepted = false;
                }
            }
            //else if (string.Compare(AvaArtText, src.Article) != 0)
            //if (src.Name.Contains(SearchBar, StringComparison.OrdinalIgnoreCase)) return;
            if (src.Name is null) return;
            if (splitSearch.All(s => src.Name.Contains(s.ToString(), StringComparison.OrdinalIgnoreCase))) return;
            if (src.Article.ToString().Contains(SearchText)) return;
            if (src.PartNumber != null && src.PartNumber.Contains(SearchText)) return;

            e.Accepted = false;
        }

        private bool _canRemoveArticleFilter;
        public bool CanRemoveArticleFilter
        {
            get { return _canRemoveArticleFilter; }
            set
            {
                _canRemoveArticleFilter = value;
                OnPropertyChanged(nameof(CanRemoveArticleFilter));
            }
        }

        #endregion

        #endregion

        #region Commands
        // Команда для загрузки данных

        #region LoadDataCommand
        private ICommand _LoadDataCommand;
        public ICommand LoadDataCommand => _LoadDataCommand
            ??= new RelayCommand(OnLoadDataCommandExecuted, CanLoadDataCommandExecute);
        private bool CanLoadDataCommandExecute(object p) => true;
        private void OnLoadDataCommandExecuted(object p)
        {
            LoadData();
        }
        #endregion


        // Команда для подтверждения выбора (OK)
        #region AcceptSelectionCommand
        private ICommand _AcceptSelectionCommand;
        public ICommand AcceptSelectionCommand => _AcceptSelectionCommand
            ??= new RelayCommand(OnAcceptSelectionCommandExecuted, CanAcceptSelectionCommandExecute);
        private bool CanAcceptSelectionCommandExecute(object p)
        {
            return SelectedArticle != null;
        }
        private void OnAcceptSelectionCommandExecuted(object p)
        {
            // Проверяем, выбран ли элемент
            if (SelectedArticle != null)
            {
                IsDialogResultAccepted = true; // Устанавливаем флаг для закрытия
            }
            else
            {
                // Можно показать сообщение, что ничего не выбрано
                MessageBox.Show("Пожалуйста, выберите AvaArticle.", "Ничего не выбрано", MessageBoxButton.OK, MessageBoxImage.Warning);
                // Не устанавливаем IsDialogResultAccepted
            }
        }
        #endregion 

        // Команда для отмены (Cancel)
        #region CancelSelectionCommand
        private ICommand _CancelSelectionCommand;
        public ICommand CancelSelectionCommand => _CancelSelectionCommand
            ??= new RelayCommand(OnCancelSelectionCommandExecuted, CanCancelSelectionCommandExecute);
        private bool CanCancelSelectionCommandExecute(object p) => true;
        private void OnCancelSelectionCommandExecuted(object p)
        {
            SelectedArticle = null; // Сбрасываем выбор
            IsDialogResultAccepted = false; // Устанавливаем флаг для закрытия
        }
        #endregion

        // Команда для выбора элемента (двойной клик)
        #region SelectItemCommand
        private ICommand _SelectItemCommand;
        public ICommand SelectItemCommand => _SelectItemCommand
            ??= new RelayCommand<AvaArticleModel>(OnSelectItemCommandExecuted, CanSelectItemCommandExecute);
        private bool CanSelectItemCommandExecute(object p) => true;
        private void OnSelectItemCommandExecuted(AvaArticleModel? selectedArticle)
        {
            // Устанавливаем выбранный элемент
            SelectedArticle = selectedArticle;
            // Устанавливаем флаг для закрытия
            IsDialogResultAccepted = true;
        }
        #endregion  

        #endregion

        // Коллекция для хранения данных
        private ObservableCollection<IAGR_AvaArticleModel> _avaArticles;
        public ObservableCollection<IAGR_AvaArticleModel> AvaArticles
        {
            get => _avaArticles;
            set => Set(ref _avaArticles, value);
        }

        #region Свойство для текста поиска
        private string? _searchText;
        public string? SearchText
        {
            get => _searchText;
            set
            {
                if (Set(ref _searchText, value))
                {
                    // Обновляем фильтр при изменении текста
                    AddArticleFilter();
                }
            }
        }
        #endregion

        // Метод загрузки данных из БД
        private void LoadData()
        {
            try
            {
                //_logger.LogInformation("Загрузка данных AvaArticles для выбора...");

                // Загружаем все AvaArticles (возможно, стоит добавить сортировку)
                //var articles = await _dataContext.AvaArticles.AsNoTracking().ToListAsync();


                var articles = _dataContext.AvaArticles
                    .Where(x => !x.ArchiveType.Contains("50177"))
                    .OrderBy(x => x.Article)
                    .ToList();
                AvaArticles = new ObservableCollection<IAGR_AvaArticleModel>(articles);
                Articles_CVS.Source = AvaArticles;

                //_logger.LogInformation($"Загружено {AvaArticles.Count} записей AvaArticle.");

                // Обновляем фильтр, если был текст поиска
                AvaArticlesView.Refresh();

                // Обновляем состояние команды AcceptSelectionCommand
                //AcceptSelectionCommand.NotifyCanExecuteChanged();
            }
            catch (Exception ex)
            {
                //_logger.LogError(ex, "Ошибка при загрузке данных AvaArticles.");
                // Можно показать сообщение пользователю
            }
        }

        // Свойство для хранения выбранного элемента
        private AvaArticleModel? _selectedArticle;
        public AvaArticleModel? SelectedArticle
        {
            get => _selectedArticle;
            set => Set(ref _selectedArticle, value);
        }

        // Свойство для сигнализации результата (заменяет DialogResult)
        public bool IsDialogResultAccepted { get; private set; }

        // Метод, вызываемый командой AcceptSelectionCommand (OK)

        #region AvailableAvaTypes
        private ObservableCollection<string> _availableAvaTypes =
            new ObservableCollection<string>
            {
                AGR_AvaTypeNames.DontBuy,
                AGR_AvaTypeNames.VirtualComponent,
                AGR_AvaTypeNames.Production,
                AGR_AvaTypeNames.Component,
                AGR_AvaTypeNames.Purchased,
                AGR_AvaTypeNames.AllTypes
            };
        public ObservableCollection<string> AvailableAvaTypes
        {
            get => _availableAvaTypes;
        }
        #endregion

        #region SelectedAvaType
        private string? _selectedAvaType = AGR_AvaTypeNames.AllTypes;
        public string? SelectedAvaType
        {
            get => _selectedAvaType;
            set
            {
                if (Set(ref _selectedAvaType, value))
                {
                    AvaArticlesView.Refresh(); // Обновляем фильтр при изменении AvaType
                }
            }
        }
        #endregion
    }

}