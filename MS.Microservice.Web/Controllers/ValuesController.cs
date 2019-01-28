using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MS.Microservice.Web.ApplicationServices;

namespace MS.Microservice.Web.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ValuesController : ControllerBase
    {
        private readonly IOrderService _orderService;

        public ValuesController(IOrderService orderService) {
            _orderService = orderService;
        }
        public async Task<string> Get()
        {
            return await Task.FromResult("marson shine");
        }
    }
}