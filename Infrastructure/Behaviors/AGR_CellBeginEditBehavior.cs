using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using Microsoft.Xaml.Behaviors;

namespace AGR_PropManager.Infrastructure.Behaviors
{
    class AGR_CellBeginEditBehavior : Behavior<DataGrid>
    {
        protected override void OnAttached()
        {
            base.OnAttached();
            AssociatedObject.BeginningEdit += AssociatedObject_BeginningEdit;
        }

        private void AssociatedObject_BeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
        {
            if(e != null)
            {

            }
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();
        }
    }
}
