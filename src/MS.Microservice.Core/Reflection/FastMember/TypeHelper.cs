using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MS.Microservice.Core.Reflection.FastMember
{
    internal static class TypeHelper
    {
        public static PropertyInfo[] GetTypeAndInterfaceProperties(this Type type,BindingFlags flags)
        {
            return !type.IsInterface ? 
                type.GetProperties(flags) : 
                (new[] { type }).Concat(type.GetInterfaces()).SelectMany(i => i.GetProperties(flags)).ToArray();
        }
    }
}
