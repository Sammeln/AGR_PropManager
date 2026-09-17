// File: ViewModels/ComponentItemViewModel.cs
using AGR_PropManager.ViewModels.Base;
using System.Windows.Media.Imaging;
using System.Windows.Input; // ICommand
using AGR_PropManager.Infrastructure.Commands;
using System.Collections.ObjectModel;
using AgroventInfrastructure.Enums;
using AGR_PropManager.ViewModels.TechProcess;
using AgroventInfrastructure.Interfaces.Properties;
using Agrovent.DAL;
using System.Collections.Specialized;
using Agrovent.DAL.Services.Repositories;
using Microsoft.Extensions.Logging;
using Agrovent.Infrastructure.Interfaces;
using AgroventInfrastructure.Entities.Components;
using System.Runtime.CompilerServices;
using AgroventInfrastructure.Interfaces;

namespace AGR_PropManager.ViewModels.Components
{
    public class ComponentItemViewModel : BaseViewModel
    {
        private readonly DataContext _dataContext;
        private readonly UnitOfWork _unitOfWork;
        private readonly ComponentVersion _componentVersion;

        #region CTOR
        public ComponentItemViewModel(DataContext dataContext
            , UnitOfWork unitOfWork
            , ComponentVersion version)
        {
            _dataContext = dataContext;
            _unitOfWork = unitOfWork;
            _componentVersion = version;
            ((INotifyCollectionChanged)_operations).CollectionChanged += OnOperationsCollectionChanged;
        }
        public ComponentItemViewModel(DataContext dataContext
                ,UnitOfWork unitOfWork)
        {
            _dataContext = dataContext;
            _unitOfWork = unitOfWork;
            ((INotifyCollectionChanged)_operations).CollectionChanged += OnOperationsCollectionChanged;
        }
        public ComponentItemViewModel()
        {
            // Конструктор для Design-time

        }
        #endregion

        #region Properties

        #region IsSelected
        private bool _IsSelected = false;
        public bool IsSelected
        {
            get
            {
                if (ComponentType == AGR_ComponentType_e.Purchased) return false;
                return _IsSelected;
            }

            set => Set(ref _IsSelected, value);

        }
        #endregion

        #region PartNumber
        private string _PartNumber = "";
        public string PartNumber
        {
            get
            {
                if (ComponentType != AGR_ComponentType_e.Purchased) return _PartNumber;
                return "";
            }

            set => Set(ref _PartNumber, value);
        }
        #endregion

        #region Name
        private string _Name = "";
        public string Name
        {
            get => _Name;
            set => Set(ref _Name, value);
        }
        #endregion

        #region Quantity
        private int _Quantity;
        public int Quantity
        {
            get => _Quantity;
            set => Set(ref _Quantity, value);
        }
        #endregion

        #region Material
        private string _Material = "";
        public string Material
        {
            get
            {
                if (ComponentType != AGR_ComponentType_e.Purchased) return _Material;
                return "";
            }

            set => Set(ref _Material, value);
        }
        #endregion

        #region Paint
        private string _Paint = "";
        public string Paint
        {
            get => _Paint;
            set => Set(ref _Paint, value);
        }
        #endregion

        #region BendCount
        private int? _BendCount;
        public int? BendCount
        {
            get
            {
                if (ComponentType == AGR_ComponentType_e.SheetMetallPart) return _BendCount;
                return null;
            }

            set => Set(ref _BendCount, value);
        }
        #endregion

        #region ContourLength
        private decimal? _ContourLength;
        public decimal? ContourLength
        {
            get
            {
                if (ComponentType == AGR_ComponentType_e.SheetMetallPart) return _ContourLength;
                return null;   
            }

            set => Set(ref _ContourLength, value);
        }
        #endregion

        #region ComponentType
        private AGR_ComponentType_e _ComponentType;
        public AGR_ComponentType_e ComponentType
        {
            get => _ComponentType;
            set => Set(ref _ComponentType, value);
        }
        #endregion

        #region PreviewImage
        private BitmapImage? _PreviewImage;
        public BitmapImage? PreviewImage
        {
            get => _PreviewImage;
            set => Set(ref _PreviewImage, value);
        }
        #endregion

        #region Property - 
        private int _Version;
        public int Version
        {
            get => _Version;
            set => Set(ref _Version, value);
        }
        #endregion 

        #region Property - AvaArticle
        private IAGR_AvaArticleModel? _AvaArticle;
        public IAGR_AvaArticleModel? AvaArticle
        {
            get => _AvaArticle;
            set => Set(ref _AvaArticle, value);
        }
        #endregion

        #region Property - Article
        private string _Article;
        public string Article
        {
            get => _Article;
            set => Set(ref _Article, value);
        }
        #endregion

        #region Property - PartnumberOrArticle
        public string PartnumberOrArticle
        {
            get
            {
                if (ComponentType == AGR_ComponentType_e.Purchased)
                {
                    return Article;
                }
                return PartNumber;
            }
        }
        #endregion 

        private TechProcessViewModel? _TechnologicalProcessModel = new();
        public TechProcessViewModel? TechnologicalProcessModel
        {
            get => _TechnologicalProcessModel;
            set => Set(ref _TechnologicalProcessModel, value);
        }
        public ComponentVersion ComponentVersionEntity => _componentVersion;

        #region Property - PropertiesCollection
        private ObservableCollection<AGR_PropertyViewModel> _PropertiesCollection = new();
        public ObservableCollection<AGR_PropertyViewModel> PropertiesCollection
        {
            get => _PropertiesCollection;
            set => Set(ref _PropertiesCollection, value);
        }
        #endregion 

        // Свойство для подсветки строк с нулевым временем
        public bool? HasZeroTimeOperations
        {
            get
            {
                if (IsPurchased) return false;
                if (Operations?.Count == 0) return true;
                if (TechnologicalProcessModel.Operations.Count == 0) return true;
                return false;
            }
        }
        public bool IsProduced => ComponentType == AGR_ComponentType_e.Assembly
                                || ComponentType == AGR_ComponentType_e.Part
                                || ComponentType == AGR_ComponentType_e.SheetMetallPart;
        public bool IsAssembly => ComponentType == AGR_ComponentType_e.Assembly;
        public bool IsPurchased => ComponentType == AGR_ComponentType_e.Purchased;

        public bool IsSheetMetallPart => ComponentType == AGR_ComponentType_e.SheetMetallPart;
        public bool IsPart => ComponentType == AGR_ComponentType_e.SheetMetallPart
                               || ComponentType == AGR_ComponentType_e.Part;

        #region Техоперации

        private ObservableCollection<TechOperationViewModel> _operations = new();
        public ObservableCollection<TechOperationViewModel> Operations
        {
            get => _operations;
            set
            {

                Set(ref _operations, value);
                foreach (var item in Operations)
                {
                    item.PropertyChanged += Item_PropertyChanged;
                }

                OnPropertyChanged(nameof(HasZeroTimeOperations));
            }
        }

        public void OnOperationsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(HasZeroTimeOperations));
        }

        public void Item_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TechOperationViewModel.CostPerHour))
            {
                OnPropertyChanged(nameof(HasZeroTimeOperations));
            }
        }
        public async void OnOperationCostChanged(TechOperationViewModel operation)
        {
            OnPropertyChanged(nameof(HasZeroTimeOperations));
            await _unitOfWork.TechProcessRepository.UpdateOperationAsync(operation.Operation);
        }
        #endregion

        #region Property - SelectedOperation
        private TechOperationViewModel _SelectedOperation;
        public TechOperationViewModel SelectedOperation
        {
            get => _SelectedOperation;
            set => Set(ref _SelectedOperation, value);
        }
        #endregion

        #endregion

        #region DeleteOperationCommand
        private ICommand _DeleteOperationCommand;

        public ICommand DeleteOperationCommand => _DeleteOperationCommand
            ??= new RelayCommand(OnDeleteOperationCommandExecuted, CanDeleteOperationCommandExecute);
        private bool CanDeleteOperationCommandExecute(object p) => true;
        private async void OnDeleteOperationCommandExecuted(object p)
        {
            var oper = p as TechOperationViewModel;
            if (oper is null) return;
            if (oper.TechProcess != null)
            {
                var entOp = _dataContext.Operations.FirstOrDefault(o => o.TechnologicalProcessId == oper.TechProcess.Id
                    && o.SequenceNumber == oper.SequenceNumber);
                if (entOp != null)
                {
                    _dataContext.Operations.Remove(entOp);
                    await _dataContext.SaveChangesAsync();
                    Operations.Remove(oper);
                }
            }
            else
            {
                Operations.Remove(oper);
            }
            OnPropertyChanged(nameof(HasZeroTimeOperations));
            
        }
        #endregion 





    }

}