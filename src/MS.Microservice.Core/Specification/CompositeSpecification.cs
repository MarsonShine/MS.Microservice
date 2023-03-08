namespace MS.Microservice.Core.Specification
{
    public abstract class CompositeSpecification<T> : Specification<T>
    {
        public ISpecification<T> Left { get; }
        public ISpecification<T> Right { get; }
        protected CompositeSpecification(ISpecification<T> left, ISpecification<T> right)
        {
            Left = left;
            Right = right;
        }
    }
}
