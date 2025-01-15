using Microsoft.AspNetCore.Mvc;
using MS.Microservice.Core.FeatureManager;

namespace MS.Microservice.Web.Controller
{
	[Route("api/[controller]")]
	[ApiController]
	public class FeatureManagerController(FeatureToggleManager featureToggleManager) : ControllerBase
	{
		private readonly FeatureToggleManager _featureToggleManager = featureToggleManager;

		/// <summary>
		/// 测试特性开关
		/// </summary>
		/// <returns></returns>
		[HttpGet("action-method")]
		public IActionResult ActionMethod()
		{
			if (_featureToggleManager.IsEnabled("EnableActionMethod"))
			{
				return Ok("ActionMethod is enabled and executed.");
			}
			else
			{
				return Forbid();
			}
		}
		/// <summary>
		/// 测试特性开关
		/// </summary>
		/// <returns></returns>
		[HttpGet("new-feature")]
		[FeatureToggle("EnableActionMethod")]
		public IActionResult NewFeature()
		{
			return Ok("NewFeature is enabled and executed.");
		}
	}
}
