using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MS.Microservice.Core
{
    /// <summary>
    /// https://github.com/AppMetrics/AppMetrics/blob/features/4.2.0/src/Core/src/App.Metrics.Abstractions/TimeUnitExtensions.cs
    /// </summary>
    public static class TimeUnitExtensions
    {
        private static readonly long[,] ConversionFactors = BuildConversionFactorsMatrix();

        public static long ToSeconds(this TimeUnit unit, long value) { return Convert(unit, TimeUnit.Seconds, value); }

        public static long Convert(this TimeUnit sourceUnit, TimeUnit targetUnit, long value)
        {
            if (sourceUnit == targetUnit)
            {
                return value;
            }

            return System.Convert.ToInt64(value * sourceUnit.ScalingFactorFor(targetUnit));
        }

        public static double ScalingFactorFor(this TimeUnit sourceUnit, TimeUnit targetUnit)
        {
            if (sourceUnit == targetUnit)
            {
                return 1.0;
            }

            var sourceIndex = (int)sourceUnit;
            var targetIndex = (int)targetUnit;

            if (sourceIndex < targetIndex)
            {
                return 1 / (double)ConversionFactors[targetIndex, sourceIndex];
            }

            return ConversionFactors[sourceIndex, targetIndex];
        }

        private static long[,] BuildConversionFactorsMatrix()
        {
            var count = Enum.GetValues(typeof(TimeUnit)).Length;

            var matrix = new long[count, count];
            var timingFactors = new[]
                                {
                                    1000L, // Nanoseconds to microseconds
                                    1000L, // Microseconds to milliseconds
                                    1000L, // Milliseconds to seconds
                                    60L, // Seconds to minutes
                                    60L, // Minutes to hours
                                    24L // Hours to days
                                };

            for (var source = 0; source < count; source++)
            {
                var cumulativeFactor = 1L;
                for (var target = source - 1; target >= 0; target--)
                {
                    cumulativeFactor *= timingFactors[target];
                    matrix[source, target] = cumulativeFactor;
                }
            }

            return matrix;
        }
    }


}
