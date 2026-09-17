// File: ViewModels/Reports/TreeImportReportViewModel.cs
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using System.Windows.Input;
using AGR_PropManager.Infrastructure.Commands;
using AGR_PropManager.ViewModels.Base;
using AGR_PropManager.ViewModels.Components;
using AGR_PropManager.ViewModels.Reports.Interfaces;
using Agrovent.DAL;
using AgroventInfrastructure.Enums;
using AgroventInfrastructure.Entities.Components;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using AgroventInfrastructure;

namespace AGR_PropManager.ViewModels.Reports
{
    public class TreeImportReportItem : BaseViewModel, IAGR_ReportItem
    {
        public int RowNumber { get; set; }
        public string MainArtName { get; set; }
        public string MainPartNumber { get; set; }
        public string ChildPartNumber { get; set; }
        public string ChildName { get; set; }
        public string MainArticleAVA { get; set; }
        public string ChildArticleAVA { get; set; }
        public double Quantity { get; set; }
        // Пустые столбцы 7-10 (индексы 7-10)
        public string ChildUnit { get; set; }
        // Пустые столбцы 12-13 (индексы 12-13)
        public string ChildType { get; set; }
        // Пустые столбцы 15-24 (индексы 15-24)
        public string ChildURL { get; set; }

        public ComponentVersion Parent { get; set; }
        public ComponentVersion? Child { get; set; }
    }

    public class TreeImportReportViewModel : AGR_BaseReport
    {
        #region Fields
        private readonly UnitOfWork _unitOfWork;
        private readonly ComponentItemViewModel _mainComponent;
        private readonly string _mainProductName; // Name for the filename
        private string FilePath = string.Empty;

        #endregion

        #region IsExcelSaved

        /// <summary>
        /// Свойство для отображения сохранен ли отчет в эксель
        /// </summary>
        private bool _IsExcelSaved = false;
        public bool IsExcelSaved
        {
            get => _IsExcelSaved;
            set => Set(ref _IsExcelSaved, value);
        }
        #endregion

        #region Constructor

        public TreeImportReportViewModel(ComponentItemViewModel mainComponent, UnitOfWork unitOfWork)
        {
            _mainComponent = mainComponent ?? throw new ArgumentNullException(nameof(mainComponent));
            _mainProductName = _mainComponent.Name ?? "Неизвестное_изделие";
            _unitOfWork = unitOfWork;
            ReportData = new ObservableCollection<IAGR_ReportItem>();
            Initialize();
        }

        private async void Initialize()
        {

            if (_mainComponent.ComponentType == AGR_ComponentType_e.Assembly)
            {
                await LoadReportDataAsync();
            }
            if (_mainComponent.IsPart)
            {
                await LoadReportDataForPartAsync();
            }
            Validate();
        }

        #endregion

        #region Commands

        #region ExportToExcelCommand
        private ICommand _ExportToExcelCommand;
        public ICommand ExportToExcelCommand => _ExportToExcelCommand
            ??= new RelayCommand(OnExportToExcelCommandExecuted, CanExportToExcelCommandExecute);
        private bool CanExportToExcelCommandExecute(object p) => true;
        private void OnExportToExcelCommandExecuted(object p)
        {
            GenerateAndSaveExcel();
            IsExcelSaved = true;
            CloseWindow();
        }
        #endregion 

        #endregion

        #region METHODS

        private async Task LoadReportDataAsync()
        {
            StatusMessage = "Загрузка данных отчета...";
            IsGenerating = true; // Используем IsGenerating как индикатор загрузки тоже
            try
            {
                List<AssemblyStructure> structureEntries = new();
                List<ComponentVersion> uniqueParts = new();
                int rowNumber = 1; 

                 structureEntries = await _unitOfWork.ComponentRepository.GetAssemblyStructureRecursive(_mainComponent.PartNumber, _mainComponent.Version); // Используем DataService или UnitOfWork из _mainComponent

                // Очищаем старые данные
                ReportData.Clear();

                // Проходим по структуре и формируем строки отчета

                foreach (var entry in structureEntries)
                {
                    var parent = entry.ParentComponentVersion;
                    var child = entry.ChildComponentVersion;

                    var reportItem = new TreeImportReportItem
                    {
                        RowNumber = rowNumber++,
                        Parent = parent,
                        Child = child,
                        MainArtName = parent.Name,
                        ChildName = child.Name,
                        MainPartNumber = parent.Component.PartNumber ?? "",
                        MainArticleAVA = parent.AvaArticleArticle.ToString() ?? "",
                        Quantity = entry.Quantity,

                        ChildPartNumber = child.ComponentType == AGR_ComponentType_e.Purchased ? "" : child.Component.PartNumber,
                        ChildArticleAVA = child.AvaArticle?.Article.ToString() ?? "",
                    };
                    if (reportItem.Child.ComponentType == AGR_ComponentType_e.Purchased)
                    {
                        if (child.AvaArticle?.MainUOM == "Штука" || child.AvaArticle?.SecondaryUOM == "Штука")
                        {
                            reportItem.ChildUnit = "Штука";
                        }
                        else
                        {
                            reportItem.ChildUnit = child.AvaArticle?.MainUOM ?? "";
                        }

                        reportItem.ChildType = "";
                    }
                    else
                    {
                        reportItem.ChildUnit = "Штука";
                        reportItem.ChildType = "Комплектующие";
                        if (!string.IsNullOrEmpty(reportItem.ChildPartNumber))
                        {
                            reportItem.ChildURL = $@"{AGR_Options.ProductionRootFolderPath}\{child.Component.PartNumber}";
                        }
                    }
                    ReportData.Add(reportItem);
                    }


                // 2. Получаем уникальные компоненты с типом Part и SheetMetallPart
                 uniqueParts = structureEntries
                    .SelectMany(e => new[] { e.ParentComponentVersion, e.ChildComponentVersion })
                    .Where(cv => cv != null && (cv.ComponentType == AGR_ComponentType_e.Part
                                    || cv.ComponentType == AGR_ComponentType_e.SheetMetallPart
                                    || cv.ComponentType == AGR_ComponentType_e.Assembly))
                    .GroupBy(cv => cv.Id)
                    .Select(g => g.First())
                    .ToList();
                
                // 3. Обработка каждой уникальной детали (добавление строк материала и покраски)
                foreach (var part in uniqueParts)
                {
                    var material = part.Material;
                    if (material?.BaseMaterial != null)
                    {
                        string uom = material.MaterialAvaArticle?.MainUOM?.Trim().ToLower() ?? "";
                        double quantity = 0;

                        // Анализ единицы измерения и выбор свойства
                        if (uom == "кв метр" || uom == "м2" || uom == "квадратный метр")
                        {
                            quantity = GetPropertyValue(part, AGR_PropertyNames.BlankArea);
                        }
                        else if (uom == "метр" || uom == "м" || uom == "пог. м" || uom == "пог м" || uom == "погонный метр")
                        {
                            quantity = Math.Round(GetPropertyValue(part, AGR_PropertyNames.BlankLen) / 1000, 3, MidpointRounding.ToPositiveInfinity);
                        }
                        else if (uom == "шт" || uom == "штука" || uom == "шт." || uom == "штук")
                        {
                            quantity = 1;
                        }

                        // Добавляем строку Содержания материала
                        var materialRow = new TreeImportReportItem
                        {
                            RowNumber = rowNumber++,
                            Parent = part,
                            Child = null,
                            MainArtName = part.Name,
                            ChildName = material.BaseMaterial,
                            MainPartNumber = part.Component?.PartNumber ?? "",
                            MainArticleAVA = part.AvaArticle?.Article.ToString() ?? "",
                            Quantity = quantity,
                            ChildPartNumber = "",
                            ChildArticleAVA = material.MaterialAvaArticle?.Article.ToString() ?? "",
                            ChildUnit = material.MaterialAvaArticle?.MainUOM ?? "",
                            ChildType = "",
                            ChildURL = ""
                        };
                        ReportData.Add(materialRow);

                    }
                    // 4. Проверка наличия операции "покраска" в техпроцессе
                    bool hasPainting = HasPaintingOperation(part) || part.Material?.HasPaint == true;

                    // Если есть покраска, добавляем дополнительную строку
                    if (hasPainting)
                    {
                        double blankArea = GetPropertyValue(part, AGR_PropertyNames.BlankArea);
                        double paintQuantity = Math.Round(blankArea * 0.22, 3, MidpointRounding.ToPositiveInfinity);

                        var paintRow = new TreeImportReportItem
                        {
                            RowNumber = rowNumber++,
                            Parent = part,
                            Child = null,
                            MainArtName = part.Name,
                            ChildName = material.Paint?.ToString() ?? "",
                            MainPartNumber = part.Component?.PartNumber ?? "",
                            MainArticleAVA = part.AvaArticle?.Article.ToString() ?? "",
                            Quantity = paintQuantity,
                            ChildPartNumber = "",
                            ChildArticleAVA = material.PaintAvaArticleID?.ToString() ?? "",
                            ChildUnit = "Кг",
                            ChildType = "",
                            ChildURL = ""
                        };
                        ReportData.Add(paintRow);
                    }
                }


                StatusMessage = $"Загружено {ReportData.Count} строк.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка при загрузке данных: {ex.Message}";
            }
            finally
            {
                IsGenerating = false; // Завершаем индикатор загрузки
            }
        }
        private async Task LoadReportDataForPartAsync()
        {
            StatusMessage = "Загрузка данных отчета...";
            IsGenerating = true; // Используем IsGenerating как индикатор загрузки тоже
            int rowNumber = 1;
            try
            {
                // Предполагаем, что _mainComponent.PartNumber и Version установлены корректно
                var part = await _unitOfWork.ComponentRepository.GetLatestComponentVersion(_mainComponent.PartNumber);
                //var structureEntries = await _unitOfWork.ComponentRepository.GetAssemblyStructureRecursive(_mainComponent.PartNumber, _mainComponent.Version); // Используем DataService или UnitOfWork из _mainComponent
                //var material = parent.Material;
                // Очищаем старые данные
                ReportData.Clear();

                // Найдем версию сборки по PartNumber (берем последнюю по версии)
                //var assemblyVersion = await _unitOfWork.ComponentRepository.GetLatestComponentVersion(_mainComponent.PartNumber);
                //var reportItem = new TreeImportReportItem
                //{
                //    RowNumber = 1,
                //    Parent = parent,
                //    MainArtName = parent.Name,
                //    MainPartNumber = parent.Component.PartNumber ?? "",
                //    MainArticleAVA = parent.AvaArticle?.Article.ToString() ?? "",
                //    Child = null,
                //    ChildName = material.BaseMaterial,
                //    Quantity = 1 // Quantity из AssemblyStructure

                //    //ChildPartNumber = "",
                //    //ChildArticleAVA = child.BaseMaterial,
                //    //ChildUnit = "шт",//DetermineUnit(childComponentVM.ComponentType), // Определяем ЕИ
                //    //ChildType = "Комплектующие", //DetermineType(childComponentVM.ComponentType), // Определяем Тип
                //    //ChildURL = $@"\\192.168.10.1\kd\Listogib\TestRootFolder\{child.Component.PartNumber}" // Формируем URL
                //};

                //ReportData.Add(reportItem);
                var material = part.Material;
                if (material.BaseMaterial != null)
                {
                    string uom = material.MaterialAvaArticle?.MainUOM?.Trim().ToLower() ?? "";
                    double quantity = 0;

                    // Анализ единицы измерения и выбор свойства
                    if (uom == "кв метр" || uom == "м2" || uom == "квадратный метр")
                    {
                        quantity = GetPropertyValue(part, AGR_PropertyNames.BlankArea);
                    }
                    else if (uom == "метр" || uom == "м" || uom == "пог. м" || uom == "пог м" || uom == "погонный метр")
                    {
                        quantity = Math.Round(GetPropertyValue(part, AGR_PropertyNames.BlankLen) / 1000, 3, MidpointRounding.ToPositiveInfinity);
                    }
                    else if (uom == "шт" || uom == "штука" || uom == "шт." || uom == "штук")
                    {
                        quantity = 1;
                    }

                    // Добавляем строку Содержания материала
                    var materialRow = new TreeImportReportItem
                    {
                        RowNumber = rowNumber++,
                        Parent = part,
                        Child = null,
                        MainArtName = part.Name,
                        ChildName = material.BaseMaterial,
                        MainPartNumber = part.Component?.PartNumber ?? "",
                        MainArticleAVA = part.AvaArticle?.Article.ToString() ?? "",
                        Quantity = quantity,
                        ChildPartNumber = "",
                        ChildArticleAVA = material.MaterialAvaArticle?.Article.ToString() ?? "",
                        ChildUnit = material.MaterialAvaArticle?.MainUOM ?? "",
                        ChildType = "",
                        ChildURL = ""
                    };
                    ReportData.Add(materialRow);

                }
                // 4. Проверка наличия операции "покраска" в техпроцессе
                bool hasPainting = HasPaintingOperation(part) || part.Material?.HasPaint == true;

                // Если есть покраска, добавляем дополнительную строку
                if (hasPainting)
                {
                    double blankArea = GetPropertyValue(part, AGR_PropertyNames.BlankArea);
                    double paintQuantity = Math.Round(blankArea * 0.22, 3, MidpointRounding.ToPositiveInfinity);

                    var paintRow = new TreeImportReportItem
                    {
                        RowNumber = rowNumber++,
                        Parent = part,
                        Child = null,
                        MainArtName = part.Name,
                        ChildName = material.Paint?.ToString() ?? "",
                        MainPartNumber = part.Component?.PartNumber ?? "",
                        MainArticleAVA = part.AvaArticle?.Article.ToString() ?? "",
                        Quantity = paintQuantity,
                        ChildPartNumber = "",
                        ChildArticleAVA = material.PaintAvaArticleID?.ToString() ?? "",
                        ChildUnit = "Кг",
                        ChildType = "",
                        ChildURL = ""
                    };
                    ReportData.Add(paintRow);
                }

                StatusMessage = $"Загружено {ReportData.Count} строк.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка при загрузке данных: {ex.Message}";
                //MessageBox.Show(StatusMessage, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsGenerating = false; // Завершаем индикатор загрузки
            }
        }
        private string DetermineUnit(AGR_ComponentType_e type)
        {
            return type switch
            {
                AGR_ComponentType_e.Part or AGR_ComponentType_e.SheetMetallPart or AGR_ComponentType_e.Assembly => "шт",
                //AGR_ComponentType_e.Material => "м2",
                _ => "м2"
            };
        }
        private string DetermineType(AGR_ComponentType_e type)
        {
            return type switch
            {
                AGR_ComponentType_e.Part or AGR_ComponentType_e.SheetMetallPart or AGR_ComponentType_e.Assembly => "Комплектующие",
                _ => ""
            };
        }
        private void GenerateAndSaveExcel()
        {
            if (IsGenerating) return;
            IsGenerating = true;
            StatusMessage = "Генерация Excel...";

            try
            {
                var saveFileDialog = new SaveFileDialog
                {
                    Filter = "Excel Files (*.xlsx)|*.xlsx|All Files (*.*)|*.*",
                    FileName = $"{_mainProductName}_Отчет_импорта_дерева.xlsx",
                    DefaultExt = ".xlsx",
                    AddExtension = true,
                    OverwritePrompt = true,
                    CheckPathExists = true
                };

                if (saveFileDialog.ShowDialog() == DialogResult.OK)
                {
                    StatusMessage = "Создание файла...";

                    var filePath = saveFileDialog.FileName;
                    FilePath = filePath;

                    using (var workbook = new XSSFWorkbook())
                    {
                        ISheet sheet = workbook.CreateSheet("Отчет импорта дерева");

                        // Create header row
                        IRow headerRow = sheet.CreateRow(0);
                        headerRow.CreateCell(0).SetCellValue("//Наименование главного артикула");
                        headerRow.CreateCell(1).SetCellValue("Part Number главного артикула");
                        headerRow.CreateCell(2).SetCellValue("Part Number child");
                        headerRow.CreateCell(3).SetCellValue("Наименование child");
                        headerRow.CreateCell(4).SetCellValue("Артикул AVA гл.артикула");
                        headerRow.CreateCell(5).SetCellValue("Артикул AVA child");
                        headerRow.CreateCell(6).SetCellValue("Кол-во");
                        // Columns 7-10: Пусто
                        headerRow.CreateCell(7).SetCellValue("");
                        headerRow.CreateCell(8).SetCellValue("");
                        headerRow.CreateCell(9).SetCellValue("");
                        headerRow.CreateCell(10).SetCellValue("");
                        headerRow.CreateCell(11).SetCellValue("ЕИ child");
                        // Columns 12-13: Пусто
                        headerRow.CreateCell(12).SetCellValue("");
                        headerRow.CreateCell(13).SetCellValue("");
                        headerRow.CreateCell(14).SetCellValue("Тип child");

                        headerRow.CreateCell(25).SetCellValue("URL child");

                        // Style for PartNumber columns to preserve leading zeros
                        ICellStyle partNumberStyle = workbook.CreateCellStyle();
                        partNumberStyle.DataFormat = HSSFDataFormat.GetBuiltinFormat("@");

                        int rowIndex = 1;
                        foreach (var rowitem in ReportData)
                        {
                            var item = rowitem as TreeImportReportItem;
                            IRow row = sheet.CreateRow(rowIndex++);

                            row.CreateCell(0).SetCellValue(item.MainArtName);
                            var mainPNCell = row.CreateCell(1);
                            mainPNCell.SetCellValue(item.MainPartNumber);
                            mainPNCell.CellStyle = partNumberStyle;

                            var childPNCell = row.CreateCell(2);
                            childPNCell.SetCellValue(item.ChildPartNumber);
                            childPNCell.CellStyle = partNumberStyle;

                            row.CreateCell(3).SetCellValue(item.ChildName);
                            row.CreateCell(4).SetCellValue(item.MainArticleAVA);
                            row.CreateCell(5).SetCellValue(item.ChildArticleAVA);
                            row.CreateCell(6).SetCellValue(item.Quantity);

                            // Columns 7-10: Пусто
                            row.CreateCell(7).SetCellValue("");
                            row.CreateCell(8).SetCellValue("");
                            row.CreateCell(9).SetCellValue("");
                            row.CreateCell(10).SetCellValue("");

                            row.CreateCell(11).SetCellValue(item.ChildUnit);

                            // Columns 12-13: Пусто
                            row.CreateCell(12).SetCellValue("");
                            row.CreateCell(13).SetCellValue("");

                            row.CreateCell(14).SetCellValue(item.ChildType);

                            // Columns 15-24: Пусто
                            for (int i = 15; i <= 24; i++)
                            {
                                row.CreateCell(i).SetCellValue("");
                            }

                            row.CreateCell(25).SetCellValue(item.ChildURL);
                        }

                        // Optional: Auto-size columns after populating data
                        for (int i = 0; i < 26; i++) // 26 columns (0-25)
                        {
                            sheet.AutoSizeColumn(i);
                        }

                        StatusMessage = "Сохранение файла...";
                        using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                        {
                            workbook.Write(fileStream);
                        }

                        StatusMessage = $"Файл успешно сохранен: {filePath}";

                        Process.Start(new ProcessStartInfo
                        {
                            FileName = Path.GetDirectoryName(filePath),
                            UseShellExecute = true
                        });
                    }
                }
                else
                {
                    StatusMessage = "Операция сохранения отменена.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка при создании или сохранении Excel: {ex.Message}";
                //MessageBox.Show(StatusMessage, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsGenerating = false;
            }
        }

        // Метод для проверки ошибок на основе данных и типов
        public void Validate()
        {
            Errors = null;
            HasErrors = false;

            var errorList = new List<string>();
            var warningList = new List<string>();

            //errorList.Add($"У компонента {item.PartNumber}.{item.Name} не указано количество");

            foreach (var item in ReportData)
            {
                var row = item as TreeImportReportItem;
                if (row.RowNumber == 1)
                {
                    if (string.IsNullOrEmpty(row.MainArticleAVA)
                        && string.IsNullOrEmpty(row.MainPartNumber))
                    {
                        errorList.Add($"У основного изделия {row.MainArtName} пустые артикул и partnumber");
                    }
                }

                if (row.Child?.ComponentType == AGR_ComponentType_e.Purchased)
                {
                    if (string.IsNullOrEmpty(row.ChildArticleAVA))
                        errorList.Add($"В строке {row.RowNumber} не указан артикул {row.ChildName}");
                    if (string.IsNullOrEmpty(row.ChildUnit))
                        errorList.Add($"В строке {row.RowNumber} не указан ЕИ {row.ChildName}");
                }
                if (row.ChildUnit == null && !string.IsNullOrEmpty(row.ChildUnit) 
                            && row.ChildUnit.Contains("шт", StringComparison.OrdinalIgnoreCase))
                {
                    warningList.Add($"В строке {row.RowNumber} указан ЕИ ШТ, внимательно проверьте количество");
                }

                if (row.Quantity == 0 || row.Quantity == double.NaN)
                {
                    errorList.Add($"В строке {row.RowNumber} некорректное количество для {row.ChildName}");
                }
            }

            if (errorList.Any())
            {
                Errors = string.Join("\n", errorList);
                HasErrors = true;
            }
            if (warningList.Any())
            {
                Warnings = string.Join("\n", warningList);
                HasErrors = true;
                HasWarnings = true;
            }
        }
        private double GetPropertyValue(ComponentVersion componentVersion, string propertyName)
        {
            if (componentVersion?.Properties == null || string.IsNullOrEmpty(propertyName))
                return 0;

            // Ищем свойство по имени (без учета регистра)
            var property = componentVersion.Properties
                .FirstOrDefault(p => string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase));

            if (property == null)
                return 0;

            // Пытаемся получить значение из различных возможных полей
            // Замените на актуальные поля вашего класса ComponentProperty
            try
            {
                // Если значение хранится как строка
                if (!string.IsNullOrEmpty(property.Value) && double.TryParse(property.Value, out double result))
                    return result;
            }
            catch
            {
                // Игнорируем ошибки преобразования
            }

            return 0;
        }
        private bool HasPaintingOperation(ComponentVersion componentVersion)
        {
            if (componentVersion?.Component?.TechnologicalProcess?.Operations == null)
                return false;

            return componentVersion.Component.TechnologicalProcess.Operations
                .Any(op => !string.IsNullOrEmpty(op.Name) &&
                           op.Name.IndexOf("покраска", StringComparison.OrdinalIgnoreCase) >= 0);
        }
        #endregion

    }
}