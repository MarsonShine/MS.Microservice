using MS.Microservice.Core.Extension;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Xunit;

namespace MS.Microservice.Core.Tests.Coverage
{
    // ========== IEnumerableExtensions (Core.Extension) ==========
    public class IEnumerableExtensionsTests
    {
        [Fact] public void FindIndex_Found() { Assert.Equal(2, new[] { 1, 2, 3 }.FindIndex(x => x == 3)); }
        [Fact] public void FindIndex_NotFound() { Assert.Equal(-1, new[] { 1, 2 }.FindIndex(x => x == 5)); }
        [Fact] public void FindIndex_Empty() { Assert.Equal(-1, Array.Empty<int>().FindIndex(x => true)); }

        [Fact] public void ForEach_Action() { int sum = 0; new[] { 1, 2, 3 }.ForEach(x => sum += x); Assert.Equal(6, sum); }
        [Fact] public void ForEach_Indexed() { var idx = new List<int>(); new[] { 10, 20 }.ForEach((x, i) => idx.Add(i)); Assert.Equal(new[] { 0, 1 }, idx); }

        [Fact] public void Shuffle_IEnumerable() { var r = new[] { 1, 2, 3, 4, 5 }.Shuffle().ToList(); Assert.Equal(5, r.Count); }

        [Fact] public void Flatten_Simple() { var tree = new[] { new Node { V = 1, Children = new[] { new Node { V = 2 } } } }; var r = tree.Flatten(n => n.Children?.Select(c => c)).Select(n => n.V).ToList(); Assert.Equal(new[] { 1, 2 }, r); }
        [Fact] public void Flatten_Empty() { var r = Array.Empty<Node>().Flatten(n => n.Children?.Select(c => c)); Assert.Empty(r); }

        [Fact] public void JoinAsString() { Assert.Equal("a,b,c", new[] { "a", "b", "c" }.JoinAsString(",")); }
        [Fact] public void JoinAsString_Empty() { Assert.Equal("", Array.Empty<string>().JoinAsString(",")); }

        [Fact] public void ToArray_Convert() { var r = new[] { 1, 2, 3 }.ToArray(x => x * 2); Assert.Equal(new[] { 2, 4, 6 }, r); }
        [Fact] public void ToArray_Empty() { Assert.Empty(Array.Empty<int>().ToArray(x => x * 2)); }
    }

    public class Node { public int V; public Node[]? Children; }

    // ========== ExpressionStarter ==========
    public class ExpressionStarterTests
    {
        [Fact] public void Default_True() { var s = new ExpressionStarter<int>(true); Assert.True(s.UseDefaultExpression); Assert.False(s.IsStarted); }
        [Fact] public void Default_False() { var s = new ExpressionStarter<int>(false); Assert.True(s.UseDefaultExpression); Assert.False(s.IsStarted); }
        [Fact] public void Default_NoArg() { var s = new ExpressionStarter<int>(); Assert.False(s.IsStarted); }
        [Fact] public void Start_WithExpr() { var s = new ExpressionStarter<int>(x => x > 0); Assert.True(s.IsStarted); }
        [Fact] public void Start() { var s = new ExpressionStarter<int>(); s.Start(x => x > 0); Assert.True(s.IsStarted); }
        [Fact] public void Start_ThrowsTwice() { var s = new ExpressionStarter<int>(); s.Start(x => x > 0); Assert.Throws<Exception>(() => s.Start(x => x < 5)); }
        [Fact] public void Or_Started() { var s = new ExpressionStarter<int>(); s.Start(x => x > 0); s.Or(x => x < 10); Assert.Equal(ExpressionType.OrElse, s.Body.NodeType); }
        [Fact] public void Or_NotStarted() { var s = new ExpressionStarter<int>(); s.Or(x => x > 0); Assert.True(s.IsStarted); }
        [Fact] public void And_Started() { var s = new ExpressionStarter<int>(); s.Start(x => x > 0); s.And(x => x < 10); Assert.Equal(ExpressionType.AndAlso, s.Body.NodeType); }
        [Fact] public void And_NotStarted() { var s = new ExpressionStarter<int>(); s.And(x => x > 0); Assert.True(s.IsStarted); }

        [Fact] public void ImplicitOperator_Expression() { ExpressionStarter<int> s = new(true); Expression<Func<int, bool>> e = s; Assert.NotNull(e); }
        [Fact] public void ImplicitOperator_Func() { ExpressionStarter<int> s = new(true); Func<int, bool> f = s; Assert.NotNull(f); Assert.True(f(1)); }
        [Fact] public void ImplicitOperator_FromExpression() { Expression<Func<int, bool>> e = x => x > 0; ExpressionStarter<int> s = e; Assert.True(s.IsStarted); }

        [Fact] public void Compile() { var s = new ExpressionStarter<int>(true); var f = s.Compile(); Assert.True(f(5)); }

        [Fact] public void Properties() { var s = new ExpressionStarter<int>(x => x > 0); Assert.NotNull(s.Body); Assert.NotNull(s.Parameters); Assert.NotNull(s.ToString()); }

        [Fact] public void ToString_NoPredicate() { var s = new ExpressionStarter<int>(false); Assert.Equal("f => False", s.ToString()); }

        [Fact] public void DefaultExpression_Property() { var s = new ExpressionStarter<int>(); s.DefaultExpression = x => true; Assert.True(s.UseDefaultExpression); }
    }

    // Functional EnumerableExtensions — extension() methods not testable from another assembly
}
