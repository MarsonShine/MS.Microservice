using MS.Microservice.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace MS.Microservice
{
    public static class Check
    {
        public static T NotNull<T>(
               T value,
               string parameterName)
        {
            if (value == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            return value;
        }

        public static T NotNull<T>(
            T value,
            [NotNull] string parameterName,
            string message)
        {
            if (value == null)
            {
                throw new ArgumentNullException(parameterName, message);
            }

            return value;
        }

        public static string NotNull(
            string value,
            [NotNull] string parameterName,
            int maxLength = int.MaxValue,
            int minLength = 0)
        {
            if (value == null)
            {
                throw new ArgumentException($"{parameterName} can not be null!", parameterName);
            }

            if (value.Length > maxLength)
            {
                throw new ArgumentException($"{parameterName} length must be equal to or lower than {maxLength}!", parameterName);
            }

            if (minLength > 0 && value.Length < minLength)
            {
                throw new ArgumentException($"{parameterName} length must be equal to or bigger than {minLength}!", parameterName);
            }

            return value;
        }

        public static ICollection<T> NotNullOrEmpty<T>(ICollection<T> value, [NotNull] string parameterName)
        {
            if (value.IsNullOrEmpty())
            {
                throw new ArgumentException(parameterName + " can not be null or empty!", parameterName);
            }

            return value!;
        }
    }
}