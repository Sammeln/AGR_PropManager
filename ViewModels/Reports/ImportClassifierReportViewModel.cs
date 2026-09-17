// File: ViewModels/Reports/ImportClassifierReportViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using AGR_PropManager.Infrastructure.Commands; // Assuming RelayCommand is here
using AGR_PropManager.ViewModels.Base;
using AGR_PropManager.ViewModels.Components;
using AgroventInfrastructure.Enums; // Assuming AGR_ComponentType_e is defined here
using Microsoft.Win32; // For SaveFileDialog
using NPOI.HSSF.UserModel; // For older .xls format if needed
using NPOI.SS.UserModel; // Core interfaces
using NPOI.XSSF.UserModel; // For .xlsx format
using System.IO;
using System.Diagnostics;
using AGR_PropManager.ViewModels.Reports.Interfaces;
using AgroventInfrastructure;

namespace AGR_PropManager.ViewModels.Reports
{
    // Вспомогательный класс для хранения строки данных отчета
    public class ReportRowItem : BaseViewModel, IAGR_ReportItem
    {
        public int RowCount { get; set; }
        public string Name { get; set; }
        public int Type { get; set; } // Всегда 5
        public string Partnumber { get; set; }
        public int MainUnit { get; set; } // Всегда 1
        public string URL { get; set; }
        // Пустые столбцы можно не моделировать как свойства
        public string Article { get; set; }
    }

    public class ImportClassifierReportViewModel : AGR_BaseReport
    {
        #region Fields

        private readonly ObservableCollection<ComponentItemViewModel> _sourceComponents; // Source data
        private readonly string _mainProductName; // Name for the filename
        private string _statusMessage;
        private bool _isGenerating;
        private string FilePath = string.Empty;
        #endregion

        #region CTOR

        public ImportClassifierReportViewModel()
        {

        }

        public ImportClassifierReportViewModel(ObservableCollection<ComponentItemViewModel> sourceComponents)
        {
            _sourceComponents = sourceComponents ?? throw new ArgumentNullException(nameof(sourceComponents));
            _mainProductName = sourceComponents.First().Name ?? string.Empty;

            // Подготовка данных для DataGrid
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
            if (IsGenerating) return; // Prevent multiple simultaneous executions
            IsGenerating = true;
            StatusMessage = "Генерация Excel...";

            try
            {
                var saveFileDialog = new SaveFileDialog
                {
                    Filter = "Excel Files (*.xlsx)|*.xlsx|All Files (*.*)|*.*",
                    FileName = $"{_mainProductName}_Отчет_импорта_классификатора.xlsx",
                    DefaultExt = ".xlsx",
                    AddExtension = true,
                    OverwritePrompt = true,
                    CheckPathExists = true
                };

                if (saveFileDialog.ShowDialog() == true)
                {

                    var filePath = saveFileDialog.FileName;
                    FilePath = filePath;

                    using (var workbook = new XSSFWorkbook()) // Create a new .xlsx workbook
                    {
                        ISheet sheet = workbook.CreateSheet("Отчет импорта");

                        // Create header row
                        IRow headerRow = sheet.CreateRow(0);
                        headerRow.CreateCell(0).SetCellValue("//Наименование");
                        headerRow.CreateCell(1).SetCellValue(""); // Пусто
                        headerRow.CreateCell(2).SetCellValue(""); // Пусто
                        headerRow.CreateCell(3).SetCellValue(""); // Пусто
                        headerRow.CreateCell(4).SetCellValue("ТИП"); // ТИП
                        headerRow.CreateCell(5).SetCellValue("Partnumber"); // Partnumber
                        headerRow.CreateCell(6).SetCellValue("Основная ЕИ"); // Основная ЕИ
                        headerRow.CreateCell(7).SetCellValue("URL"); // URL
                        headerRow.CreateCell(8).SetCellValue(""); // Пусто
                        headerRow.CreateCell(9).SetCellValue(""); // Пусто
                        headerRow.CreateCell(10).SetCellValue("Артикул"); // Артикул

                        // Style for PartNumber column to preserve leading zeros
                        ICellStyle partNumberStyle = workbook.CreateCellStyle();
                        partNumberStyle.DataFormat = HSSFDataFormat.GetBuiltinFormat("@"); // Format as text

                        int rowIndex = 1;
                        foreach (var repotItem in ReportData) // Iterate over prepared data
                        {
                            var item = repotItem as ReportRowItem;
                            if (!string.IsNullOrEmpty(item.Article)) continue;


                            IRow row = sheet.CreateRow(rowIndex++);
                            row.CreateCell(0).SetCellValue(item.Name);

                            // Cells 1, 2, 3 are empty as requested
                            row.CreateCell(1).SetCellValue("");
                            row.CreateCell(2).SetCellValue("");
                            row.CreateCell(3).SetCellValue("");

                            row.CreateCell(4).SetCellValue(item.Type); // ТИП

                            // Partnumber: Ensure it's treated as text to preserve leading zeros
                            var partNumberCell = row.CreateCell(5);
                            partNumberCell.SetCellValue(item.Partnumber);
                            partNumberCell.CellStyle = partNumberStyle; // Apply style

                            row.CreateCell(6).SetCellValue(item.MainUnit); // Основная ЕИ

                            row.CreateCell(7).SetCellValue(item.URL); // URL

                            // Cells 8, 9 are empty as requested
                            row.CreateCell(8).SetCellValue("");
                            row.CreateCell(9).SetCellValue("");

                            row.CreateCell(10).SetCellValue(item.Article); // Артикул
                        }

                        // Optional: Auto-size columns after populating data
                        for (int i = 0; i < 11; i++) // Adjust for 11 columns (0-10)
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
            // Очищаем старые данные, если таковые были
            ReportData.Clear();

            var relevantComponents = _sourceComponents
                .Where(c => c.ComponentType == AGR_ComponentType_e.Assembly ||
                            c.ComponentType == AGR_ComponentType_e.Part ||
                            c.ComponentType == AGR_ComponentType_e.SheetMetallPart)
                .ToList();

            var i = 1;
            foreach (var component in relevantComponents)
            {
                var rowItem = new ReportRowItem
                {
                    RowCount = i++,
                    Name = component.Name ?? "",
                    Type = 5, // Всегда 5
                    Partnumber = component.PartNumber ?? "", // Partnumber сохраняет ведущие нули как строка
                    MainUnit = 1, // Всегда 1
                    URL = $@"{AGR_Options.ProductionRootFolderPath}\{component.PartNumber}", // Конструируем URL
                    Article = component.Article ?? "" // Артикул, если не null
                };
                ReportData.Add(rowItem);
            }
        }

        #endregion
    }
}