using MS.Microservice.Core.Functional.Data.BinaryTree;
using Xunit;

namespace MS.Microservice.Core.Tests.Coverage
{
    public class BinaryTreeTests
    {
        // Tree<T> abstract class
        [Fact] public void Tree_Leaf_Match() { var t = Tree.Leaf(42); var r = t.Match(v => v, (l, r2) => 0); Assert.Equal(42, r); }
        [Fact] public void Tree_Branch_Match() { var l = Tree.Leaf(1); var r = Tree.Leaf(2); var b = Tree.Branch(l, r); var result = b.Match(v => v, (left, right) => left.Match(v2=>v2, (_,_)=>0) + right.Match(v2=>v2, (_,_)=>0)); Assert.Equal(3, result); }
        [Fact] public void Tree_Equals_Same() { Assert.Equal(Tree.Leaf(5), Tree.Leaf(5)); }
        [Fact] public void Tree_GetHashCode() { Assert.Equal(Tree.Leaf(1).GetHashCode(), Tree.Leaf(1).GetHashCode()); }
        [Fact] public void Tree_Equals_Obj() { Assert.True(Tree.Leaf(3).Equals((object)Tree.Leaf(3))); }

        // Map
        [Fact] public void Tree_Map_Leaf() { var t = Tree.Leaf(2); var r = t.Map(x => x * 3); Assert.Equal(6, r.Match(v => v, (_, _) => 0)); }
        [Fact] public void Tree_Map_Branch() { var t = Tree.Branch(Tree.Leaf(1), Tree.Leaf(2)); var r = t.Map(x => x + 10); var sum = r.Match(v => v, (l, r2) => l.Match(v2 => v2, (_, _) => 0) + r2.Match(v2 => v2, (_, _) => 0)); Assert.Equal(23, sum); }

        // Bind
        [Fact] public void Tree_Bind_Leaf() { var t = Tree.Leaf(1); var r = t.Bind(x => Tree.Leaf(x + 1)); Assert.Equal(2, r.Match(v => v, (_, _) => 0)); }

        // Insert
        [Fact] public void Tree_Insert() { var t = Tree.Leaf(1); var r = t.Insert(2); var str = r.ToString(); Assert.Contains("Branch", str); }

        // Aggregate
        [Fact] public void Tree_Aggregate_Binary() { var t = Tree.Branch(Tree.Leaf(3), Tree.Leaf(4)); var sum = t.Aggregate((a, b) => a + b); Assert.Equal(7, sum); }
        [Fact] public void Tree_Aggregate_Accumulator() { var t = Tree.Branch(Tree.Leaf(3), Tree.Leaf(4)); var sum = t.Aggregate(10, (acc, v) => acc + v); Assert.Equal(17, sum); }
        [Fact] public void Tree_Aggregate_Deep() { var t = Tree.Branch(Tree.Branch(Tree.Leaf(1), Tree.Leaf(2)), Tree.Leaf(3)); var sum = t.Aggregate(0, (acc, v) => acc + v); Assert.Equal(6, sum); }

        // ToString paths
        [Fact] public void Tree_Leaf_ToString() { Assert.Equal("42", Tree.Leaf(42).ToString()); }
        [Fact] public void Tree_Branch_ToString() { var b = Tree.Branch(Tree.Leaf(1), Tree.Leaf(2)); Assert.Contains("Branch", b.ToString()); Assert.Contains("1", b.ToString()); Assert.Contains("2", b.ToString()); }
    }
}
