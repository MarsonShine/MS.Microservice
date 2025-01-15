using Microsoft.Extensions.Configuration;

namespace MS.Microservice.Core.FeatureManager.Internals
{
	/// <summary>
	/// 从配置文件中读取功能开关的提供者。
	/// </summary>
	/// <remarks>
	/// 初始化 <see cref="ConfigurationFeatureToggleProvider"/> 实例。
	/// </remarks>
	/// <param name="configuration">应用程序的配置。</param>
	public class ConfigurationFeatureToggleProvider(IConfiguration configuration) : IFeatureToggleProvider
	{
		private readonly IConfiguration _configuration = configuration;

		/// <inheritdoc />
		public bool IsFeatureEnabled(string featureName)
		{
			return _configuration.GetValue<bool>($"FeatureToggles:{featureName}");
		}
	}
}
