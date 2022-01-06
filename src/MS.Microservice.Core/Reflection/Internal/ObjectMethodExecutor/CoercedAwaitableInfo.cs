using System;
using System.Linq.Expressions;

namespace MS.Microservice.Core.Reflection.Internal
{
    internal readonly struct CoercedAwaitableInfo
    {
        public AwaitableInfo AwaitableInfo { get;}
        public Expression CoercerExpression {get;}
        public Type CoercerResultType {get;}
        public bool RequiresCoercion => CoercerExpression != null;

        public CoercedAwaitableInfo(AwaitableInfo awaitableInfo) {
            AwaitableInfo = awaitableInfo;
            CoercerExpression = null;
            CoercerResultType = null;
        }

        public CoercedAwaitableInfo(Expression coercerExpression, Type coercerResultType, AwaitableInfo coercedAwaitableInfo)
        {
            CoercerExpression = coercerExpression;
            CoercerResultType = coercerResultType;
            AwaitableInfo = coercedAwaitableInfo;
        }

        public static bool IsTypeAwaitable(Type type,out CoercedAwaitableInfo info) {
            if(AwaitableInfo.IsTypeAwaitable(type, out var directlyAwaitableInfo)) {
                info = new CoercedAwaitableInfo(directlyAwaitableInfo);
                return true;
            }

            // 不是直接 awaitable，但是我们可能转换成它
            // 目前我们支持转换 FSharpAsync<T>,下面省略
            // ...
            info = default(CoercedAwaitableInfo);
            return false;
        }
    }
}