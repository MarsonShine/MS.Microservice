using System.Numerics;
using Microsoft.System.Collection;
using MS.Microservice.Core.Functional;
using Xunit;

namespace MS.Microservice.Core.Tests.Coverage
{
    public class ExtensionCoverageTests
    {
        // ========== MathExtensions - IFloatingPoint ==========
        [Fact] public void Math_Round_Double() { Assert.Equal(3.0, 3.14159.Round()); }
        [Fact] public void Math_Round_Digits_Double() { Assert.Equal(3.14, 3.14159.Round(2)); }
        [Fact] public void Math_Round_Midpoint_Double() { Assert.Equal(3.0, 3.14159.Round(MidpointRounding.ToZero)); }
        [Fact] public void Math_Ceiling_Double() { Assert.Equal(4.0, 3.14.Ceiling()); Assert.Equal(-3.0, (-3.14).Ceiling()); }
        [Fact] public void Math_Floor_Double() { Assert.Equal(3.0, 3.14.Floor()); Assert.Equal(-4.0, (-3.14).Floor()); }
        [Fact] public void Math_Truncate_Double() { Assert.Equal(3.0, 3.99.Truncate()); Assert.Equal(-3.0, (-3.99).Truncate()); }

        // ========== MathExtensions - INumber ==========
        [Fact] public void Math_Abs_Int() { Assert.Equal(5, (-5).Abs()); Assert.Equal(0, 0.Abs()); }
        [Fact] public void Math_Sign_Int() { Assert.Equal(1, 42.Sign()); Assert.Equal(-1, (-42).Sign()); Assert.Equal(0, 0.Sign()); }
        [Fact] public void Math_Min_Double() { Assert.Equal(3.0, 5.0.Min(3.0)); Assert.Equal(3.0, 3.0.Min(5.0)); }
        [Fact] public void Math_Max_Double() { Assert.Equal(5.0, 5.0.Max(3.0)); Assert.Equal(5.0, 3.0.Max(5.0)); }
        [Fact] public void Math_Clamp_InRange() { Assert.Equal(5.0, 5.0.Clamp(0.0, 10.0)); }
        [Fact] public void Math_Clamp_BelowMin() { Assert.Equal(0.0, (-5.0).Clamp(0.0, 10.0)); }
        [Fact] public void Math_Clamp_AboveMax() { Assert.Equal(10.0, 20.0.Clamp(0.0, 10.0)); }
        [Fact] public void Math_Clamp_InvalidRange() { Assert.Throws<ArgumentException>(() => 3.0.Clamp(10.0, 0.0)); }
        [Fact] public void Math_Abs_Double() { Assert.Equal(3.14, (-3.14).Abs()); }
        [Fact] public void Math_Sign_Double() { Assert.Equal(1, 3.14.Sign()); Assert.Equal(-1, (-3.14).Sign()); }

        // ========== MathExtensions - IPowerFunctions ==========
        [Fact] public void Math_Pow_Double() { Assert.Equal(8.0, 2.0.Pow(3.0)); }

        // ========== MathExtensions - IRootFunctions ==========
        [Fact] public void Math_Sqrt_Double() { Assert.Equal(4.0, 16.0.Sqrt()); }

        // ========== MathExtensions - ILogarithmicFunctions ==========
        [Fact] public void Math_Log_Natural() { Assert.Equal(1.0, Math.E.Log(), 1e-10); }
        [Fact] public void Math_Log_Base() { Assert.Equal(2.0, 100.0.Log(10.0), 1e-10); }

        // ========== MathExtensions - ITrigonometricFunctions ==========
        [Fact] public void Math_Sin_Double() { Assert.Equal(0.0, 0.0.Sin(), 1e-10); Assert.Equal(1.0, (Math.PI / 2).Sin(), 1e-10); }
        [Fact] public void Math_Cos_Double() { Assert.Equal(1.0, 0.0.Cos(), 1e-10); }
        [Fact] public void Math_Tan_Double() { Assert.Equal(0.0, 0.0.Tan(), 1e-10); }

        // ========== IEnumerableExtensions ==========
        [Fact] public void IEnumerable_Shuffle() { var list = new[] { 1, 2, 3, 4, 5 }; var shuffled = list.Shuffle().ToArray(); Assert.Equal(5, shuffled.Length); }

        // ========== FuncExtensions - CurryFirst ==========
        [Fact] public void CurryFirst_T3() { var curried = F.CurryFirst<int, int, int, int>((a, b, c) => a + b + c); Assert.Equal(6, curried(1)(2, 3)); }
        [Fact] public void CurryFirst_T4() { var curried = F.CurryFirst<int, int, int, int, int>((a, b, c, d) => a + b + c + d); Assert.Equal(10, curried(1)(2, 3, 4)); }
        [Fact] public void CurryFirst_T5() { var curried = F.CurryFirst<int, int, int, int, int, int>((a, b, c, d, e) => a + b + c + d + e); Assert.Equal(15, curried(1)(2, 3, 4, 5)); }
        [Fact] public void CurryFirst_T6() { var curried = F.CurryFirst<int, int, int, int, int, int, int>((a, b, c, d, e, f) => a + b + c + d + e + f); Assert.Equal(21, curried(1)(2, 3, 4, 5, 6)); }
        [Fact] public void CurryFirst_T7() { var curried = F.CurryFirst<int, int, int, int, int, int, int, int>((a, b, c, d, e, f, g) => a + b + c + d + e + f + g); Assert.Equal(28, curried(1)(2, 3, 4, 5, 6, 7)); }
        [Fact] public void CurryFirst_T8() { var curried = F.CurryFirst<int, int, int, int, int, int, int, int, int>((a, b, c, d, e, f, g, h) => a + b + c + d + e + f + g + h); Assert.Equal(36, curried(1)(2, 3, 4, 5, 6, 7, 8)); }
        [Fact] public void CurryFirst_T9() { var curried = F.CurryFirst<int, int, int, int, int, int, int, int, int, int>((a, b, c, d, e, f, g, h, i) => a + b + c + d + e + f + g + h + i); Assert.Equal(45, curried(1)(2, 3, 4, 5, 6, 7, 8, 9)); }

    }
}
