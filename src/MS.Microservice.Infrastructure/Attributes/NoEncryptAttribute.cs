using System;

namespace MS.Microservice.Infrastructure.Attributes
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public class NoEncryptAttribute : Attribute
    {
    }
}
