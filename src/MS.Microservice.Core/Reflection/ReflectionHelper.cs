using System;
using System.Reflection;

namespace MS.Microservice.Core.Reflection {
    public class ReflectionHelper {
        public static bool IsAssignableToGenericType(Type givenType, Type genericType) {
            var givenTypeInfo = givenType.GetTypeInfo();

            if (givenTypeInfo.IsGenericType && givenType.GetGenericTypeDefinition() == genericType) {
                return true;
            }

            foreach (var interfaceType in givenTypeInfo.GetInterfaces()) {
                if (interfaceType.GetTypeInfo().IsGenericType && interfaceType.GetGenericTypeDefinition() == genericType) {
                    return true;
                }
            }

            if (givenTypeInfo.BaseType == null) {
                return false;
            }

            return IsAssignableToGenericType(givenTypeInfo.BaseType, genericType);
        }
    }
}