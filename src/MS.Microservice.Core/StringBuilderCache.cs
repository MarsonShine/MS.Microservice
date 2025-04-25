using System;
using System.Text;

namespace MS.Microservice.Core
{
    /// <summary>
    /// https://referencesource.microsoft.com/#mscorlib/system/text/stringbuildercache.cs
    /// </summary>
    public class StringBuilderCache
    {
        private const int MAX_BUILDER_SIZE = 360;
        private const int DefaultCapacity = 16;
        [ThreadStatic]
        private static StringBuilder? CachedInstance;
        public static StringBuilder Acquire(int capacity = DefaultCapacity)
        {
            if (capacity <= MAX_BUILDER_SIZE)
            {
                StringBuilder? sb = CachedInstance;
                if (sb != null)
                {
                    // Avoid stringbuilder block fragmentation by getting a new StringBuilder
                    // when the requested size is larger than the current capacity
                    if (capacity <= sb.Capacity)
                    {
                        CachedInstance = null;
                        sb.Clear();
                        return sb;
                    }
                }
            }
            return new StringBuilder(capacity);
        }
        public static void Release(StringBuilder sb)
        {
            if (sb.Capacity <= MAX_BUILDER_SIZE)
            {
                CachedInstance = sb;
            }
        }

        public static string GetStringAndRelease(StringBuilder sb)
        {
            string result = sb.ToString();
            Release(sb);
            return result;
        }
    }
}
