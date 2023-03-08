namespace MS.Microservice.Core.Specification
{
    public interface ISingleResultSpecification
    {
    }

    public interface ISingleResultSpecification<T> : ISpecification<T>, ISingleResultSpecification { }
}
