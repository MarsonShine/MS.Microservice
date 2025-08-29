using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MS.Microservice.Core.FeatureManager;

namespace MS.Microservice.Web.Controller
{
    [Route("api/[controller]")]
    [ApiController]
    public class FeatureManagerController(FeatureToggleManager featureToggleManager, ILogger<FeatureManagerController> logger) : ControllerBase
    {
        private readonly FeatureToggleManager _featureToggleManager = featureToggleManager;
        private readonly ILogger<FeatureManagerController> logger = logger;

        /// <summary>
        /// 测试特性开关
        /// </summary>
        /// <returns></returns>
        [HttpGet("action-method")]
        public IActionResult ActionMethod()
        {
            logger.LogInformation("Checking feature toggle 'EnableActionMethod'");
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
