using MS.Microservice.Core.Functional;
using MS.Microservice.Core.Functional.Data.BinaryTree;
using MS.Microservice.Core.Functional.Data.LabelTree;
using MS.Microservice.Core.Functional.Data.LinkedList;
using static MS.Microservice.Core.Functional.F;
using static MS.Microservice.Core.Functional.Data.BinaryTree.Tree;
using static MS.Microservice.Core.Functional.Data.LabelTree.LabelTree;
using static MS.Microservice.Core.Functional.Data.LinkedList.LinkedList;

namespace MS.Microservice.Core.Tests.Functional
{
    public class DataStructuresTests
    {
        [Fact]
        public void ReadOnlyListTraversalVisitsLongChainInOrderAndStopsWhenTheCallbackThrows()
        {
            const int count = 8192;
            var list = List(Enumerable.Range(0, count).ToArray());
            Assert.Equal(count, list.Length());
            Assert.Equal((Option<int>)Some(count - 1), list[count - 1]);
            Assert.True(list[count].IsNone);
            Assert.Equal(Enumerable.Range(0, count), list.AsEnumerable());
            Assert.Equal(count * (count - 1) / 2, list.Aggregate(0, (sum, value) => sum + value));

            int visited = 0;
            var stop = new InvalidOperationException();
            var error = Assert.Throws<InvalidOperationException>(() => list.ForEach(value =>
            {
                Assert.Equal(visited++, value);
                if (value == count - 2) throw stop;
            }));
            Assert.Same(stop, error);
            Assert.Equal(count - 1, visited);
        }

        [Fact]
        public void LeftFoldAndPartialEnumerationPreserveTheSharedTail()
        {
            var tail = List(2, 3);
            var list = tail.Add(1);
            using var prefix = list.AsEnumerable().GetEnumerator();
            Assert.True(prefix.MoveNext());
            Assert.Equal(1, prefix.Current);

            Assert.Equal(4123, list.Aggregate(4, (digits, next) => digits * 10 + next));
            Assert.Equal(423, tail.Aggregate(4, (digits, next) => digits * 10 + next));
            Assert.Same(tail, list.Tail.GetOrElse(() => throw new InvalidOperationException()));
            Assert.True(prefix.MoveNext());
            Assert.Equal(2, prefix.Current);
            Assert.Equal(new[] { 1, 2, 3 }, list.AsEnumerable());
        }

        [Fact]
        public void EmptyFoldReturnsSeedWithoutInvokingAReducer()
        {
            var seed = new object();
            Assert.Same(seed, List<int>().Aggregate(seed, null!));
        }

        [Fact]
        public void InsertAt_InsertsValueAtGivenIndex()
        {
            var list = List(1, 2, 4);

            var result = list.InsertAt(2, 3);

            Assert.True(result.IsSome);
            Assert.Equal(
                new[] { 1, 2, 3, 4 },
                result.Match(
                    none: Array.Empty<int>,
                    some: updated => updated.AsEnumerable().ToArray()));
        }

        [Fact]
        public void RemoveAt_RemovesValueAtGivenIndex()
        {
            var list = List(1, 2, 3, 4);

            var result = list.RemoveAt(1);

            Assert.True(result.IsSome);
            Assert.Equal(
                new[] { 1, 3, 4 },
                result.Match(
                    none: Array.Empty<int>,
                    some: updated => updated.AsEnumerable().ToArray()));
        }

        [Fact]
        public void InsertAt_ReturnsNone_WhenIndexIsOutOfRange()
        {
            var list = List(1, 2, 3);

            var result = list.InsertAt(4, 99);

            Assert.True(result.IsNone);
        }

        [Fact]
        public void RemoveAt_ReturnsNone_WhenIndexIsOutOfRange()
        {
            var list = List(1, 2, 3);

            var result = list.RemoveAt(3);

            Assert.True(result.IsNone);
        }

        [Fact]
        public void TakeWhile_ReturnsPrefixUntilPredicateFails()
        {
            var list = List(1, 2, 3, 1);

            var result = list.TakeWhile(x => x < 3);

            Assert.Equal(new[] { 1, 2 }, result.AsEnumerable());
        }

        [Fact]
        public void DropWhile_SkipsPrefixWhilePredicateHolds()
        {
            var list = List(1, 2, 3, 1);

            var result = list.DropWhile(x => x < 3);

            Assert.Equal(new[] { 3, 1 }, result.AsEnumerable());
        }

        [Fact]
        public void EnumerableTakeWhile_ReturnsPrefixLazily()
        {
            var source = new[] { 1, 2, 3, 4 };

            var result = LinkedList.TakeWhile<int>(source, x => x < 3).ToArray();

            Assert.Equal(new[] { 1, 2 }, result);
        }

        [Fact]
        public void EnumerableDropWhile_ReturnsSuffixAfterPredicateFails()
        {
            var source = new[] { 1, 2, 3, 4 };

            var result = LinkedList.DropWhile<int>(source, x => x < 3).ToArray();

            Assert.Equal(new[] { 3, 4 }, result);
        }

        [Fact]
        public void TreeBind_ReplacesEachLeafWithReturnedTree()
        {
            var tree = Branch(Leaf(1), Branch(Leaf(2), Leaf(3)));

            var result = tree.Bind(x => Branch(Leaf(x), Leaf(x * 10)));

            Assert.Equal(
                "Branch(Branch(1, 10), Branch(Branch(2, 20), Branch(3, 30)))",
                result.ToString());
        }

        [Fact]
        public void LabelTreeMap_TransformsEveryLabel()
        {
            var tree = Node("root", Node("child-1"), Node("child-2", Node("leaf")));

            var result = tree.Map(label => label.ToUpperInvariant());
            var leafLabel =
                from restChildren in result.Children.Tail
                from secondChild in restChildren.Head
                from leaf in secondChild.Children.Head
                select leaf.Label;

            Assert.Equal("ROOT", result.Label);
            Assert.Equal(new[] { "CHILD-1", "CHILD-2" }, result.Children.AsEnumerable().Select(x => x.Label));
            Assert.Equal((Option<string>)Some("LEAF"), leafLabel);
        }

        [Fact]
        public void LabelTreeLocalize_UsesTranslationsWhenAvailable()
        {
            var tree = Node("nav.home", Node("nav.products"), Node("nav.missing"));
            var translations = new Dictionary<string, string>
            {
                ["nav.home"] = "首页",
                ["nav.products"] = "产品"
            };

            var result = tree.Localize(translations);
            var secondChildLabel =
                from restChildren in result.Children.Tail
                from secondChild in restChildren.Head
                select secondChild.Label;

            Assert.Equal("首页", result.Label);
            Assert.Equal((Option<string>)Some("产品"), result.Children.Head.Map(x => x.Label));
            Assert.Equal((Option<string>)Some("nav.missing"), secondChildLabel);
        }
    }
}
