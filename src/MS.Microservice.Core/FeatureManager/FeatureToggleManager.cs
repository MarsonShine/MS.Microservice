namespace MS.Microservice.Core.FeatureManager
{
	/// <summary>
	/// 管理功能开关的状态。
	/// </summary>
	/// <remarks>
	/// 初始化 <see cref="FeatureToggleManager"/> 实例。
	/// </remarks>
	/// <param name="featureToggleProvider">功能开关提供者。</param>
	public class FeatureToggleManager(IFeatureToggleProvider featureToggleProvider)
	{
		private readonly IFeatureToggleProvider _featureToggleProvider = featureToggleProvider;

		/// <summary>
		/// 检查指定的功能开关是否启用。
		/// </summary>
		/// <param name="featureName">功能开关的名称。</param>
		/// <returns>如果启用，返回 <c>true</c>；否则，返回 <c>false</c>。</returns>
		public bool IsEnabled(string featureName)
		{
			return _featureToggleProvider.IsFeatureEnabled(featureName);
		}
	}
}
