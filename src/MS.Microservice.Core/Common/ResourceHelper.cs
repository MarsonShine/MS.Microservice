using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System;

namespace MS.Microservice.Core.Common
{
    /// <summary>
    /// 资源操作帮助类
    /// </summary>
    public class ResourceHelper
    {
        /// <summary>
        /// 获取嵌入的资源
        /// </summary>
        /// <param name="path">用点运算符表示目录层级，如：PathA.PathB（等同于 PathA/PathB）</param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public static async ValueTask<Stream> EmbeddedResourceReadAsync(string path)
        {
            Assembly assembly = Assembly.GetEntryAssembly()!;
            var info = assembly.GetName();
            var resourceName = $"{info.Name}.{path}";
            using var resourceStream = assembly
                .GetManifestResourceStream(resourceName);
            if (resourceStream == null)
            {
                throw new ArgumentException($"Resource '{resourceName}' not found.");
            }
            using MemoryStream memoryStream = new();
            await resourceStream.CopyToAsync(memoryStream);

            return memoryStream;
        }
    }
}
