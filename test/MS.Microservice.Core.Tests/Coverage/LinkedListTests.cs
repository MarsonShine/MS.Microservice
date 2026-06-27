using System.Linq;
using MS.Microservice.Core.Functional;
using MS.Microservice.Core.Functional.Data.LinkedList;
using static MS.Microservice.Core.Functional.Data.LinkedList.LinkedList;
using Xunit;

namespace MS.Microservice.Core.Tests.Coverage
{
    public class LinkedListTests
    {
        [Fact] public void List_Params() { var l = List(1, 2, 3); Assert.Equal(3, l.Length()); }
        [Fact] public void List_Empty() { var l = List<int>(); Assert.Equal(0, l.Length()); }
        [Fact] public void List_Head() { var l = List(1, 2, 3); Assert.True(l.Head.IsSome); }
        [Fact] public void List_Tail() { var l = List(1, 2, 3); Assert.True(l.Tail.IsSome); }
        [Fact] public void List_Index() { var l = List(10, 20, 30); Assert.True(l[1].IsSome); }
        [Fact] public void List_Index_Negative() { var l = List(10); Assert.False(l[-1].IsSome); }
        [Fact] public void List_AsEnumerable() { var l = List(1, 2); Assert.Equal(new[] { 1, 2 }, l.AsEnumerable()); }
        [Fact] public void List_ToString() { var l = List(1, 2); var s = l.ToString(); Assert.Contains("1", s); Assert.Contains("2", s); }
        [Fact] public void List_ToString_Empty() { Assert.Equal("{ }", List<int>().ToString()); }

        [Fact] public void Length_Empty() { Assert.Equal(0, List<int>().Length()); }
        [Fact] public void Length_Three() { Assert.Equal(3, List(1, 2, 3).Length()); }

        [Fact] public void Add() { var l = List(1, 2); var r = l.Add(0); Assert.Equal(3, r.Length()); }
        [Fact] public void Append() { var l = List(1, 2); var r = l.Append(3); Assert.Equal(3, r.Length()); }

        [Fact] public void InsertAt_Head() { var l = List(2, 3); var r = l.InsertAt(0, 1); Assert.True(r.IsSome); }
        [Fact] public void InsertAt_Mid() { var l = List(1, 3); var r = l.InsertAt(1, 2); Assert.True(r.IsSome); }
        [Fact] public void InsertAt_Negative() { Assert.False(List(1).InsertAt(-1, 0).IsSome); }
        [Fact] public void InsertAt_Beyond() { Assert.False(List<int>().InsertAt(5, 1).IsSome); }

        [Fact] public void RemoveAt_Head() { var l = List(1, 2, 3); var r = l.RemoveAt(0); Assert.True(r.IsSome); }
        [Fact] public void RemoveAt_Mid() { var l = List(1, 2, 3); var r = l.RemoveAt(1); Assert.True(r.IsSome); }
        [Fact] public void RemoveAt_Negative() { Assert.False(List(1).RemoveAt(-1).IsSome); }

        [Fact] public void TakeWhile_All() { var l = List(1, 2, 3); Assert.Equal(3, l.TakeWhile(x => x < 10).Length()); }
        [Fact] public void TakeWhile_Partial() { var l = List(1, 2, 3, 0); Assert.Equal(3, l.TakeWhile(x => x > 0).Length()); }

        [Fact] public void DropWhile_All() { var l = List(1, 2, 3); Assert.Equal(0, l.DropWhile(x => x < 10).Length()); }
        [Fact] public void DropWhile_Partial() { var l = List(1, 2, 3); Assert.Equal(2, l.DropWhile(x => x < 2).Length()); }

        [Fact] public void Map() { var l = List(1, 2); var r = l.Map(x => x * 2); Assert.Equal(2, r.Length()); Assert.True(r.Head.IsSome); }
        [Fact] public void ForEach() { int sum = 0; List(1, 2, 3).ForEach(x => sum += x); Assert.Equal(6, sum); }

        [Fact] public void Bind() { var l = List(2, 3); var r = l.Bind(x => List(x, x * 2)); Assert.Equal(4, r.Length()); }
        [Fact] public void Join() { var ll = List(List(1, 2), List(3, 4)); var r = ll.Join(); Assert.Equal(4, r.Length()); }

        [Fact] public void Aggregate_Sum() { var l = List(1, 2, 3); Assert.Equal(6, l.Aggregate(0, (a, x) => a + x)); }
        [Fact] public void Aggregate_Empty() { Assert.Equal(10, List<int>().Aggregate(10, (a, x) => a + x)); }

        [Fact] public void TakeWhile_IEnumerable_All() { var e = new[] { 1, 2, 3 }; Assert.Equal(3, LinkedList.TakeWhile(e, x => x > 0).Count()); }
        [Fact] public void TakeWhile_IEnumerable_Partial() { var e = new[] { 1, 2, 0, 3 }; Assert.Equal(2, LinkedList.TakeWhile(e, x => x > 0).Count()); }

        [Fact] public void DropWhile_IEnumerable_All() { var e = new[] { 1, 2, 3 }; Assert.Empty(LinkedList.DropWhile(e, x => x < 10)); }
        [Fact] public void DropWhile_IEnumerable_Partial() { var e = new[] { 1, 2, 3 }; Assert.Equal(2, LinkedList.DropWhile(e, x => x < 2).Count()); }
    }
}
