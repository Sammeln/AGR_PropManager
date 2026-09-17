// File: ViewModels/Reports/TechOpsImportReportViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using AGR_PropManager.Infrastructure.Commands;
using AGR_PropManager.ViewModels.Base;
using AGR_PropManager.ViewModels.Components;
using AGR_PropManager.ViewModels.Reports.Interfaces;
using AGR_PropManager.ViewModels.TechProcess; // For TechOperationViewModel
using AgroventInfrastructure.Enums; // Assuming AGR_ComponentType_e is here
using Microsoft.Win32; // For SaveFileDialog
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace AGR_PropManager.ViewModels.Reports
{
    public class TechOpsImportReportItem : INotifyPropertyChanged, IAGR_ReportItem
    {
        public int RowNumber { get; set; }
        // Свойства для отображения в DataGrid/Excel
        public string ComponentName { get; set; }
        public int? Article { get; set; } // Может быть null для не-главного изделия
        public string Partnumber { get; set; }
        public string OperationName { get; set; }
        public string WorkstationName { get; set; }
        public decimal LabourIntensity { get; set; } // Трудоемкость
        public string Availability { get; set; } // Всегда "1"
        public int Order { get; set; } // Порядок (SequenceNumber)
        public string Additional { get; set; } // Добавочная (всегда пусто)

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class TechOpsImportReportViewModel : AGR_BaseReport
    {
        #region Fields

        private readonly ObservableCollection<ComponentItemViewModel> _sourceComponents;
        private readonly ComponentItemViewModel _mainComponent; // Main product component
        private readonly string _mainProductName; // Name for the filename

        #endregion

        #region Constructor

        public TechOpsImportReportViewModel(ObservableCollection<ComponentItemViewModel> sourceComponents)
        {
            _sourceComponents = sourceComponents ?? throw new ArgumentNullException(nameof(sourceComponents));
            _mainProductName = _sourceComponents.First().Name;
            ReportData = new ObservableCollection<IAGR_ReportItem>();
            LoadReportData(); // Load synchronously for simplicity, though operations list could be large
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
            CloseWindow();
        }
        #endregion 


        #endregion

        #region METHODS

        private void LoadReportData()
        {
            StatusMessage = "Загрузка данных отчета...";
            IsGenerating = true; // Use IsGenerating as loading indicator too
            try
            {
                // Очищаем старые данные
                ReportData.Clear();
                int _rowNumber = 1;

                var relevantComponents = _sourceComponents
                    .Where(c => c.ComponentType == AGR_ComponentType_e.Assembly ||
                                c.ComponentType == AGR_ComponentType_e.Part ||
                                c.ComponentType == AGR_ComponentType_e.SheetMetallPart)
                    .ToList();

                foreach (var component in relevantComponents)
                {
                    var reportItem = new TechOpsImportReportItem();
                    reportItem.ComponentName = component.Name;
                    reportItem.Partnumber = component.PartNumber ?? "";

                    if (component.Operations.Count == 0)
                    {
                        ReportData.Add(reportItem);
                    }
                    else
                    {
                        foreach (var operation in component.Operations.OrderBy(o => o.SequenceNumber)) // Iterate through each operation of the component
                        {
                            reportItem = new TechOpsImportReportItem();
                            reportItem.RowNumber = _rowNumber++;
                            reportItem.ComponentName = component.Name;
                            reportItem.Partnumber = component.PartNumber ?? "";

                            if (_rowNumber == 0) reportItem.Article = int.TryParse(component.Article, out int articleVal) ? articleVal : null;
                            else reportItem.Article = null;

                            reportItem.Partnumber = component.PartNumber ?? "";
                            reportItem.LabourIntensity = operation.CostPerHour;
                            reportItem.OperationName = operation.Name;
                            reportItem.WorkstationName = operation.WorkstationName;
                            reportItem.Availability = "1";
                            reportItem.Order = operation.SequenceNumber;
                            reportItem.Additional = "";
                            ReportData.Add(reportItem);
                        }
                    }
                }

                StatusMessage = $"Загружено {ReportData.Count} строк.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка при загрузке данных: {ex.Message}";
                MessageBox.Show(StatusMessage, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsGenerating = false; // End loading indicator
            }
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
                    FileName = $"{_mainProductName}_Отчет_импорта_техопераций.xlsx",
                    DefaultExt = ".xlsx",
                    AddExtension = true,
                    OverwritePrompt = true,
                    CheckPathExists = true
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    StatusMessage = "Создание файла...";

                    var filePath = saveFileDialog.FileName;

                    using (var workbook = new XSSFWorkbook())
                    {
                        ISheet sheet = workbook.CreateSheet("Отчет импорта техопераций");

                        // Create header row
                        IRow headerRow = sheet.CreateRow(0);
                        headerRow.CreateCell(0).SetCellValue("//Артикул");
                        headerRow.CreateCell(1).SetCellValue("Partnumber");
                        headerRow.CreateCell(2).SetCellValue("Операция"); // Пусто
                        headerRow.CreateCell(3).SetCellValue("Участок"); // Пусто
                        headerRow.CreateCell(4).SetCellValue(""); // Пусто
                        headerRow.CreateCell(5).SetCellValue(""); // Пусто
                        headerRow.CreateCell(6).SetCellValue("Трудоемкость");
                        headerRow.CreateCell(7).SetCellValue(""); // Пусто
                        headerRow.CreateCell(8).SetCellValue(""); // Пусто
                        headerRow.CreateCell(9).SetCellValue("Доступность");
                        headerRow.CreateCell(10).SetCellValue("Порядок");
                        headerRow.CreateCell(11).SetCellValue("Добавочная");

                        // Style for PartNumber column to preserve leading zeros
                        ICellStyle partNumberStyle = workbook.CreateCellStyle();
                        partNumberStyle.DataFormat = HSSFDataFormat.GetBuiltinFormat("@"); // Format as text

                        int rowIndex = 1;
                        foreach (var reportItem in ReportData)
                        {
                            var item = reportItem as TechOpsImportReportItem;
                            IRow row = sheet.CreateRow(rowIndex++);

                            // Article: Write as number only if not null
                            var articleCell = row.CreateCell(0);
                            if (item.Article.HasValue)
                            {
                                articleCell.SetCellValue(item.Article.Value);
                            }
                            else
                            {
                                articleCell.SetCellValue(""); // Or leave blank if null
                            }

                            var partNumberCell = row.CreateCell(1);
                            partNumberCell.SetCellValue(item.Partnumber);
                            partNumberCell.CellStyle = partNumberStyle;

                            row.CreateCell(2).SetCellValue(item.OperationName);
                            row.CreateCell(3).SetCellValue(item.WorkstationName);
                            
                            // Columns 4-5: Empty
                            row.CreateCell(4).SetCellValue("");
                            row.CreateCell(5).SetCellValue("");

                            row.CreateCell(6).SetCellValue(item.LabourIntensity.ToString()); // Labour Intensity
                            // Columns 7-8: Empty
                            row.CreateCell(7).SetCellValue("");
                            row.CreateCell(8).SetCellValue("");

                            row.CreateCell(9).SetCellValue(item.Availability); // Availability ("1")

                            row.CreateCell(10).SetCellValue(item.Order); // Order (Sequence Number)

                            row.CreateCell(11).SetCellValue(item.Additional); // Additional (empty)
                        }

                        // Optional: Auto-size columns after populating data
                        for (int i = 0; i < 10; i++) // 10 columns (0-9)
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
    }
}