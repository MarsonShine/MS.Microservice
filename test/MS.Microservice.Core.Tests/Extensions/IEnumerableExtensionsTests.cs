using MS.Microservice.Core.Extension;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MS.Microservice.Core.Tests.Extensions
{
    public class IEnumerableExtensionsTests
    {
        [Fact]
        public void FindIndex_MatchFound_ReturnsCorrectIndex()
        {
            var list = new List<int> { 10, 20, 30, 40 };
            Assert.Equal(2, list.FindIndex(x => x == 30));
        }

        [Fact]
        public void FindIndex_NoMatch_ReturnsMinusOne()
        {
            var list = new List<int> { 10, 20, 30 };
            Assert.Equal(-1, list.FindIndex(x => x == 99));
        }

        [Fact]
        public void FindIndex_NullPredicate_ThrowsArgumentNullException()
        {
            var list = new List<int> { 1 };
            Assert.Throws<ArgumentNullException>(() => list.FindIndex(null!));
        }

        [Fact]
        public void ForEach_Action_ExecutesForEachItem()
        {
            var list = new List<int> { 1, 2, 3 };
            var results = new List<int>();
            list.ForEach(x => results.Add(x * 2));
            Assert.Equal(new List<int> { 2, 4, 6 }, results);
        }

        [Fact]
        public void ForEach_NullAction_ThrowsArgumentNullException()
        {
            var list = new List<int> { 1 };
            Assert.Throws<ArgumentNullException>(() => list.ForEach((Action<int>)null!));
        }

        [Fact]
        public void ForEach_WithIndex_ProvidesCorrectIndices()
        {
            var list = new List<string> { "a", "b", "c" };
            var indices = new List<int>();
            var items = new List<string>();
            list.ForEach((item, index) =>
            {
                items.Add(item);
                indices.Add(index);
            });
            Assert.Equal(new List<string> { "a", "b", "c" }, items);
            Assert.Equal(new List<int> { 0, 1, 2 }, indices);
        }        [Fact]
        public void Shuffle_ReturnsSameElements()
        {
            // Use IList<T>.Shuffle() from ICollectionExtensions to avoid ambiguity
            // with System.Linq.Enumerable.Shuffle in .NET 10
            IList<int> list = Enumerable.Range(1, 100).ToList();
            var shuffled = list.Shuffle();
            Assert.Equal(list.Count, shuffled.Count);
            Assert.True(list.OrderBy(x => x).SequenceEqual(shuffled.OrderBy(x => x)));
        }

        [Fact]
        public void Flatten_SimpleTree_FlattensAll()
        {
            var tree = new List<TreeNode>
            {
                new TreeNode("root", new List<TreeNode>
                {
                    new TreeNode("child1", new List<TreeNode>
                    {
                        new TreeNode("grandchild1", null)
                    }),
                    new TreeNode("child2", null)
                })
            };

            var flat = tree.Flatten(n => n.Children).ToList();
            Assert.Equal(4, flat.Count);
            Assert.Contains(flat, n => n.Name == "root");
            Assert.Contains(flat, n => n.Name == "child1");
            Assert.Contains(flat, n => n.Name == "child2");
            Assert.Contains(flat, n => n.Name == "grandchild1");
        }

        [Fact]
        public void JoinAsString_CombinesWithSeparator()
        {
            var list = new List<string> { "a", "b", "c" };
            Assert.Equal("a, b, c", list.JoinAsString(", "));
        }

        [Fact]
        public void JoinAsString_NullSource_ThrowsArgumentNullException()
        {
            IEnumerable<string>? list = null;
            Assert.Throws<ArgumentNullException>(() => list.JoinAsString(","));
        }

        [Fact]
        public void ToArray_ConvertsElements()
        {
            var list = new List<int> { 1, 2, 3 };
            var result = list.ToArray<int, string>(x => (x * 10).ToString());
            Assert.Equal(new[] { "10", "20", "30" }, result);
        }

        [Fact]
        public void ToArray_NullConverter_ThrowsArgumentNullException()
        {
            var list = new List<int> { 1 };
            Assert.Throws<ArgumentNullException>(() => list.ToArray<int, string>(null!));
        }

        private class TreeNode
        {
            public string Name { get; }
            public List<TreeNode>? Children { get; }

            public TreeNode(string name, List<TreeNode>? children)
            {
                Name = name;
                Children = children;
            }
        }
    }
}
