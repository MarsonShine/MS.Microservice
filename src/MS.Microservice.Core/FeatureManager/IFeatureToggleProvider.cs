namespace MS.Microservice.Core.FeatureManager
{
	/// <summary>
	/// 定义功能开关提供者的接口。
	/// </summary>
	public interface IFeatureToggleProvider
	{
		/// <summary>
		/// 检查指定的功能开关是否启用。
		/// </summary>
		/// <param name="featureName">功能开关的名称。</param>
		/// <returns>如果启用，返回 <c>true</c>；否则，返回 <c>false</c>。</returns>
		bool IsFeatureEnabled(string featureName);
	}
}
