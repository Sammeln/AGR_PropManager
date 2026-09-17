// File: ViewModels/Reports/FullImportClassifierReportViewModel.cs
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using AGR_PropManager.Infrastructure.Commands; // RelayCommand
using AGR_PropManager.ViewModels.Base;
using AGR_PropManager.ViewModels.Components;
using AGR_PropManager.ViewModels.Reports.Interfaces;
using AgroventInfrastructure;
using AgroventInfrastructure.Enums; // AGR_ComponentType_e
using Microsoft.Win32; // SaveFileDialog
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace AGR_PropManager.ViewModels.Reports
{
    // Строка отчета полного импорта классификатора.
    // Формат столбцов подобран по эталонному файлу:
    // "..._Отчет_полного_импорта_классификатора.xlsx"
    public class FullImportClassifierReportItem : BaseViewModel, IAGR_ReportItem
    {
        public int RowCount { get; set; }
        public string Name { get; set; }
        public string PartNumber { get; set; }
        public string Type { get; set; }             // "Тип" — в эталоне почти всегда "5" (см. примечание в LoadReportData)
        public string Unit { get; set; }              // "ЕИ" — всегда "1"
        public string Article { get; set; }           // "Артикул" — пусто, если не задан
        public string ProductionSite { get; set; }    // "Вып. участок" — всегда "Очистить"
        public string DeliveryTerm { get; set; }      // "Срок поставки" — всегда "Очистить"
        public string DeliveryTermUnit { get; set; }  // "ЕИ срока поставки" — всегда "Очистить"
        public string DrawingUrl { get; set; }        // "Чертеж ссылка"
    }

    public class FullImportClassifierReportViewModel : AGR_BaseReport
    {
        #region Fields

        private readonly ObservableCollection<ComponentItemViewModel> _sourceComponents;
        private readonly string _mainProductName; // Имя для генерации имени файла
        private string FilePath = string.Empty;

        // Эталонный файл заполняет "Вып. участок", "Срок поставки" и "ЕИ срока поставки"
        // одним и тем же служебным значением "Очистить" для КАЖДОЙ строки — судя по всему,
        // это команда для импортера очистить эти поля у существующей позиции в справочнике.
        private const string ClearMarker = "Очистить";

        #endregion

        #region CTOR

        public FullImportClassifierReportViewModel()
        {
        }

        public FullImportClassifierReportViewModel(ObservableCollection<ComponentItemViewModel> sourceComponents)
        {
            _sourceComponents = sourceComponents ?? throw new ArgumentNullException(nameof(sourceComponents));
            _mainProductName = sourceComponents.First().Name ?? string.Empty;

            ReportData = new ObservableCollection<IAGR_ReportItem>();
            LoadReportData();
        }

        #endregion

        #region COMMANDS

        #region ExportToExcelCommand

        private ICommand _ExportToExcelCommand;
        public ICommand ExportToExcelCommand => _ExportToExcelCommand
            ??= new RelayCommand(OnExportToExcelCommandExecuted, CanExportToExcelCommandExecute);

        private bool CanExportToExcelCommandExecute(object p) => !IsGenerating;

        private void OnExportToExcelCommandExecuted(object p)
        {
            GenerateAndSaveExcel();
            CloseWindow();
            if (!string.IsNullOrEmpty(FilePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Path.GetDirectoryName(FilePath),
                    UseShellExecute = true
                });
            }
        }

        private void GenerateAndSaveExcel()
        {
            if (IsGenerating) return; // Защита от повторного одновременного запуска
            IsGenerating = true;
            StatusMessage = "Генерация Excel...";

            try
            {
                var saveFileDialog = new SaveFileDialog
                {
                    Filter = "Excel Files (*.xlsx)|*.xlsx|All Files (*.*)|*.*",
                    FileName = $"{_mainProductName}_Отчет_полного_импорта_классификатора.xlsx",
                    DefaultExt = ".xlsx",
                    AddExtension = true,
                    OverwritePrompt = true,
                    CheckPathExists = true
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    var filePath = saveFileDialog.FileName;
                    FilePath = filePath;

                    using (var workbook = new XSSFWorkbook())
                    {
                        ISheet sheet = workbook.CreateSheet("Классификатор");

                        // Стиль для Part Number — сохраняем ведущие нули как текст
                        ICellStyle partNumberStyle = workbook.CreateCellStyle();
                        partNumberStyle.DataFormat = HSSFDataFormat.GetBuiltinFormat("@");

                        // Заголовок. Индексы колонок соответствуют реальному эталонному
                        // файлу: A(0), C(2), D(3), I(8), J(9), U(20), AE(30), AF(31), AG(32), AW(48).
                        // Остальные колонки в эталоне не заполнены вовсе (ячейки отсутствуют),
                        // поэтому здесь они намеренно не создаются.
                        IRow headerRow = sheet.CreateRow(0);
                        headerRow.CreateCell(0).SetCellValue("//");
                        headerRow.CreateCell(2).SetCellValue("Part Number");
                        headerRow.CreateCell(3).SetCellValue("Наименование");
                        headerRow.CreateCell(8).SetCellValue("Тип");
                        headerRow.CreateCell(9).SetCellValue("ЕИ");
                        headerRow.CreateCell(20).SetCellValue("Артикул");
                        headerRow.CreateCell(30).SetCellValue("Вып. участок");
                        headerRow.CreateCell(31).SetCellValue("Срок поставки");
                        headerRow.CreateCell(32).SetCellValue("ЕИ срока поставки");
                        headerRow.CreateCell(48).SetCellValue("Чертеж ссылка");

                        int rowIndex = 1;
                        foreach (var reportItem in ReportData)
                        {
                            var item = reportItem as FullImportClassifierReportItem;
                            IRow row = sheet.CreateRow(rowIndex++);

                            var partNumberCell = row.CreateCell(2);
                            partNumberCell.SetCellValue(item.PartNumber);
                            partNumberCell.CellStyle = partNumberStyle;

                            row.CreateCell(3).SetCellValue(item.Name);
                            //row.CreateCell(8).SetCellValue(item.Type);
                            row.CreateCell(9).SetCellValue(item.Unit);
                            row.CreateCell(20).SetCellValue(item.Article);
                            row.CreateCell(30).SetCellValue(item.ProductionSite);
                            row.CreateCell(31).SetCellValue(item.DeliveryTerm);
                            row.CreateCell(32).SetCellValue(item.DeliveryTermUnit);
                            row.CreateCell(48).SetCellValue(item.DrawingUrl);
                        }

                        var usedColumns = new HashSet<int> { 0, 2, 3, 9, 20, 30, 31, 32, 48 };
                        int lastColumnIndex = usedColumns.Max();
                        for (int col = 0; col <= lastColumnIndex; col++)
                        {
                            if (usedColumns.Contains(col))
                            {
                                sheet.AutoSizeColumn(col);
                            }
                            else
                            {
                                sheet.SetColumnHidden(col, true);
                            }
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
                MessageBox.Show(StatusMessage, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsGenerating = false;
            }
        }

        #endregion

        #endregion

        #region Methods

        private void LoadReportData()
        {
            ReportData.Clear();

            var relevantComponents = _sourceComponents
                .Where(c => c.ComponentType == AGR_ComponentType_e.Assembly ||
                            c.ComponentType == AGR_ComponentType_e.Part ||
                            c.ComponentType == AGR_ComponentType_e.SheetMetallPart)
                .ToList();

            var i = 1;
            foreach (var component in relevantComponents)
            {
                var rowItem = new FullImportClassifierReportItem
                {
                    RowCount = i++,
                    Name = component.Name ?? "",
                    PartNumber = component.PartNumber ?? "",
                    //Type = "5",
                    Unit = "1",
                    Article = component.Article ?? "",
                    ProductionSite = ClearMarker,
                    DeliveryTerm = ClearMarker,
                    DeliveryTermUnit = ClearMarker,
                    DrawingUrl = $@"{AGR_Options.ProductionRootFolderPath}\{component.PartNumber}"
                };
                ReportData.Add(rowItem);
            }
        }

        #endregion
    }
}
