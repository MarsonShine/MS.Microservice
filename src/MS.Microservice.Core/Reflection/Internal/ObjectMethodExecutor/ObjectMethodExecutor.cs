#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace MS.Microservice.Core.Reflection.Internal {
    internal class ObjectMethodExecutor {
        private readonly object?[] ? _parameterDefaultValue;
        private readonly MethodExecutorAsync? _executorAsync;
        private readonly MethodExecutor? _executor;

        private static readonly ConstructorInfo _objectMethodExecutorAwaitableConstructor = typeof(ObjectMethodExecutorAwaitable).GetConstructor(new [] {
            typeof(object), // customAwaitable
            typeof(Func<object, object>), // getAwaiterMethod
            typeof(Func<object, bool>), // isCompletedMethod
            typeof(Func<object, object>), // getResultMethod
            typeof(Action<object, Action>), // onCompletedMethod
            typeof(Action<object, Action>) // unsafeOnCompletedMethod
        }) !;

        private ObjectMethodExecutor(MethodInfo methodInfo, TypeInfo targetTypeInfo, object?[] ? parameterDefaultValue) {
            if (methodInfo == null) {
                throw new ArgumentNullException(nameof(methodInfo));
            }

            MethodInfo = methodInfo;
            MethodParameters = methodInfo.GetParameters();
            TargetTypeInfo = targetTypeInfo;
            MethodReturnType = methodInfo.ReturnType;

            var isAwaitable = CoercedAwaitableInfo.IsTypeAwaitable(MethodReturnType, out var coercedAwaitableInfo);

            IsMethodAsync = isAwaitable;
            AsyncResultType = isAwaitable ? coercedAwaitableInfo.AwaitableInfo.ResultType : null;

            // 上流代码可能会优先使用同步执行器（sync-executor），即使是对异步方法，因为如果它知道结果是一个特定的 Task<T>，其中T是已知的，那么它可以直接转换到该类型并等待它，而不需要在 _executorAsync 代码路径中涉及额外的堆分配。
            _executor = GetExecutor(methodInfo, targetTypeInfo);
        }

        #region private fields
        private delegate ObjectMethodExecutorAwaitable MethodExecutorAsync(object target, object[] parameters);
        private delegate object MethodExecutor(object target, object[] parameters);
        #endregion

        public MethodInfo MethodInfo { get; }
        public ParameterInfo[] MethodParameters { get; }
        public TypeInfo TargetTypeInfo { get; }
        public Type? AsyncResultType { get; }
        // 这个字段设置为 internal set 是因为在单元测试中有用到
        public Type MethodReturnType {get; internal set; }
        public bool IsMethodAsync {get;}

        private static MethodExecutor GetExecutor(MethodInfo methodInfo, TypeInfo targetTypeInfo)
        {
            
        }
    }
}