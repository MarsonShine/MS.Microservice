#nullable disable
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MS.Microservice.Core.Reflection.Internal
{
    internal readonly struct AwaitableInfo {
        public Type AwaiterType { get; }
        public PropertyInfo AwaiterIsCompletedProperty { get; }
        public MethodInfo AwaiterGetResultMethod { get; }
        public MethodInfo AwaiterOnCompletedMethod { get; }
        public MethodInfo AwaiterUnsafeOnCompletedMethod { get; }
        public Type ResultType { get; }
        public MethodInfo GetAwaiterMethod { get; }

        public AwaitableInfo(
            Type awaiterType,
            PropertyInfo awaiterIsCompletedProperty,
            MethodInfo awaiterGetResultMethod,
            MethodInfo awaiterOnCompletedMethod,
            MethodInfo awaiterUnsafeOnCompletedMethod,
            Type resultType,
            MethodInfo getAwaiterMethod) {
            AwaiterType = awaiterType;
            AwaiterIsCompletedProperty = awaiterIsCompletedProperty;
            AwaiterGetResultMethod = awaiterGetResultMethod;
            AwaiterOnCompletedMethod = awaiterOnCompletedMethod;
            AwaiterUnsafeOnCompletedMethod = awaiterUnsafeOnCompletedMethod;
            ResultType = resultType;
            GetAwaiterMethod = getAwaiterMethod;
        }

        public static bool IsTypeAwaitable(Type type, out AwaitableInfo awaitableInfo) {
            var getAwaiterMethod = type.GetRuntimeMethods().FirstOrDefault(m => m.Name.Equals("GetAwaiter", StringComparison.OrdinalIgnoreCase) &&
                m.GetParameters().Length == 0 &&
                m.ReturnType != null);
            if (getAwaiterMethod == null) {
                awaitableInfo = default(AwaitableInfo);
                return false;
            }

            var awaiterType = getAwaiterMethod.ReturnType;

            // Awaiter 必须能匹配属性 “bool IsCompleted { get; }”
            var isCompletedProperty = awaiterType.GetRuntimeProperties().FirstOrDefault(p => p.Name.Equals("IsCompleted", StringComparison.OrdinalIgnoreCase) &&
                p.PropertyType == typeof(bool) &&
                p.GetMethod != null);
            if (isCompletedProperty == null) {
                awaitableInfo = default(AwaitableInfo);
                return false;
            }

            // Awaiter 必须实现 INotifyCompletion
            var awaiterInterfaces = awaiterType.GetInterfaces();
            var implementsINotifyCompletion = awaiterInterfaces.Any(t => t == typeof(INotifyCompletion));
            if (!implementsINotifyCompletion) {
                awaitableInfo = default(AwaitableInfo);
                return false;
            }

            // INotifyCompletion 提供方法 “void OnCompleted(Action action)” 需要匹配
            var onCompletedMethod = typeof(INotifyCompletion).GetRuntimeMethods().Single(m => m.Name.Equals("OnCompleted", StringComparison.CurrentCultureIgnoreCase) &&
                m.ReturnType == typeof(void) &&
                m.GetParameters().Length == 1 &&
                m.GetParameters() [0].ParameterType == typeof(Action));

            // Awaiter 可选的实现 ICriticalNotifyCompletion
            var implementsICriticalNotifyCompletion = awaiterInterfaces.Any(t => t == typeof(ICriticalNotifyCompletion));
            MethodInfo unsafeOnCompletedMethod;
            if (implementsICriticalNotifyCompletion) {
                unsafeOnCompletedMethod = typeof(ICriticalNotifyCompletion).GetRuntimeMethods().Single(m => m.Name.Equals("UnsafeOnCompleted", StringComparison.OrdinalIgnoreCase) &&
                    m.ReturnType == typeof(void) &&
                    m.GetParameters().Length == 1 &&
                    m.GetParameters() [0].ParameterType == typeof(Action));
            } else {
                unsafeOnCompletedMethod = null;
            }

            // Awaiter 必须还要匹配 "void GetResult" 或者 "T GetResult()"
            var getResultMethod = awaiterType.GetRuntimeMethods().FirstOrDefault(m => m.Name.Equals("GetResult") &&
                m.GetParameters().Length == 0);
            if (getResultMethod == null) {
                awaitableInfo = default(AwaitableInfo);
                return false;
            }

            awaitableInfo = new AwaitableInfo(
                awaiterType,
                isCompletedProperty,
                getResultMethod,
                onCompletedMethod,
                unsafeOnCompletedMethod,
                getResultMethod.ReturnType,
                getAwaiterMethod
            );

            return true;
        }
    }
}