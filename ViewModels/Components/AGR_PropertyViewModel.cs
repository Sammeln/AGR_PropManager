using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AGR_PropManager.ViewModels.Base;
using AgroventInfrastructure.Entities.Components;
using AgroventInfrastructure.Interfaces.Entities.Components;

namespace AGR_PropManager.ViewModels.Components
{
    public class AGR_PropertyViewModel : BaseViewModel
    {
        #region CTOR
        public AGR_PropertyViewModel()
        {

        }

        public AGR_PropertyViewModel(ComponentProperty componentProperty)
        {
            Name = componentProperty.Name;
            Value = componentProperty.Value;
        }

        #endregion

        #region Props

        private string _Name;
        public string Name { get => _Name; set => _Name = value; }

        private string _Value;
        public string Value { get => _Value; set => _Value = value; } 
        #endregion
    }
}
