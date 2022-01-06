#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;

namespace MS.Microservice.Core.Reflection.Internal
{
    public class ObjectMethodExecutor
    {
        private readonly object?[]? _parameterDefaultValues;
        private readonly MethodExecutorAsync? _executorAsync;
        private readonly MethodExecutor? _executor;

        private static readonly ConstructorInfo _objectMethodExecutorAwaitableConstructor = typeof(ObjectMethodExecutorAwaitable).GetConstructor(new[] {
            typeof(object), // customAwaitable
            typeof(Func<object, object>), // getAwaiterMethod
            typeof(Func<object, bool>), // isCompletedMethod
            typeof(Func<object, object>), // getResultMethod
            typeof(Action<object, Action>), // onCompletedMethod
            typeof(Action<object, Action>) // unsafeOnCompletedMethod
        })!;

        private ObjectMethodExecutor(MethodInfo methodInfo, TypeInfo targetTypeInfo, object?[]? parameterDefaultValues)
        {
            if (methodInfo == null)
            {
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

            if (IsMethodAsync)
            {
                _executorAsync = GetExecutorAsync(methodInfo, targetTypeInfo, coercedAwaitableInfo);
            }

            _parameterDefaultValues = parameterDefaultValues;
        }

        #region private fields
        private delegate ObjectMethodExecutorAwaitable MethodExecutorAsync(object target, object?[]? parameters);
        private delegate object? MethodExecutor(object target, object?[]? parameters);
        private delegate void VoidMethodExecutor(object target, object?[]? parameters);
        #endregion

        public MethodInfo MethodInfo { get; }
        public ParameterInfo[] MethodParameters { get; }
        public TypeInfo TargetTypeInfo { get; }
        public Type? AsyncResultType { get; }
        // 这个字段设置为 internal set 是因为在单元测试中有用到
        public Type MethodReturnType { get; internal set; }
        public bool IsMethodAsync { get; }

        public static ObjectMethodExecutor Create(MethodInfo methodInfo, TypeInfo targetTypeInfo)
        {
            return new ObjectMethodExecutor(methodInfo, targetTypeInfo, null);
        }

        public static ObjectMethodExecutor Create(MethodInfo methodInfo, TypeInfo targetTypeInfo, object?[] parameterDefaultValues)
        {
            if (parameterDefaultValues == null)
            {
                throw new ArgumentNullException(nameof(parameterDefaultValues));
            }

            return new ObjectMethodExecutor(methodInfo, targetTypeInfo, parameterDefaultValues);
        }
        /// <summary>
        /// 执行在 <paramref name="target"/>上配置的方法. 无论配置的方法是异步还是同步，都能使用它
        /// </summary>
        /// <remarks>
        /// 如果你在编译器就知道返回的具体的类型，即使target方法是异步的，也应该调用Execute，而不是ExecuteAsync。因为你可以通过类型转换直接 await 值，以及生成的代码可以参考awaitable的结果作为一个值类型的变量。如果使用的是ExecuteAsync，那么生成的代码就会将awaitable视为一个装箱对象，因为它在编译期类型是未知的。
        /// </remarks>
        /// <param name="target">要执行方法的对象</param>
        /// <param name="parameters">传递给方法的参数</param>
        /// <returns></returns>
        public object? Execute(object target, object?[]? parameters)
        {
            Debug.Assert(_executor != null, "Sync execution is not supported.");
            return _executor(target, parameters);
        }
        /// <summary>
        /// 执行<paramref name="target"/>上的方法，只能用于异步方法
        /// </summary>
        /// <remarks>
        /// 如果在编译器不知道方法返回的awaitable的类型，你就可以使用ExecuteAsync，它提供一个可等待的对象（awaitable-of-object）。这总是有效的，但与使用Execute然后在被转换为已知awaitable类型的结果值上使用“await”相比，可能会招致一些额外的堆分配。可能的额外堆分配是:
        /// 1. 自定义awaitable(尽管通常有一个堆分配，因为它通常是一个引用类型，你通常创建一个新的实例)
        /// 2. 自定义awaiter(不管它是不是一个值类型，因为如果它不是，您需要它的一个新实例，如果它是，它将必须被装箱，以便调用代码可以作为一个对象引用它)。
        /// 3. 这个异步的结果值，如果它是一个值类型(它必须被包装为一个对象，因为调用代码不知道它将是什么类型)。
        /// </remarks>
        /// <param name="target"></param>
        /// <param name="parameters"></param>
        /// <returns>一个对象，这个对象可以 await 调用方法获取返回的值</returns>
        public ObjectMethodExecutorAwaitable ExecuteAsync(object target, object?[]? parameters)
        {
            Debug.Assert(_executorAsync != null, "Async execution is not supported.");
            return _executorAsync(target, parameters);
        }

        public object? GetDefaultValueForParameter(int index)
        {
            if (_parameterDefaultValues == null)
            {
                throw new InvalidOperationException($"Cannot call {nameof(GetDefaultValueForParameter)}, because no parameter default values were supplied.");
            }

            if (index < 0 || index > MethodParameters.Length - 1)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _parameterDefaultValues[index];
        }

        private static MethodExecutor GetExecutor(MethodInfo methodInfo, TypeInfo targetTypeInfo)
        {
            // 参数构造
            var targetParameter = Expression.Parameter(typeof(object), "target");
            var parametersParameter = Expression.Parameter(typeof(object?[]), "parameters");

            // 构建方法形参列表
            var paramInfos = methodInfo.GetParameters();
            var parameters = new List<Expression>(paramInfos.Length);
            for (int i = 0; i < paramInfos.Length; i++)
            {
                var paramInfo = paramInfos[i];
                var valueObj = Expression.ArrayIndex(parametersParameter, Expression.Constant(i));
                var valueCast = Expression.Convert(valueObj, paramInfo.ParameterType);

                // valueCast 是 "(Ti) parameters[i]"
                parameters.Add(valueCast);
            }

            // 调用方法
            var instanceCast = Expression.Convert(targetParameter, targetTypeInfo.AsType());
            var methodCall = Expression.Call(instanceCast, methodInfo, parameters);

            // methodCall = "((Ttartget) target) method((T0) parameters[0], (T1) parameters[1], ...)"
            // 创建函数
            if (methodCall.Type == typeof(void))
            {
                var lambda = Expression.Lambda<VoidMethodExecutor>(methodCall, targetParameter, parametersParameter);
                var voidExecutor = lambda.Compile();
                return WrapVoidMethod(voidExecutor);
            }
            else
            {
                // 必须强制 methodCall 匹配 ActionExecutor 签名
                var castMethodCall = Expression.Convert(methodCall, typeof(object));
                var lambda = Expression.Lambda<MethodExecutor>(castMethodCall, targetParameter, parametersParameter);
                return lambda.Compile();
            }
        }

        private static MethodExecutor WrapVoidMethod(VoidMethodExecutor executor)
        {
            return delegate (object target, object?[]? parameters)
            {
                executor(target, parameters);
                return null;
            };
        }

        private static MethodExecutorAsync GetExecutorAsync(MethodInfo methodInfo, TypeInfo targetTypeInfo, CoercedAwaitableInfo coercedAwaitableInfo)
        {
            // 构造方法参数
            var targetParameter = Expression.Parameter(typeof(object), "target");
            var parameterParameter = Expression.Parameter(typeof(object[]), "parameters");

            // 构建形参数据列表
            var paramInfos = methodInfo.GetParameters();
            var parameters = new List<Expression>(paramInfos.Length);
            for (int i = 0; i < paramInfos.Length; i++)
            {
                var paramInfo = paramInfos[i];
                var valueObj = Expression.ArrayIndex(parameterParameter, Expression.Constant(i));
                var valueCast = Expression.Convert(valueObj, paramInfo.ParameterType);

                // valueCast = "(Ti) parameters[i]"
                parameters.Add(valueCast);
            }

            // 方法调用
            var instanceCast = Expression.Convert(targetParameter, targetTypeInfo.AsType());
            var methodCall = Expression.Call(instanceCast, methodInfo, parameters);

            // 使用方法返回值，基于我们之前构造的关于 awaitable 模式实现的信息，构造一个 ObjectMethodExecutorAwaitable。
            // 请注意，我们在这里构造的所有函数/操作都是预编译的，因此 ObjectMethodExecutor 的整个生命周期中，每个函数/操作都只保留一个实例。

            // var getAwaitableFunc = (object awaitable) => 
            //      (object) ((CustomAwaitableType)awaitable).GetAwaiter();
            var customAwaitableParam = Expression.Parameter(typeof(object), "awaitable");
            var awaitableInfo = coercedAwaitableInfo.AwaitableInfo;
            var postCoercionMethodReturnType = coercedAwaitableInfo.CoercerResultType ?? methodInfo.ReturnType;
            var getAwaiterFunc = Expression.Lambda<Func<object, object>>(
                Expression.Convert(
                    Expression.Call(
                        Expression.Convert(customAwaitableParam, postCoercionMethodReturnType),
                        awaitableInfo.GetAwaiterMethod),
                    typeof(object)),
                customAwaitableParam).Compile();

            // var isCompletedFunc = (object awaiter) =>
            //     ((CustomAwaiterType)awaiter).IsCompleted;
            var isCompletedParam = Expression.Parameter(typeof(object), "awaiter");
            var isCompletedFunc = Expression.Lambda<Func<object, bool>>(
                Expression.MakeMemberAccess(
                    Expression.Convert(isCompletedParam, awaitableInfo.AwaiterType),
                    awaitableInfo.AwaiterIsCompletedProperty),
                isCompletedParam).Compile();

            var getResultParam = Expression.Parameter(typeof(object), "awaiter");
            Func<object, object> getResultFunc;
            if (awaitableInfo.ResultType == typeof(void))
            {
                // var getResultFunc = (object awaiter) =>
                // {
                //     ((CustomAwaiterType)awaiter).GetResult(); // We need to invoke this to surface any exceptions
                //     return (object)null;
                // };
                getResultFunc = Expression.Lambda<Func<object, object>>(
                    Expression.Block(
                        Expression.Call(
                            Expression.Convert(getResultParam, awaitableInfo.AwaiterType),
                            awaitableInfo.AwaiterGetResultMethod),
                        Expression.Constant(null)
                        ), getResultParam).Compile();
            }
            else
            {
                // var getResultFunc = (object awaiter) =>
                //     (object)((CustomAwaiterType)awaiter).GetResult();
                getResultFunc = Expression.Lambda<Func<object, object>>(
                Expression.Convert(
                    Expression.Call(
                        Expression.Convert(getResultParam, awaitableInfo.AwaiterType),
                        awaitableInfo.AwaiterGetResultMethod),
                    typeof(object)),
                getResultParam).Compile();
            }

            // var onCompletedFunc = (object awaiter, Action continuation) => {
            //     ((CustomAwaiterType)awaiter).OnCompleted(continuation);
            // };
            var onCompletedParam1 = Expression.Parameter(typeof(object), "awaiter");
            var onCompletedParam2 = Expression.Parameter(typeof(Action), "continuation");
            var onCompletedFunc = Expression.Lambda<Action<object, Action>>(
                Expression.Call(
                    Expression.Convert(onCompletedParam1, awaitableInfo.AwaiterType),
                    awaitableInfo.AwaiterOnCompletedMethod,
                    onCompletedParam2),
                onCompletedParam1,
                onCompletedParam2).Compile();

            Action<object, Action>? unsafeOnCompletedFunc = null;
            if (awaitableInfo.AwaiterUnsafeOnCompletedMethod != null)
            {
                // var unsafeOnCompletedFunc = (object awaiter, Action continuation) => {
                //     ((CustomAwaiterType)awaiter).UnsafeOnCompleted(continuation);
                // };
                var unsafeOnCompletedParam1 = Expression.Parameter(typeof(object), "awaiter");
                var unsafeOnCompletedParam2 = Expression.Parameter(typeof(Action), "continuation");
                unsafeOnCompletedFunc = Expression.Lambda<Action<object, Action>>(
                    Expression.Call(
                        Expression.Convert(unsafeOnCompletedParam1, awaitableInfo.AwaiterType),
                        awaitableInfo.AwaiterUnsafeOnCompletedMethod,
                        unsafeOnCompletedParam2),
                    unsafeOnCompletedParam1,
                    unsafeOnCompletedParam2).Compile();
            }

            // 如果我们需要通过一个 coercer 函数传递方法调用的结果来获得一个 awaitable，那么就这样做。
            var coercerMethodCall = coercedAwaitableInfo.RequiresCoercion
                ? Expression.Invoke(coercedAwaitableInfo.CoercerExpression, methodCall)
                : (Expression)methodCall;

            // return new ObjectMethodExecutorAwaitable(
            //     (object)coercedMethodCall,
            //     getAwaiterFunc,
            //     isCompletedFunc,
            //     getResultFunc,
            //     onCompletedFunc,
            //     unsafeOnCompletedFunc);
            var returnValueExpression = Expression.New(
                _objectMethodExecutorAwaitableConstructor,
                Expression.Convert(coercerMethodCall, typeof(object)),
                Expression.Constant(getAwaiterFunc),
                Expression.Constant(isCompletedFunc),
                Expression.Constant(getResultFunc),
                Expression.Constant(onCompletedFunc),
                Expression.Constant(unsafeOnCompletedFunc)
                );

            var lambda = Expression.Lambda<MethodExecutorAsync>(returnValueExpression, targetParameter, parameterParameter);
            return lambda.Compile();
        }
    }
}