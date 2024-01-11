namespace MS.Microservice.Domain.Exception
{
    public class ExceptionHelper
    {
        public static void ThrowDomainException(string message)
        {
            throw new DomainException(message);
        }
    }
}
