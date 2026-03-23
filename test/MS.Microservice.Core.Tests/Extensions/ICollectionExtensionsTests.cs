using MS.Microservice.Core.Extension;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MS.Microservice.Core.Tests.Extensions
{
    public class ICollectionExtensionsTests
    {
        [Fact]
        public void IsNullOrEmpty_NullCollection_ReturnsTrue()
        {
            ICollection<int>? list = null;
            Assert.True(list.IsNullOrEmpty());
        }

        [Fact]
        public void IsNullOrEmpty_EmptyCollection_ReturnsTrue()
        {
            ICollection<int> list = new List<int>();
            Assert.True(list.IsNullOrEmpty());
        }

        [Fact]
        public void IsNullOrEmpty_NonEmptyCollection_ReturnsFalse()
        {
            ICollection<int> list = new List<int> { 1 };
            Assert.False(list.IsNullOrEmpty());
        }

        [Fact]
        public void AddIfNotContains_Single_NewItem_ReturnsTrue()
        {
            var list = new List<int> { 1, 2, 3 };
            bool added = list.AddIfNotContains(4);
            Assert.True(added);
            Assert.Contains(4, list);
        }

        [Fact]
        public void AddIfNotContains_Single_ExistingItem_ReturnsFalse()
        {
            var list = new List<int> { 1, 2, 3 };
            bool added = list.AddIfNotContains(2);
            Assert.False(added);
            Assert.Equal(3, list.Count);
        }

        [Fact]
        public void AddIfNotContains_Multiple_OnlyAddsNew()
        {
            var list = new List<int> { 1, 2, 3 };
            var added = list.AddIfNotContains(new[] { 2, 3, 4, 5 }).ToList();
            Assert.Equal(2, added.Count);
            Assert.Contains(4, added);
            Assert.Contains(5, added);
            Assert.Equal(5, list.Count);
        }        [Fact]
        public void RemoveAll_RemovesMatchingItems()
        {
            ICollection<int> list = new List<int> { 1, 2, 3, 4, 5 };
            var removed = list.RemoveAll(x => x > 3);
            Assert.Equal(new List<int> { 4, 5 }, removed);
            Assert.Equal(3, list.Count);
        }

        [Fact]
        public void RemoveAll_NoMatch_RemovesNothing()
        {
            ICollection<int> list = new List<int> { 1, 2, 3 };
            var removed = list.RemoveAll(x => x > 10);
            Assert.Empty(removed);
            Assert.Equal(3, list.Count);
        }        [Fact]
        public void ContainsAll_AllPresent_ReturnsTrue()
        {
            ICollection<int> list = new List<int> { 1, 2, 3, 4, 5 };
            Assert.True(list.ContainsAll(new[] { 1, 3, 5 }, EqualityComparer<int>.Default));
        }

        [Fact]
        public void ContainsAll_SomeMissing_ReturnsFalse()
        {
            ICollection<int> list = new List<int> { 1, 2, 3 };
            Assert.False(list.ContainsAll(new[] { 2, 3, 99 }, EqualityComparer<int>.Default));
        }

        [Fact]
        public void Shuffle_IList_ReturnsSameElements()
        {
            IList<int> list = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
            var shuffled = list.Shuffle();
            Assert.Equal(list.Count, shuffled.Count);
            Assert.True(list.OrderBy(x => x).SequenceEqual(shuffled.OrderBy(x => x)));
        }
    }
}
