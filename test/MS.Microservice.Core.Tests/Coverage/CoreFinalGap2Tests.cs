using MS.Microservice.Core.Domain.Entity;
using MS.Microservice.Core.Extension;
using MS.Microservice.Core.Serialization;
using System;
using System.Linq.Expressions;
using System.Numerics;
using System.Text;
using Xunit;

namespace MS.Microservice.Core.Tests.Coverage
{
    public class DefaultSerializeSettingTests
    {
        [Fact] public void Default_NotNull() { Assert.NotNull(DefaultSerializeSetting.Default); }
        [Fact] public void Default_PropertyNamingPolicy() { Assert.NotNull(DefaultSerializeSetting.Default.PropertyNamingPolicy); }
        [Fact] public void Default_PropertyNameCaseInsensitive() { Assert.True(DefaultSerializeSetting.Default.PropertyNameCaseInsensitive); }
        [Fact] public void ChinesseEncoder_NotNull() { Assert.NotNull(DefaultSerializeSetting.ChineseEncoder); }
    }

    public class MathExtensionsMoreTests
    {
        [Fact] public void Round_NegativeDigits_Double() { var v = 1234.56; var r = v.Round(-1); Assert.Equal(1230.0, (double)r, 1); }
        [Fact] public void Round_NegativeDigits_Decimal() { var v = 1234.56m; var r = v.Round(-1); Assert.Equal(1230m, (decimal)r); }
        [Fact] public void Round_NegativeDigits_Float() { var v = 1234.56f; var r = v.Round(-2); Assert.Equal(1200f, (float)r, 10); }
    }

    public class Md5MoreTests
    {
        [Fact] public void Encrypt_Unicode() { var r = MS.Microservice.Core.Security.Summary.Md5.Encrypt("你好", Encoding.UTF8); Assert.Equal(32, r.Length); }
        [Fact] public void Encrypt_SpecialChars() { var r = MS.Microservice.Core.Security.Summary.Md5.Encrypt("!@#$%", Encoding.ASCII); Assert.Equal(32, r.Length); }
    }

    public class EntityHelperMoreTests
    {
        [Fact] public void EntityEquals_Entity1MoreKeys() { Assert.False(EntityHelper.EntityEquals(new EH_Entity(1, 2), new EH_Entity(1))); }
        [Fact] public void EntityEquals_Entity1NullKey() { Assert.False(EntityHelper.EntityEquals(new EH_Entity(new object[] { null! }), new EH_Entity(1))); }
        [Fact] public void HasDefaultKeys_MultipleKeys_AllDefault() { Assert.True(EntityHelper.HasDefaultKeys(new EH_Entity(0, 0L))); }
        [Fact] public void HasDefaultKeys_MultipleKeys_OneNotDefault() { Assert.False(EntityHelper.HasDefaultKeys(new EH_Entity(1, 0L))); }
        [Fact] public void HasDefaultId_NegativeLong() { Assert.True(EntityHelper.HasDefaultId(new EH_LongEntity(-1L))); }
        [Fact] public void HasDefaultId_IntMaxCheck() { Assert.True(EntityHelper.HasDefaultId(new EH_LongEntity(0L))); }
    }

    public class EH_Entity : IEntity
    {
        private readonly object[] _keys;
        public EH_Entity(params object[] keys) => _keys = keys;
        public object[] GetKeys() => _keys;
    }

    public class EH_LongEntity : IEntity<long>
    {
        public long Id { get; set; }
        public object[] GetKeys() => new object[] { Id };
        public EH_LongEntity(long id) => Id = id;
    }

    public class ExpressionStarterMoreTests
    {
        [Fact] public void Update() { var s = new ExpressionStarter<int>(x => x > 0); var u = s.Update(s.Body, s.Parameters); Assert.NotNull(u); }
        [Fact] public void CanReduce() { var s = new ExpressionStarter<int>(true); Assert.False(s.CanReduce); }
        [Fact] public void Body() { var s = new ExpressionStarter<int>(x => x > 0); Assert.Equal(ExpressionType.GreaterThan, s.Body.NodeType); }
        [Fact] public void NodeType() { var s = new ExpressionStarter<int>(x => x > 0); Assert.Equal(ExpressionType.Lambda, s.NodeType); }
        [Fact] public void ReturnType() { var s = new ExpressionStarter<int>(x => x > 0); Assert.Equal(typeof(bool), s.ReturnType); }
        [Fact] public void Parameters() { var s = new ExpressionStarter<int>(x => x > 0); Assert.Single(s.Parameters); }
        [Fact] public void Type_() { var s = new ExpressionStarter<int>(x => x > 0); Assert.NotNull(s.Type); }
        [Fact] public void TailCall() { var s = new ExpressionStarter<int>(x => x > 0); Assert.False(s.TailCall); }
    }
}
