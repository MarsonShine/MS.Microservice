using FastMember;
using MS.Microservice.Core.Reflection.FastMember;
using MS.Microservice.Test.Etos;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace MS.Microservice.Test
{
    public class FastMemberTest
    {
        [Fact]
        public void TypeAccessor_Test()
        {
            UserEto user = new UserEto("marsonshine", 26, true);
            var accessor = TypeAccessor.Create(typeof(UserEto));

            Assert.Equal("marsonshine", accessor[user, "UserName"]);
            Assert.Equal(26, accessor[user, "Age"]);
        }
    }
}
