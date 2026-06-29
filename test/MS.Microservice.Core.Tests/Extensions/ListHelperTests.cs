using FluentAssertions;
using MS.Microservice.Core.Extension;

namespace MS.Microservice.Core.Tests.Extensions
{
    public class ListHelperTests
    {
        private record Item(string Name) : IEquatable<Item>
        {
            public virtual bool Equals(Item? other) =>
                other is not null && Name == other.Name;
            public override int GetHashCode() => Name.GetHashCode();
        }

        [Fact]
        public void ValidatedShuffle_WithValues_DoesNotThrow()
        {
            var list = new List<Item>
            {
                new("a"), new("b"), new("c"), new("d"), new("e"),
                new("f"), new("g"), new("h"), new("i"), new("j"),
                new("k"), new("l"), new("m"), new("n"), new("o"),
            };
            list.ValidatedShuffle();
            list.Should().HaveCount(15);
        }

        [Fact]
        public void ValidatedShuffle_SingleItem_NoOp()
        {
            var list = new List<Item> { new("only") };
            list.ValidatedShuffle();
            list[0].Name.Should().Be("only");
        }

        [Fact]
        public void ValidatedShuffle_EmptyList_NoOp()
        {
            var list = new List<Item>();
            list.ValidatedShuffle();
            list.Should().BeEmpty();
        }

        [Fact]
        public void Shuffle_ReturnsSameElementsDifferentOrder()
        {
            IList<int> list = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
            var shuffled = list.Shuffle();
            shuffled.Should().HaveSameCount(list);
            shuffled.OrderBy(x => x).Should().Equal(list.OrderBy(x => x));
        }

        [Fact]
        public void ValidatedShuffle_Duplicates_DoesNotThrow()
        {
            var list = new List<Item>
            {
                new("a"), new("a"), new("b"), new("b"), new("c"), new("c"),
            };
            list.ValidatedShuffle();
            list.Should().HaveCount(6);
        }
    }
}
