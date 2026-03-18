namespace System.Numerics
{
    /// <summary>
    /// Math 类常见运算的通用泛型扩展方法
    /// </summary>
    public static partial class MathExtensions
    {
        extension<T>(T value) where T : IFloatingPoint<T>
        {
            /// <summary>
            /// 泛型 Round 方法 - 四舍五入到最接近的整数
            /// </summary>
            public T Round()
            {
                return T.Round(value);
            }

            /// <summary>
            /// 泛型 Round 方法 - 四舍五入到指定小数位数
            /// </summary>
            public T Round(int digits)
            {
                var factor = GetPowerOfTen<T>(digits);
                return T.Round(value * factor) / factor;
            }

            /// <summary>
            /// 泛型 Round 方法 - 使用指定的舍入规则
            /// </summary>
            public T Round(MidpointRounding mode)
            {
                return T.Round(value, mode);
            }

            /// <summary>
            /// 泛型 Ceiling 方法 - 向上取整
            /// </summary>
            public T Ceiling()
            {
                return T.Ceiling(value);
            }

            /// <summary>
            /// 泛型 Floor 方法 - 向下取整
            /// </summary>
            public T Floor()
            {
                return T.Floor(value);
            }

            /// <summary>
            /// 泛型 Truncate 方法 - 截断小数部分
            /// </summary>
            public T Truncate()
            {
                return T.Truncate(value);
            }
        }

        extension<T>(T value) where T : INumber<T>
        {
            /// <summary>
            /// 泛型 Abs 方法 - 绝对值
            /// </summary>
            public T Abs()
            {
                return T.Abs(value);
            }

            /// <summary>
            /// 泛型 Sign 方法 - 符号
            /// </summary>
            public int Sign()
            {
                return T.Sign(value);
            }

            /// <summary>
            /// 泛型 Min 方法 - 最小值
            /// </summary>
            public T Min(T other)
            {
                return value < other ? value : other;
            }

            /// <summary>
            /// 泛型 Max 方法 - 最大值
            /// </summary>
            public T Max(T other)
            {
                return value > other ? value : other;
            }

            /// <summary>
            /// 泛型 Clamp 方法 - 将值限制在指定范围内
            /// </summary>
            public T Clamp(T min, T max)
            {
                if (min > max)
                    throw new ArgumentException("min should be less than or equal to max");

                return value < min ? min : value > max ? max : value;
            }
        }

        extension<T>(T value) where T : IPowerFunctions<T>
        {
            /// <summary>
            /// 泛型 Pow 方法 - 幂运算
            /// </summary>
            public T Pow(T exponent)
            {
                return T.Pow(value, exponent);
            }
        }

        extension<T>(T value) where T : IRootFunctions<T>
        {
            /// <summary>
            /// 泛型 Sqrt 方法 - 平方根
            /// </summary>
            public T Sqrt()
            {
                return T.Sqrt(value);
            }
        }

        extension<T>(T value) where T : ILogarithmicFunctions<T>
        {
            /// <summary>
            /// 泛型 Log 方法 - 自然对数
            /// </summary>
            public T Log()
            {
                return T.Log(value);
            }

            /// <summary>
            /// 泛型 Log 方法 - 指定底数的对数
            /// </summary>
            public T Log(T baseValue)
            {
                return T.Log(value, baseValue);
            }
        }

        extension<T>(T value) where T : ITrigonometricFunctions<T>
        {
            /// <summary>
            /// 泛型 Sin 方法 - 正弦
            /// </summary>
            public T Sin()
            {
                return T.Sin(value);
            }

            /// <summary>
            /// 泛型 Cos 方法 - 余弦
            /// </summary>
            public T Cos()
            {
                return T.Cos(value);
            }

            /// <summary>
            /// 泛型 Tan 方法 - 正切
            /// </summary>
            public T Tan()
            {
                return T.Tan(value);
            }
        }

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
    }
}