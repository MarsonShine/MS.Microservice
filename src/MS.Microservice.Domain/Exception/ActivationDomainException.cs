using MS.Microservice.Core;

namespace MS.Microservice.Domain.Exception
{
    public class ActivationDomainException : MsPlatformException
    {
        public ActivationDomainException()
        { }

        public ActivationDomainException(string message)
            : base(message)
        { }

        public ActivationDomainException(string message, System.Exception innerException)
            : base(message, innerException)
        { }

        public ActivationDomainException(int code, string message) : base(code, message)
        {
        }
    }
}
