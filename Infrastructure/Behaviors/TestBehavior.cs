using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Xaml.Behaviors;

namespace AGR_PropManager.Infrastructure.Behaviors
{
    public class TestBehavior : Behavior<DataGrid>
    {
        private DataGrid _dataGrid;
        protected override void OnAttached()
        {
            base.OnAttached();
            _dataGrid = AssociatedObject;
            AssociatedObject.SizeChanged += AssociatedObject_SizeChanged;
            AssociatedObject.BeginningEdit += AssociatedObject_BeginningEdit;
            AssociatedObject.PreparingCellForEdit += AssociatedObject_PreparingCellForEdit;
        }

        private void AssociatedObject_PreparingCellForEdit(object? sender, DataGridPreparingCellForEditEventArgs e)
        {
            var contentPresenter = e.EditingElement as ContentPresenter;

            var editingControl = FindVisualChild<TextBox>(contentPresenter);
            if (editingControl != null)
            {
                editingControl.Focus();
                editingControl.SelectAll();
            }
        }
        private childItem FindVisualChild<childItem>(DependencyObject obj) where childItem : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(obj, i);
                if (child != null && child is childItem)
                {
                    return (childItem)child;
                }
                else
                {
                    childItem childOfChild = FindVisualChild<childItem>(child);
                    if (childOfChild != null)
                        return childOfChild;
                }
            }
            return null;
        }
        private void AssociatedObject_BeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
        {
            if (true)
            {

            }
        }

        private void AssociatedObject_SizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
        {
            //_dataGrid.Columns.First().Width = 25;
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();
            AssociatedObject.SizeChanged -= AssociatedObject_SizeChanged;
            AssociatedObject.BeginningEdit -= AssociatedObject_BeginningEdit;
        }
    }
}
