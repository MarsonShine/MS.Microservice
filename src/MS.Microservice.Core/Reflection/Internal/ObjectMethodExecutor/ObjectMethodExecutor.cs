#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MS.Microservice.Core.Reflection.Internal
{
    internal class ObjectMethodExecutor
    {
        private readonly object?[]? _parameterDefaultValue;
        private readonly MethodExecutorAsync? _executorAsync;
        private readonly MethodExecutor? _executor;


        #region private fields
        private delegate ObjectMethodExecutorAwaitable MethodExecutorAsync(object target, object[] parameters);
        private delegate object MethodExecutor(object target, object[] parameters);
        #endregion
    }
}
