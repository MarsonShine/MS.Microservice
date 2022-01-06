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

namespace MS.Microservice.Core.Test
{
    public class ObjectMethodExecutorTest
    {
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

        private static ObjectMethodExecutor GetExecutor(string methodName)
        {
            var type = typeof(TestController);
            var methodInfo = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(methodInfo);
            return ObjectMethodExecutor.Create(methodInfo, type.GetTypeInfo());
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
    }
}
