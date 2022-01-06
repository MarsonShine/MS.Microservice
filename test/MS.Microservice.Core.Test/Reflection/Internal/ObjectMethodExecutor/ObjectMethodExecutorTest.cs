using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using MS.Microservice.Core.Reflection.Internal;
using Microsoft.AspNetCore.Mvc.Internal;
using System.Runtime.CompilerServices;
using System.Threading;

namespace MS.Microservice.Core.Test
{
    public class ObjectMethodExecutorTest
    {
        private TestObject _targetObject = new TestObject();
        private TypeInfo targetTypeInfo = typeof(TestObject).GetTypeInfo();
        [Fact]
        public void ObjectMethodExecutor_ExecutesVoidActions()
        {
            // Arrange
            //var mapper = new ActionResultTypeMapper();
            var controller = new TestController();
            var objectMethodExecutor = GetExecutor(nameof(TestController.VoidAction));
            var returnValue = objectMethodExecutor.Execute(controller, null);
            Assert.Null(returnValue);
        }
        [Fact]
        public async void ObjectMethodExecutor_ExecutesAsyncMethods()
        {
            var controller = new TestController();
            var objectMethodExecutor = GetExecutor(nameof(TestController.ReturnActionResultOFTAsync));
            var returnValue = objectMethodExecutor.Execute(controller, null);
            Assert.NotNull(returnValue);
            var returnValueAsync = Assert.IsType<Task<ActionResult<TestModel>>>(returnValue);
            Assert.NotNull(returnValueAsync);
            var result = await returnValueAsync;
            Assert.NotNull(result);
            Assert.IsType<ActionResult<TestModel>>(result);
        }

        [Fact]
        public async Task ExecuteValueMethodAsync()
        {
            var executor = GetExecutorForMethod("ValueMethodAsync");
            var result = await executor.ExecuteAsync(
                _targetObject,
                new object[] { 10, 20 });
            Assert.True(executor.IsMethodAsync);
            Assert.Equal(30, (int)result);
        }
        private static ObjectMethodExecutor GetExecutor(string methodName)
        {
            var type = typeof(TestController);
            var methodInfo = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(methodInfo);
            return ObjectMethodExecutor.Create(methodInfo, type.GetTypeInfo());
        }

        private ObjectMethodExecutor GetExecutorForMethod(string methodName)
        {
            var method = typeof(TestObject).GetMethod(methodName);
            return ObjectMethodExecutor.Create(method, targetTypeInfo);
        }

        private ObjectMethodExecutor GetExecutorForMethod(string methodName, object[] parameterDefaultValues)
        {
            var method = typeof(TestObject).GetMethod(methodName);
            return ObjectMethodExecutor.Create(method, targetTypeInfo, parameterDefaultValues);
        }


        private class TestController
        {
            public bool Executed { get; set; }

            public void VoidAction() => Executed = true;

            public IActionResult ReturnIActionResult() => new ContentResult();

            public ContentResult ReturnsIActionResultSubType() => new ContentResult();

            public ActionResult<TestModel> ReturnsActionResultOfT() => new ActionResult<TestModel>(new TestModel());

            public CustomConvertibleFromAction ReturnsCustomConvertibleFromIActionResult() => new CustomConvertibleFromAction();

            public TestModel ReturnsModelAsModel() => new TestModel();

            public object ReturnModelAsObject() => new TestModel();

            public object ReturnIActionResultAsObject() => new RedirectResult("/foo");

            public Task ReturnsTask()
            {
                Executed = true;
                return Task.CompletedTask;
            }

            public Task<IActionResult> ReturnIActionResultAsync() => Task.FromResult((IActionResult)new StatusCodeResult(201));

            public Task<StatusCodeResult> ReturnsIActionResultSubTypeAsync() => Task.FromResult(new StatusCodeResult(200));

            public Task<TestModel> ReturnsModelAsModelAsync() => Task.FromResult(new TestModel());

            public Task<object> ReturnsModelAsObjectAsync() => Task.FromResult((object)new TestModel());

            public Task<object> ReturnIActionResultAsObjectAsync() => Task.FromResult((object)new OkResult());

            public Task<ActionResult<TestModel>> ReturnActionResultOFTAsync() => Task.FromResult(new ActionResult<TestModel>(new TestModel()));
        }

        private class TestModel
        {
        }

        private class CustomConvertibleFromAction : IConvertToActionResult
        {
            public IActionResult Convert() => null;
        }

        public class TestObject
        {
            public string value;
            public int ValueMethod(int i, int j)
            {
                return i + j;
            }

            public void VoidValueMethod(int i)
            {

            }

            public TestObject ValueMethodWithReturnType(int i)
            {
                return new TestObject() { value = "Hello" }; ;
            }

            public TestObject ValueMethodWithReturnTypeThrowsException(TestObject i)
            {
                throw new NotImplementedException("Not Implemented Exception");
            }

            public TestObject ValueMethodUpdateValue(TestObject parameter)
            {
                parameter.value = "HelloWorld";
                return parameter;
            }

            public Task<int> ValueMethodAsync(int i, int j)
            {
                return Task.FromResult<int>(i + j);
            }

            public async Task VoidValueMethodAsync(int i)
            {
                await ValueMethodAsync(3, 4);
            }
            public Task<TestObject> ValueMethodWithReturnTypeAsync(int i)
            {
                return Task.FromResult<TestObject>(new TestObject() { value = "Hello" });
            }

            public async Task ValueMethodWithReturnVoidThrowsExceptionAsync(TestObject i)
            {
                await Task.CompletedTask;
                throw new NotImplementedException("Not Implemented Exception");
            }

            public async Task<TestObject> ValueMethodWithReturnTypeThrowsExceptionAsync(TestObject i)
            {
                await Task.CompletedTask;
                throw new NotImplementedException("Not Implemented Exception");
            }

            public Task<TestObject> ValueMethodUpdateValueAsync(TestObject parameter)
            {
                parameter.value = "HelloWorld";
                return Task.FromResult<TestObject>(parameter);
            }

            public TestAwaitable<TestObject> CustomAwaitableOfReferenceTypeAsync(
                string input1,
                int input2)
            {
                return new TestAwaitable<TestObject>(new TestObject
                {
                    value = $"{input1} {input2}"
                });
            }

            public TestAwaitable<int> CustomAwaitableOfValueTypeAsync(
                int input1,
                int input2)
            {
                return new TestAwaitable<int>(input1 + input2);
            }

            public TestAwaitableWithICriticalNotifyCompletion CustomAwaitableWithICriticalNotifyCompletion()
            {
                return new TestAwaitableWithICriticalNotifyCompletion();
            }

            public TestAwaitableWithoutICriticalNotifyCompletion CustomAwaitableWithoutICriticalNotifyCompletion()
            {
                return new TestAwaitableWithoutICriticalNotifyCompletion();
            }

            public ValueTask<int> ValueTaskOfValueType(int result)
            {
                return new ValueTask<int>(result);
            }

            public ValueTask<string> ValueTaskOfReferenceType(string result)
            {
                return new ValueTask<string>(result);
            }

            public void MethodWithMultipleParameters(int valueTypeParam, string referenceTypeParam)
            {
            }
        }

        public class TestAwaitable<T>
        {
            private T _result;
            private bool _isCompleted;
            private List<Action> _onCompletedCallbacks = new List<Action>();

            public TestAwaitable(T result)
            {
                _result = result;

                // Simulate a brief delay before completion
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    Thread.Sleep(100);
                    SetCompleted();
                });
            }

            private void SetCompleted()
            {
                _isCompleted = true;

                foreach (var callback in _onCompletedCallbacks)
                {
                    callback();
                }
            }

            public TestAwaiter GetAwaiter()
            {
                return new TestAwaiter(this);
            }

            public struct TestAwaiter : INotifyCompletion
            {
                private TestAwaitable<T> _owner;

                public TestAwaiter(TestAwaitable<T> owner) : this()
                {
                    _owner = owner;
                }

                public bool IsCompleted => _owner._isCompleted;

                public void OnCompleted(Action continuation)
                {
                    if (_owner._isCompleted)
                    {
                        continuation();
                    }
                    else
                    {
                        _owner._onCompletedCallbacks.Add(continuation);
                    }
                }

                public T GetResult()
                {
                    return _owner._result;
                }
            }
        }

        public class TestAwaitableWithICriticalNotifyCompletion
        {
            public TestAwaiterWithICriticalNotifyCompletion GetAwaiter()
                => new TestAwaiterWithICriticalNotifyCompletion();
        }

        public class TestAwaitableWithoutICriticalNotifyCompletion
        {
            public TestAwaiterWithoutICriticalNotifyCompletion GetAwaiter()
                => new TestAwaiterWithoutICriticalNotifyCompletion();
        }

        public class TestAwaiterWithICriticalNotifyCompletion
            : CompletionTrackingAwaiterBase, ICriticalNotifyCompletion
        {
        }

        public class TestAwaiterWithoutICriticalNotifyCompletion
            : CompletionTrackingAwaiterBase, INotifyCompletion
        {
        }

        public class CompletionTrackingAwaiterBase
        {
            private string _result;

            public bool IsCompleted { get; private set; }

            public string GetResult() => _result;

            public void OnCompleted(Action continuation)
            {
                _result = "Used OnCompleted";
                IsCompleted = true;
                continuation();
            }

            public void UnsafeOnCompleted(Action continuation)
            {
                _result = "Used UnsafeOnCompleted";
                IsCompleted = true;
                continuation();
            }
        }
    }
}
