using MS.Infrastructure.Util.Reflection;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;
using static MS.Microservice.Test.TypeNameHelper_Test.NestedType<string>;

namespace MS.Microservice.Test
{
    public class TypeNameHelper_Test
    {
        [Fact]
        public void DisplayTypeName_Test()
        {
            var name = TypeNameHelper.GetTypeDisplayName(this);
            var name1 = TypeNameHelper.GetTypeDisplayName(typeof(NestedType<string>));
            var name2 = TypeNameHelper.GetTypeDisplayName(typeof(ChildClassNestedType<string,int>));
            Assert.NotEmpty(name);
        }

        public class NestedType<T>
        {
            public List<T> List { get; set; } = null!;

            public class ChildClassNestedType<TChild, D>
                where TChild : notnull
            {
                public Dictionary<TChild, D> HashDic { get; set; } = null!;
            }
        }
    }
}
