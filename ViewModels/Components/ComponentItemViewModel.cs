// File: ViewModels/ComponentItemViewModel.cs
using AGR_PropManager.ViewModels.Base;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using AGR_PropManager.Infrastructure.Commands;
using System.Collections.ObjectModel;
using AgroventInfrastructure.Enums;
using AGR_PropManager.ViewModels.TechProcess;
using AgroventInfrastructure.Interfaces.Properties;
using Agrovent.DAL;
using System.Collections.Specialized;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AgroventInfrastructure.Entities.Components;
using AgroventInfrastructure.Interfaces;

namespace AGR_PropManager.ViewModels.Components
{
    public class ComponentItemViewModel : BaseViewModel
    {
        private readonly IServiceScopeFactory? _scopeFactory;
        private readonly ComponentVersion? _componentVersion;

        #region CTOR
        public ComponentItemViewModel(IServiceScopeFactory scopeFactory, ComponentVersion version)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _componentVersion = version;
            ((INotifyCollectionChanged)_operations).CollectionChanged += OnOperationsCollectionChanged;
        }

        public ComponentItemViewModel(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            ((INotifyCollectionChanged)_operations).CollectionChanged += OnOperationsCollectionChanged;
        }

        public ComponentItemViewModel()
        {
            ((INotifyCollectionChanged)_operations).CollectionChanged += OnOperationsCollectionChanged;
        }
        #endregion

        #region Properties

        private bool _IsSelected;
        public bool IsSelected
        {
            get => ComponentType == AGR_ComponentType_e.Purchased ? false : _IsSelected;
            set => Set(ref _IsSelected, value);
        }

        private string _PartNumber = "";
        public string PartNumber
        {
            get => ComponentType != AGR_ComponentType_e.Purchased ? _PartNumber : "";
            set => Set(ref _PartNumber, value);
        }

        private string _Name = "";
        public string Name { get => _Name; set => Set(ref _Name, value); }

        private int _Quantity;
        public int Quantity { get => _Quantity; set => Set(ref _Quantity, value); }

        private string _Material = "";
        public string Material
        {
            get => ComponentType != AGR_ComponentType_e.Purchased ? _Material : "";
            set => Set(ref _Material, value);
        }

        private string _Paint = "";
        public string Paint { get => _Paint; set => Set(ref _Paint, value); }

        private int? _BendCount;
        public int? BendCount
        {
            get => ComponentType == AGR_ComponentType_e.SheetMetallPart ? _BendCount : null;
            set => Set(ref _BendCount, value);
        }

        private decimal? _ContourLength;
        public decimal? ContourLength
        {
            get => ComponentType == AGR_ComponentType_e.SheetMetallPart ? _ContourLength : null;
            set => Set(ref _ContourLength, value);
        }

        private AGR_ComponentType_e _ComponentType;
        public AGR_ComponentType_e ComponentType { get => _ComponentType; set => Set(ref _ComponentType, value); }

        private BitmapImage? _PreviewImage;
        public BitmapImage? PreviewImage { get => _PreviewImage; set => Set(ref _PreviewImage, value); }

        private int _Version;
        public int Version { get => _Version; set => Set(ref _Version, value); }

        private IAGR_AvaArticleModel? _AvaArticle;
        public IAGR_AvaArticleModel? AvaArticle { get => _AvaArticle; set => Set(ref _AvaArticle, value); }

        private string _Article = "";
        public string Article { get => _Article; set => Set(ref _Article, value); }

        public string PartnumberOrArticle => ComponentType == AGR_ComponentType_e.Purchased ? Article : PartNumber;

        private TechProcessViewModel? _TechnologicalProcessModel = new();
        public TechProcessViewModel? TechnologicalProcessModel
        {
            get => _TechnologicalProcessModel;
            set => Set(ref _TechnologicalProcessModel, value);
        }

        public ComponentVersion? ComponentVersionEntity => _componentVersion;

        private ObservableCollection<AGR_PropertyViewModel> _PropertiesCollection = new();
        public ObservableCollection<AGR_PropertyViewModel> PropertiesCollection
        {
            get => _PropertiesCollection;
            set => Set(ref _PropertiesCollection, value);
        }

        public bool? HasZeroTimeOperations
        {
            get
            {
                if (IsPurchased) return false;
                if (Operations?.Count == 0) return true;
                if (TechnologicalProcessModel?.Operations.Count == 0) return true;
                return false;
            }
        }

        public bool IsProduced => ComponentType == AGR_ComponentType_e.Assembly
                                || ComponentType == AGR_ComponentType_e.Part
                                || ComponentType == AGR_ComponentType_e.SheetMetallPart;
        public bool IsAssembly => ComponentType == AGR_ComponentType_e.Assembly;
        public bool IsPurchased => ComponentType == AGR_ComponentType_e.Purchased;
        public bool IsSheetMetallPart => ComponentType == AGR_ComponentType_e.SheetMetallPart;
        public bool IsPart => ComponentType == AGR_ComponentType_e.SheetMetallPart || ComponentType == AGR_ComponentType_e.Part;

        private ObservableCollection<TechOperationViewModel> _operations = new();
        public ObservableCollection<TechOperationViewModel> Operations
        {
            get => _operations;
            set
            {
                Set(ref _operations, value);
                foreach (var item in Operations)
                    item.PropertyChanged += Item_PropertyChanged;
                OnPropertyChanged(nameof(HasZeroTimeOperations));
            }
        }

        public void OnOperationsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
            => OnPropertyChanged(nameof(HasZeroTimeOperations));

        public void Item_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TechOperationViewModel.CostPerHour))
                OnPropertyChanged(nameof(HasZeroTimeOperations));
        }

        /// <summary>
        /// WRITE: новый scope -> загрузить сущность -> изменить -> SaveChanges -> dispose.
        /// Никакой EF-сущности, принадлежащей долгоживущему VM, здесь не используется.
        /// </summary>
        public async void OnOperationCostChanged(TechOperationViewModel operation)
        {
            OnPropertyChanged(nameof(HasZeroTimeOperations));
            if (_scopeFactory == null || operation.Operation == null) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dataContext = scope.ServiceProvider.GetRequiredService<DataContext>();

                var dbOperation = await dataContext.Operations.FindAsync(operation.Operation.Id);
                if (dbOperation == null) return;

                dbOperation.CostPerHour = operation.CostPerHour;
                await dataContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка сохранения стоимости операции: {ex}");
            }
        }
        #endregion

        #region DeleteOperationCommand
        private ICommand _DeleteOperationCommand;
        public ICommand DeleteOperationCommand => _DeleteOperationCommand
            ??= new RelayCommand(OnDeleteOperationCommandExecuted, CanDeleteOperationCommandExecute);

        private bool CanDeleteOperationCommandExecute(object p) => true;

        /// <summary>
        /// WRITE: новый scope -> найти операцию -> удалить -> SaveChanges -> dispose.
        /// </summary>
        private async void OnDeleteOperationCommandExecuted(object p)
        {
            var oper = p as TechOperationViewModel;
            if (oper is null) return;

            if (oper.TechProcess != null && _scopeFactory != null)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var dataContext = scope.ServiceProvider.GetRequiredService<DataContext>();

                    var entOp = await dataContext.Operations.FirstOrDefaultAsync(o =>
                        o.TechnologicalProcessId == oper.TechProcess.Id &&
                        o.SequenceNumber == oper.SequenceNumber);

                    if (entOp != null)
                    {
                        dataContext.Operations.Remove(entOp);
                        await dataContext.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Ошибка удаления операции: {ex}");
                    return;
                }
            }

            Operations.Remove(oper);
            OnPropertyChanged(nameof(HasZeroTimeOperations));
        }
        #endregion
    }
}
