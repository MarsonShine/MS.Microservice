using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MS.Microservice.Core.Dto;

namespace MS.Microservice.Web.Controller {
    [Route("api/[controller]")]
    [ApiController]
    public class ImageController : ControllerBase {
        /// <summary>
        /// 上传图片
        /// </summary>
        /// <param name="file"></param>
        /// <returns></returns>
        [HttpPost("upload")]
        [ProducesResponseType(typeof(ResultDto<bool>), (int) HttpStatusCode.OK)]
        [ProducesResponseType((int) HttpStatusCode.BadRequest)]
        public async Task<IActionResult> Upload(IFormFile file) {
            if (file == null || file.Length == 0) {
                return Ok(new {
                    timestamp = DateTime.Now.Ticks,
                        status = 500,
                        url = ""
                });
            }
            // var buffer = new byte[stream.Length];
            // await stream.ReadAsync(buffer,0,buffer.Length);
            using var fs = System.IO.File.Create("../MS.Microservice.Web/file.jpg");
            await fs.CopyToAsync(file.OpenReadStream());
            await fs.FlushAsync();
            return Ok(new {
                timestamp = DateTime.Now.Ticks,
                    status = 200,
                    url = "http://tmp/" + file.FileName
            });
        }
    }
}