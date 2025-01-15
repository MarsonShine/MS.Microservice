using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Core.FeatureManager.Internals;

namespace MS.Microservice.Core.FeatureManager
{
	public static class ServiceCollectionExtensions
	{
		/// <summary>
		/// 注册功能开关相关的服务。
		/// </summary>
		/// <param name="services">服务集合。</param>
		/// <param name="configuration">应用程序的配置。</param>
		/// <returns>服务集合。</returns>
		public static IServiceCollection AddFeatureToggle(this IServiceCollection services, IConfiguration configuration)
		{
			// 注册功能开关提供者
			services.AddSingleton<IFeatureToggleProvider, ConfigurationFeatureToggleProvider>();
			// 注册功能开关管理器
			services.AddSingleton<FeatureToggleManager>();

			return services;
		}
	}
}
