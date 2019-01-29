using AutoMapper;
using MS.Microservice.Domain;
using MS.Microservice.Web.Apps.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MS.Microservice.Web.AutoMappers.Profiles
{
    public class OrderAutoMapperProfiles : Profile
    {
        public OrderAutoMapperProfiles()
        {
            CreateMap<CreateOrderCmd, Order>();
        }
    }
}
