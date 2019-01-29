using MediatR;
using Microsoft.AspNetCore.Mvc;
using MS.Microservice.Web.Apps.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MS.Microservice.Web.Controllers
{
    [Produces("application/json")]
    [Route("api/[controller]")]
    [ApiController]
    public class OrderController : ControllerBase
    {
        private readonly IMediator _mediator;
        public OrderController(IMediator mediator)
        {
            _mediator = mediator;
        }
        [Route("Create")]
        public async Task<string> Create([FromBody]CreateOrderCmd createOrder)
        {
            await _mediator.Send(createOrder);
            return "marson shine";
        }
    }
}
