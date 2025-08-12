namespace System.Numerics
{
    /// <summary>
    /// Math 类常见运算的通用泛型扩展方法
    /// </summary>
    public static class MathExtensions
    {
        #region Round 扩展方法

        /// <summary>
        /// 泛型 Round 方法 - 四舍五入到最接近的整数
        /// </summary>
        public static T Round<T>(this T value) where T : IFloatingPoint<T>
        {
            return T.Round(value);
        }

        /// <summary>
        /// 泛型 Round 方法 - 四舍五入到指定小数位数
        /// </summary>
        public static T Round<T>(this T value, int digits) where T : IFloatingPoint<T>
        {
            var factor = GetPowerOfTen<T>(digits);
            return T.Round(value * factor) / factor;
        }

        /// <summary>
        /// 泛型 Round 方法 - 使用指定的舍入规则
        /// </summary>
        public static T Round<T>(this T value, MidpointRounding mode) where T : IFloatingPoint<T>
        {
            return T.Round(value, mode);
        }

        #endregion

        #region Ceiling 和 Floor 扩展方法

        /// <summary>
        /// 泛型 Ceiling 方法 - 向上取整
        /// </summary>
        public static T Ceiling<T>(this T value) where T : IFloatingPoint<T>
        {
            return T.Ceiling(value);
        }

        /// <summary>
        /// 泛型 Floor 方法 - 向下取整
        /// </summary>
        public static T Floor<T>(this T value) where T : IFloatingPoint<T>
        {
            return T.Floor(value);
        }

        /// <summary>
        /// 泛型 Truncate 方法 - 截断小数部分
        /// </summary>
        public static T Truncate<T>(this T value) where T : IFloatingPoint<T>
        {
            return T.Truncate(value);
        }

        #endregion

        #region 绝对值和符号相关

        /// <summary>
        /// 泛型 Abs 方法 - 绝对值
        /// </summary>
        public static T Abs<T>(this T value) where T : INumber<T>
        {
            return T.Abs(value);
        }

        /// <summary>
        /// 泛型 Sign 方法 - 符号
        /// </summary>
        public static int Sign<T>(this T value) where T : INumber<T>
        {
            return T.Sign(value);
        }

        #endregion

        #region 最值和限制方法

        /// <summary>
        /// 泛型 Min 方法 - 最小值
        /// </summary>
        public static T Min<T>(this T value, T other) where T : INumber<T>
        {
            return value < other ? value : other;
        }

        /// <summary>
        /// 泛型 Max 方法 - 最大值
        /// </summary>
        public static T Max<T>(this T value, T other) where T : INumber<T>
        {
            return value > other ? value : other;
        }

        /// <summary>
        /// 泛型 Clamp 方法 - 将值限制在指定范围内
        /// </summary>
        public static T Clamp<T>(this T value, T min, T max) where T : INumber<T>
        {
            if (min > max)
                throw new ArgumentException("min should be less than or equal to max");

            return value < min ? min : value > max ? max : value;
        }

        #endregion

        #region 数学运算

        /// <summary>
        /// 泛型 Pow 方法 - 幂运算
        /// </summary>
        public static T Pow<T>(this T value, T exponent) where T : IPowerFunctions<T>
        {
            return T.Pow(value, exponent);
        }

        /// <summary>
        /// 泛型 Sqrt 方法 - 平方根
        /// </summary>
        public static T Sqrt<T>(this T value) where T : IRootFunctions<T>
        {
            return T.Sqrt(value);
        }

        /// <summary>
        /// 泛型 Log 方法 - 自然对数
        /// </summary>
        public static T Log<T>(this T value) where T : ILogarithmicFunctions<T>
        {
            return T.Log(value);
        }

        /// <summary>
        /// 泛型 Log 方法 - 指定底数的对数
        /// </summary>
        public static T Log<T>(this T value, T baseValue) where T : ILogarithmicFunctions<T>
        {
            return T.Log(value, baseValue);
        }

        #endregion

        #region 三角函数

        /// <summary>
        /// 泛型 Sin 方法 - 正弦
        /// </summary>
        public static T Sin<T>(this T value) where T : ITrigonometricFunctions<T>
        {
            return T.Sin(value);
        }

        /// <summary>
        /// 泛型 Cos 方法 - 余弦
        /// </summary>
        public static T Cos<T>(this T value) where T : ITrigonometricFunctions<T>
        {
            return T.Cos(value);
        }

        /// <summary>
        /// 泛型 Tan 方法 - 正切
        /// </summary>
        public static T Tan<T>(this T value) where T : ITrigonometricFunctions<T>
        {
            return T.Tan(value);
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 获取 10 的指定次幂
        /// </summary>
        private static T GetPowerOfTen<T>(int exponent) where T : IFloatingPoint<T>
        {
            T result = T.One;
            T ten = T.CreateChecked(10);

            if (exponent >= 0)
            {
                for (int i = 0; i < exponent; i++)
                {
                    result *= ten;
                }
            }
            else
            {
                for (int i = 0; i < -exponent; i++)
                {
                    result /= ten;
                }
            }

            return result;
        }

        #endregion
    }
}