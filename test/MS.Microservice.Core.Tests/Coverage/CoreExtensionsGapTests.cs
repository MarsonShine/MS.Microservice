using MS.Microservice.Core.Domain.Entity;
using MS.Microservice.Core.Extension;
using MS.Microservice.Core.Security.Summary;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace MS.Microservice.Core.Tests.Coverage
{
    // ========== ICollectionExtensions ==========
    public class ICollectionExtensionsTests
    {
        [Fact] public void IsNullOrEmpty_Null() { ICollection<int>? c = null; Assert.True(c.IsNullOrEmpty()); }
        [Fact] public void IsNullOrEmpty_Empty() { Assert.True(new List<int>().IsNullOrEmpty()); }
        [Fact] public void IsNullOrEmpty_NotEmpty() { Assert.False(new List<int> { 1 }.IsNullOrEmpty()); }

        [Fact] public void AddIfNotContains_New() { var l = new List<int> { 1 }; Assert.True(l.AddIfNotContains(2)); Assert.Equal(2, l.Count); }
        [Fact] public void AddIfNotContains_Existing() { var l = new List<int> { 1 }; Assert.False(l.AddIfNotContains(1)); Assert.Single(l); }

        [Fact] public void AddIfNotContainsRange() { var l = new List<int> { 1, 2 }; var added = l.AddIfNotContains(new[] { 2, 3, 4 }).ToList(); Assert.Equal(new[] { 3, 4 }, added); Assert.Equal(4, l.Count); }

        [Fact] public void RemoveAll() { ICollection<int> l = new List<int> { 1, 2, 3, 4 }; var removed = l.RemoveAll(x => x % 2 == 0); Assert.Equal(new[] { 2, 4 }, removed); Assert.Equal(new[] { 1, 3 }, l); }

        [Fact] public void ContainsAll_True() { var l = new List<int> { 1, 2, 3 }; Assert.True(l.ContainsAll(new[] { 1, 3 }, EqualityComparer<int>.Default)); }
        [Fact] public void ContainsAll_False() { var l = new List<int> { 1, 2, 3 }; Assert.False(l.ContainsAll(new[] { 1, 4 }, EqualityComparer<int>.Default)); }

        [Fact] public void Shuffle_ReturnsSameCount() { var l = new List<int> { 1, 2, 3, 4, 5 }; var result = l.Shuffle(); Assert.Equal(l.Count, result.Count); }

        [Fact] public void IntersectBy() { var a = new[] { 1, 2, 3, 4 }; var b = new[] { 2, 4, 6 }; var r = a.IntersectBy(b, x => x, x => x).ToList(); Assert.Equal(new[] { 2, 4 }, r); }

        [Fact] public void ToDistinctDictionary() { var items = new[] { "a", "b", "a" }; var d = items.ToDistinctDictionary(x => x, x => x.ToUpper()); Assert.Equal(2, d.Count); Assert.Equal("A", d["a"]); }

        [Fact] public void OrderByReference() { var target = new[] { 3, 1, 2 }; var reference = new[] { 1, 2, 3 }; var r = target.OrderByReference(reference).ToList(); Assert.Equal(new[] { 1, 2, 3 }, r); }

        [Fact] public void SafeOrderByReference_Small() { var target = new[] { 3, 1, 2 }; var reference = new[] { 1, 2, 3 }; var r = target.SafeOrderByReference(reference); Assert.Equal(new[] { 1, 2, 3 }, r); }

        [Fact] public void SafeOrderByReference_Large() { var target = Enumerable.Range(0, 200).Reverse().ToArray(); var reference = Enumerable.Range(0, 200).ToArray(); var r = target.SafeOrderByReference(reference); Assert.Equal(reference, r); }

        [Fact] public void OrderByReference_WithSelectors() { var target = new[] { "c", "a", "b" }; var reference = new[] { 3, 1, 2 }; var r = target.OrderByReference(reference, rk => rk, tk => tk[0] - 'a' + 1).ToList(); Assert.Equal(new[] { "c", "a", "b" }, r); }

        [Fact] public void SafeOrderByReference_WithSelectors_Small() { var target = new[] { "c", "a", "b" }; var reference = new[] { 3, 1, 2 }; var r = target.SafeOrderByReference(reference, rk => rk, tk => tk[0] - 'a' + 1); Assert.Equal(new[] { "c", "a", "b" }, r); }

        [Fact] public void SafeOrderByReference_WithSelectors_Large() { var target = Enumerable.Range(0, 200).Select(i => $"x{i:D3}").Reverse().ToArray(); var reference = target.Reverse().ToArray(); var r = target.SafeOrderByReference(reference, rk => rk, tk => tk); Assert.Equal(reference, r); }
    }

    // ========== ListHelper ==========
    public class ListHelperTests
    {
        [Fact] public void Shuffle_Static() { var l = new List<int> { 1, 2, 3, 4, 5 }; var r = ListHelper.Shuffle(l); Assert.Equal(5, r.Count); Assert.All(l, x => Assert.Contains(x, r)); }

        [Fact] public void ValidatedShuffle_Single() { var l = new List<int> { 1 }; l.ValidatedShuffle(); Assert.Single(l); }
        [Fact] public void ValidatedShuffle_Multiple() { var l = Enumerable.Range(1, 10).ToList(); l.ValidatedShuffle(); Assert.Equal(10, l.Count); Assert.All(Enumerable.Range(1, 10), x => Assert.Contains(x, l)); }
        [Fact] public void ValidatedShuffle_Empty() { var l = new List<int>(); l.ValidatedShuffle(); Assert.Empty(l); }
    }

    // ========== EntityHelper ==========
    // Test entity implementations
    public class EntityHelperTestEntity : IEntity
    {
        private readonly object[] _keys;
        public EntityHelperTestEntity(params object[] keys) => _keys = keys;
        public object[] GetKeys() => _keys;
    }

    public class EntityHelperIntEntity : IEntity<int>
    {
        public int Id { get; set; }
        public object[] GetKeys() => new object[] { Id };
        public EntityHelperIntEntity(int id) => Id = id;
    }

    public class EntityHelperLongEntity : IEntity<long>
    {
        public long Id { get; set; }
        public object[] GetKeys() => new object[] { Id };
        public EntityHelperLongEntity(long id) => Id = id;
    }

    public class EntityHelperOtherEntity : IEntity
    {
        private readonly object[] _keys;
        public EntityHelperOtherEntity(params object[] keys) => _keys = keys;
        public object[] GetKeys() => _keys;
    }

    public class EntityHelperTests
    {
        [Fact] public void EntityEquals_BothNull() { Assert.False(EntityHelper.EntityEquals(null!, null!)); }
        [Fact] public void EntityEquals_OneNull() { Assert.False(EntityHelper.EntityEquals(new EntityHelperTestEntity(1), null!)); Assert.False(EntityHelper.EntityEquals(null!, new EntityHelperTestEntity(1))); }
        [Fact] public void EntityEquals_SameReference() { var e = new EntityHelperTestEntity(1); Assert.True(EntityHelper.EntityEquals(e, e)); }
        [Fact] public void EntityEquals_DifferentTypes() { Assert.False(EntityHelper.EntityEquals(new EntityHelperTestEntity(1), new EntityHelperOtherEntity(1))); }
        [Fact] public void EntityEquals_DefaultKeys() { Assert.False(EntityHelper.EntityEquals(new EntityHelperTestEntity(0), new EntityHelperTestEntity(0))); }
        [Fact] public void EntityEquals_KeyLengthMismatch() { Assert.False(EntityHelper.EntityEquals(new EntityHelperTestEntity(1), new EntityHelperTestEntity(1, 2))); }
        [Fact] public void EntityEquals_KeysEqual() { Assert.True(EntityHelper.EntityEquals(new EntityHelperTestEntity(1, "a"), new EntityHelperTestEntity(1, "a"))); }
        [Fact] public void EntityEquals_KeysNotEqual() { Assert.False(EntityHelper.EntityEquals(new EntityHelperTestEntity(1), new EntityHelperTestEntity(2))); }
        [Fact] public void EntityEquals_NullKey() { Assert.False(EntityHelper.EntityEquals(new EntityHelperTestEntity(1), new EntityHelperTestEntity(new object[] { null! }))); }

        [Fact] public void HasDefaultKeys_True() { Assert.True(EntityHelper.HasDefaultKeys(new EntityHelperTestEntity(0))); }
        [Fact] public void HasDefaultKeys_NullKey() { Assert.True(EntityHelper.HasDefaultKeys(new EntityHelperTestEntity(new object[] { null! }))); }
        [Fact] public void HasDefaultKeys_False() { Assert.False(EntityHelper.HasDefaultKeys(new EntityHelperTestEntity(1))); }
        [Fact] public void HasDefaultKeys_Long() { Assert.True(EntityHelper.HasDefaultKeys(new EntityHelperTestEntity(0L))); }

        [Fact] public void HasDefaultId_True() { Assert.True(EntityHelper.HasDefaultId(new EntityHelperIntEntity(0))); }
        [Fact] public void HasDefaultId_False() { Assert.True(EntityHelper.HasDefaultId(new EntityHelperIntEntity(-1))); }
        [Fact] public void HasDefaultId_Long() { Assert.True(EntityHelper.HasDefaultId(new EntityHelperLongEntity(0))); Assert.False(EntityHelper.HasDefaultId(new EntityHelperLongEntity(1))); }
    }

    // ========== Md5 ==========
    public class Md5Tests
    {
        [Fact] public void Encrypt_Empty() { Assert.Equal(string.Empty, Md5.Encrypt("", Encoding.UTF8)); }
        [Fact] public void Encrypt_Whitespace() { Assert.Equal(string.Empty, Md5.Encrypt("   ", Encoding.UTF8)); }
        [Fact] public void Encrypt_Normal() { var result = Md5.Encrypt("hello", Encoding.UTF8); Assert.Equal(32, result.Length); }
        [Fact] public void Encrypt_Span() { var bytes = Encoding.UTF8.GetBytes("hello"); var result = Md5.Encrypt((ReadOnlySpan<byte>)bytes.AsSpan()); Assert.Equal(47, result.Length); }
        [Fact] public void Encrypt_Consistency() { Assert.Equal(Md5.Encrypt("abc", Encoding.UTF8), Md5.Encrypt("abc", Encoding.UTF8)); }
    }
}


