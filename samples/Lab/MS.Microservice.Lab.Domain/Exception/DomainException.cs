using MS.Microservice.Core;

namespace MS.Microservice.Domain.Exception
{
    public class DomainException : CorePlatformException
    {
        public DomainException()
        { }

        public DomainException(string message)
            : base(message)
        { }

        public DomainException(string message, System.Exception innerException)
            : base(message, innerException)
        { }

        public DomainException(int code, string message) : base(code, message)
        {
        }
    }
}
