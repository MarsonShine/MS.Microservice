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
        /// <returns>从资源起点读取的流，由调用方释放。</returns>
        /// <exception cref="ArgumentException"></exception>
        public static ValueTask<Stream> EmbeddedResourceReadAsync(string path)
        {
            try
            {
                var assembly = Assembly.GetEntryAssembly()!;
                var resourceName = $"{assembly.GetName().Name}.{path}";
                var stream = assembly.GetManifestResourceStream(resourceName)
                    ?? throw new ArgumentException($"Resource '{resourceName}' not found.");
                return ValueTask.FromResult(stream);
            }
            catch (Exception error)
            {
                return ValueTask.FromException<Stream>(error);
            }
        }
    }
}
