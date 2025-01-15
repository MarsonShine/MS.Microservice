using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc;
using System;
using Microsoft.Extensions.DependencyInjection;

namespace MS.Microservice.Core.FeatureManager
{
	/// <summary>
	/// 用于标记需要功能开关控制的方法或类的特性。
	/// </summary>
	/// <remarks>
	/// 初始化 <see cref="FeatureToggleAttribute"/> 实例。
	/// </remarks>
	/// <param name="featureName">功能开关的名称。</param>
	[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
	public class FeatureToggleAttribute(string featureName) : Attribute, IAuthorizationFilter
	{
		private readonly string _featureName = featureName;

		/// <inheritdoc />
		public void OnAuthorization(AuthorizationFilterContext context)
		{
			var featureToggleManager = context.HttpContext.RequestServices.GetRequiredService<FeatureToggleManager>();

			if (featureToggleManager == null || !featureToggleManager.IsEnabled(_featureName))
			{
				context.Result = new ForbidResult();
			}
		}
	}
}
