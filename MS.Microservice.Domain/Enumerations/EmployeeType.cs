using System;
using System.Collections.Generic;
using System.Text;

namespace MS.Microservice.Domain.Enumerations
{
    public class EmployeeType : Enumeration
    {
        public static readonly EmployeeType Manager
        = new EmployeeType(0, "Manager");
        public static readonly EmployeeType Servant
            = new EmployeeType(1, "Servant");
        public static readonly EmployeeType AssistantToTheRegionalManager
            = new EmployeeType(2, "Assistant to the Regional Manager");
        private readonly int _value;
        private readonly string _name;

        private EmployeeType() { }
        private EmployeeType(int value, string name) : base(value, name) { }
    }


    public abstract class SuperEmployeeType : Enumeration
    {
        public static readonly SuperEmployeeType Manager
        = new ManagerType();

        protected SuperEmployeeType() { }
        protected SuperEmployeeType(int value, string name) : base(value, name) { }

        public abstract decimal BonusSize { get; }


        private class ManagerType : SuperEmployeeType
        {
            public ManagerType() : base(0, "Manager")
            {
            }

            public override decimal BonusSize => 1000m;
        }
    }
}
