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
using Microsoft.Extensions.DependencyInjection;

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
        public string ChildUnit { get; set; }
        public string ChildType { get; set; }
        public string ChildURL { get; set; }
        public ComponentVersion Parent { get; set; }
        public ComponentVersion? Child { get; set; }
    }

    public class TreeImportReportViewModel : AGR_BaseReport
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ComponentItemViewModel _mainComponent;
        private readonly string _mainProductName;
        private string FilePath = string.Empty;

        private bool _IsExcelSaved;
        public bool IsExcelSaved
        {
            get => _IsExcelSaved;
            set => Set(ref _IsExcelSaved, value);
        }

        public TreeImportReportViewModel(ComponentItemViewModel mainComponent, IServiceScopeFactory scopeFactory)
        {
            _mainComponent = mainComponent ?? throw new ArgumentNullException(nameof(mainComponent));
            _mainProductName = _mainComponent.Name ?? "Неизвестное_изделие";
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            ReportData = new ObservableCollection<IAGR_ReportItem>();
            Initialize();
        }

        private async void Initialize()
        {
            if (_mainComponent.ComponentType == AGR_ComponentType_e.Assembly)
                await LoadReportDataAsync();
            else if (_mainComponent.IsPart)
                await LoadReportDataForPartAsync();

            Validate();
        }

        #region Commands
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

        #region METHODS

        /// <summary>
        /// READ: scope -> query -> materialize/process -> dispose.
        /// Scope не хранится в VM и не переживает операцию чтения.
        /// </summary>
        private async Task LoadReportDataAsync()
        {
            StatusMessage = "Загрузка данных отчета...";
            IsGenerating = true;
            try
            {
                List<AssemblyStructure> structureEntries;
                using (var scope = _scopeFactory.CreateScope())
                {
                    var unitOfWork = scope.ServiceProvider.GetRequiredService<UnitOfWork>();
                    structureEntries = (await unitOfWork.ComponentRepository
                        .GetAssemblyStructureRecursive(_mainComponent.PartNumber, _mainComponent.Version)).ToList();
                }

                ReportData.Clear();
                var uniqueParts = new List<ComponentVersion>();
                int rowNumber = 1;

                foreach (var entry in structureEntries)
                {
                    var parent = entry.ParentComponentVersion;
                    var child = entry.ChildComponentVersion;

                    var reportItem = new TreeImportReportItem
                    {
                        RowNumber = rowNumber++, Parent = parent, Child = child,
                        MainArtName = parent.Name, ChildName = child.Name,
                        MainPartNumber = parent.Component.PartNumber ?? "",
                        MainArticleAVA = parent.AvaArticleArticle.ToString() ?? "",
                        Quantity = entry.Quantity,
                        ChildPartNumber = child.ComponentType == AGR_ComponentType_e.Purchased ? "" : child.Component.PartNumber,
                        ChildArticleAVA = child.AvaArticle?.Article.ToString() ?? ""
                    };

                    if (child.ComponentType == AGR_ComponentType_e.Purchased)
                    {
                        reportItem.ChildUnit = child.AvaArticle?.MainUOM == "Штука" || child.AvaArticle?.SecondaryUOM == "Штука"
                            ? "Штука" : child.AvaArticle?.MainUOM ?? "";
                    }
                    else
                    {
                        reportItem.ChildUnit = "Штука";
                        reportItem.ChildType = "Комплектующие";
                        if (!string.IsNullOrEmpty(reportItem.ChildPartNumber))
                            reportItem.ChildURL = $@"{AGR_Options.ProductionRootFolderPath}\{child.Component.PartNumber}";
                    }

                    ReportData.Add(reportItem);
                }

                uniqueParts = structureEntries
                    .SelectMany(e => new[] { e.ParentComponentVersion, e.ChildComponentVersion })
                    .Where(cv => cv != null && (cv.ComponentType == AGR_ComponentType_e.Part
                        || cv.ComponentType == AGR_ComponentType_e.SheetMetallPart
                        || cv.ComponentType == AGR_ComponentType_e.Assembly))
                    .GroupBy(cv => cv.Id)
                    .Select(g => g.First())
                    .ToList();

                foreach (var part in uniqueParts)
                {
                    var material = part.Material;
                    if (material?.BaseMaterial != null)
                    {
                        string uom = material.MaterialAvaArticle?.MainUOM?.Trim().ToLower() ?? "";
                        double quantity = uom switch
                        {
                            "кв метр" or "м2" or "квадратный метр" => GetPropertyValue(part, AGR_PropertyNames.BlankArea),
                            "метр" or "м" or "пог. м" or "пог м" or "погонный метр" => Math.Round(GetPropertyValue(part, AGR_PropertyNames.BlankLen) / 1000, 3, MidpointRounding.ToPositiveInfinity),
                            "шт" or "штука" or "шт." or "штук" => 1,
                            _ => 0
                        };

                        ReportData.Add(new TreeImportReportItem
                        {
                            RowNumber = rowNumber++, Parent = part, Child = null,
                            MainArtName = part.Name, ChildName = material.BaseMaterial,
                            MainPartNumber = part.Component?.PartNumber ?? "",
                            MainArticleAVA = part.AvaArticle?.Article.ToString() ?? "",
                            Quantity = quantity, ChildPartNumber = "",
                            ChildArticleAVA = material.MaterialAvaArticle?.Article.ToString() ?? "",
                            ChildUnit = material.MaterialAvaArticle?.MainUOM ?? "",
                            ChildType = "", ChildURL = ""
                        });
                    }

                    bool hasPainting = HasPaintingOperation(part) || part.Material?.HasPaint == true;
                    if (hasPainting)
                    {
                        double blankArea = GetPropertyValue(part, AGR_PropertyNames.BlankArea);
                        ReportData.Add(new TreeImportReportItem
                        {
                            RowNumber = rowNumber++, Parent = part, Child = null,
                            MainArtName = part.Name, ChildName = material?.Paint?.ToString() ?? "",
                            MainPartNumber = part.Component?.PartNumber ?? "",
                            MainArticleAVA = part.AvaArticle?.Article.ToString() ?? "",
                            Quantity = Math.Round(blankArea * 0.22, 3, MidpointRounding.ToPositiveInfinity),
                            ChildPartNumber = "",
                            ChildArticleAVA = material?.PaintAvaArticleID?.ToString() ?? "",
                            ChildUnit = "Кг", ChildType = "", ChildURL = ""
                        });
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
                IsGenerating = false;
            }
        }

        private async Task LoadReportDataForPartAsync()
        {
            StatusMessage = "Загрузка данных отчета...";
            IsGenerating = true;
            int rowNumber = 1;
            try
            {
                ComponentVersion part;
                using (var scope = _scopeFactory.CreateScope())
                {
                    var unitOfWork = scope.ServiceProvider.GetRequiredService<UnitOfWork>();
                    part = await unitOfWork.ComponentRepository.GetLatestComponentVersion(_mainComponent.PartNumber);
                }

                if (part == null) return;
                ReportData.Clear();
                var material = part.Material;

                if (material?.BaseMaterial != null)
                {
                    string uom = material.MaterialAvaArticle?.MainUOM?.Trim().ToLower() ?? "";
                    double quantity = uom switch
                    {
                        "кв метр" or "м2" or "квадратный метр" => GetPropertyValue(part, AGR_PropertyNames.BlankArea),
                        "метр" or "м" or "пог. м" or "пог м" or "погонный метр" => Math.Round(GetPropertyValue(part, AGR_PropertyNames.BlankLen) / 1000, 3, MidpointRounding.ToPositiveInfinity),
                        "шт" or "штука" or "шт." or "штук" => 1,
                        _ => 0
                    };

                    ReportData.Add(new TreeImportReportItem
                    {
                        RowNumber = rowNumber++, Parent = part, Child = null,
                        MainArtName = part.Name, ChildName = material.BaseMaterial,
                        MainPartNumber = part.Component?.PartNumber ?? "",
                        MainArticleAVA = part.AvaArticle?.Article.ToString() ?? "",
                        Quantity = quantity, ChildPartNumber = "",
                        ChildArticleAVA = material.MaterialAvaArticle?.Article.ToString() ?? "",
                        ChildUnit = material.MaterialAvaArticle?.MainUOM ?? "",
                        ChildType = "", ChildURL = ""
                    });
                }

                bool hasPainting = HasPaintingOperation(part) || part.Material?.HasPaint == true;
                if (hasPainting)
                {
                    double blankArea = GetPropertyValue(part, AGR_PropertyNames.BlankArea);
                    ReportData.Add(new TreeImportReportItem
                    {
                        RowNumber = rowNumber++, Parent = part, Child = null,
                        MainArtName = part.Name, ChildName = material?.Paint?.ToString() ?? "",
                        MainPartNumber = part.Component?.PartNumber ?? "",
                        MainArticleAVA = part.AvaArticle?.Article.ToString() ?? "",
                        Quantity = Math.Round(blankArea * 0.22, 3, MidpointRounding.ToPositiveInfinity),
                        ChildPartNumber = "",
                        ChildArticleAVA = material?.PaintAvaArticleID?.ToString() ?? "",
                        ChildUnit = "Кг", ChildType = "", ChildURL = ""
                    });
                }

                StatusMessage = $"Загружено {ReportData.Count} строк.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка при загрузке данных: {ex.Message}";
            }
            finally
            {
                IsGenerating = false;
            }
        }

        private string DetermineUnit(AGR_ComponentType_e type) => type switch
        {
            AGR_ComponentType_e.Part or AGR_ComponentType_e.SheetMetallPart or AGR_ComponentType_e.Assembly => "шт",
            _ => "м2"
        };

        private string DetermineType(AGR_ComponentType_e type) => type switch
        {
            AGR_ComponentType_e.Part or AGR_ComponentType_e.SheetMetallPart or AGR_ComponentType_e.Assembly => "Комплектующие",
            _ => ""
        };

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
                    DefaultExt = ".xlsx", AddExtension = true,
                    OverwritePrompt = true, CheckPathExists = true
                };

                if (saveFileDialog.ShowDialog() != DialogResult.OK)
                {
                    StatusMessage = "Операция сохранения отменена.";
                    return;
                }

                StatusMessage = "Создание файла...";
                var filePath = saveFileDialog.FileName;
                FilePath = filePath;

                using (var workbook = new XSSFWorkbook())
                {
                    ISheet sheet = workbook.CreateSheet("Отчет импорта дерева");
                    IRow headerRow = sheet.CreateRow(0);
                    headerRow.CreateCell(0).SetCellValue("//Наименование главного артикула");
                    headerRow.CreateCell(1).SetCellValue("Part Number главного артикула");
                    headerRow.CreateCell(2).SetCellValue("Part Number child");
                    headerRow.CreateCell(3).SetCellValue("Наименование child");
                    headerRow.CreateCell(4).SetCellValue("Артикул AVA гл.артикула");
                    headerRow.CreateCell(5).SetCellValue("Артикул AVA child");
                    headerRow.CreateCell(6).SetCellValue("Кол-во");
                    for (int i = 7; i <= 10; i++) headerRow.CreateCell(i).SetCellValue("");
                    headerRow.CreateCell(11).SetCellValue("ЕИ child");
                    headerRow.CreateCell(12).SetCellValue("");
                    headerRow.CreateCell(13).SetCellValue("");
                    headerRow.CreateCell(14).SetCellValue("Тип child");
                    headerRow.CreateCell(25).SetCellValue("URL child");

                    ICellStyle partNumberStyle = workbook.CreateCellStyle();
                    partNumberStyle.DataFormat = HSSFDataFormat.GetBuiltinFormat("@");

                    int rowIndex = 1;
                    foreach (var rowitem in ReportData)
                    {
                        var item = rowitem as TreeImportReportItem;
                        IRow row = sheet.CreateRow(rowIndex++);
                        row.CreateCell(0).SetCellValue(item.MainArtName);
                        var mainPNCell = row.CreateCell(1); mainPNCell.SetCellValue(item.MainPartNumber); mainPNCell.CellStyle = partNumberStyle;
                        var childPNCell = row.CreateCell(2); childPNCell.SetCellValue(item.ChildPartNumber); childPNCell.CellStyle = partNumberStyle;
                        row.CreateCell(3).SetCellValue(item.ChildName);
                        row.CreateCell(4).SetCellValue(item.MainArticleAVA);
                        row.CreateCell(5).SetCellValue(item.ChildArticleAVA);
                        row.CreateCell(6).SetCellValue(item.Quantity);
                        for (int i = 7; i <= 10; i++) row.CreateCell(i).SetCellValue("");
                        row.CreateCell(11).SetCellValue(item.ChildUnit);
                        row.CreateCell(12).SetCellValue("");
                        row.CreateCell(13).SetCellValue("");
                        row.CreateCell(14).SetCellValue(item.ChildType);
                        for (int i = 15; i <= 24; i++) row.CreateCell(i).SetCellValue("");
                        row.CreateCell(25).SetCellValue(item.ChildURL);
                    }

                    for (int i = 0; i < 26; i++) sheet.AutoSizeColumn(i);
                    StatusMessage = "Сохранение файла...";
                    using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
                    workbook.Write(fileStream);
                    StatusMessage = $"Файл успешно сохранен: {filePath}";
                    Process.Start(new ProcessStartInfo { FileName = Path.GetDirectoryName(filePath), UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка при создании или сохранении Excel: {ex.Message}";
            }
            finally
            {
                IsGenerating = false;
            }
        }

        public void Validate()
        {
            Errors = null;
            HasErrors = false;
            var errorList = new List<string>();
            var warningList = new List<string>();

            foreach (var item in ReportData)
            {
                var row = item as TreeImportReportItem;
                if (row.RowNumber == 1 && string.IsNullOrEmpty(row.MainArticleAVA) && string.IsNullOrEmpty(row.MainPartNumber))
                    errorList.Add($"У основного изделия {row.MainArtName} пустые артикул и partnumber");

                if (row.Child?.ComponentType == AGR_ComponentType_e.Purchased)
                {
                    if (string.IsNullOrEmpty(row.ChildArticleAVA)) errorList.Add($"В строке {row.RowNumber} не указан артикул {row.ChildName}");
                    if (string.IsNullOrEmpty(row.ChildUnit)) errorList.Add($"В строке {row.RowNumber} не указан ЕИ {row.ChildName}");
                }

                if (row.ChildUnit == null && !string.IsNullOrEmpty(row.ChildUnit) && row.ChildUnit.Contains("шт", StringComparison.OrdinalIgnoreCase))
                    warningList.Add($"В строке {row.RowNumber} указан ЕИ ШТ, внимательно проверьте количество");

                if (row.Quantity == 0 || row.Quantity == double.NaN)
                    errorList.Add($"В строке {row.RowNumber} некорректное количество для {row.ChildName}");
            }

            if (errorList.Any())
            {
                Errors = string.Join("\n", errorList);
                HasErrors = true;
            }
            if (warningList.Any())
            {
                Warnings = string.Join("\n", warningList);
                HasWarnings = true;
            }
        }

        private double GetPropertyValue(ComponentVersion componentVersion, string propertyName)
        {
            if (componentVersion?.Properties == null || string.IsNullOrEmpty(propertyName)) return 0;
            var property = componentVersion.Properties.FirstOrDefault(p => string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase));
            if (property == null) return 0;
            return !string.IsNullOrEmpty(property.Value) && double.TryParse(property.Value, out double result) ? result : 0;
        }

        private bool HasPaintingOperation(ComponentVersion componentVersion)
        {
            if (componentVersion?.Component?.TechnologicalProcess?.Operations == null) return false;
            return componentVersion.Component.TechnologicalProcess.Operations.Any(op =>
                !string.IsNullOrEmpty(op.Name) && op.Name.IndexOf("покраска", StringComparison.OrdinalIgnoreCase) >= 0);
        }
        #endregion
    }
}
