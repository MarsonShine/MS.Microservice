using System.Diagnostics.CodeAnalysis;

namespace MS.Microservice.Core.Specification
{
    public static class SpecificationExtensions
    {
        public static ISpecification<T> And<T>([NotNull] this ISpecification<T> specification,
            [NotNull] ISpecification<T> other)
        {
            Check.NotNull(specification, nameof(specification));
            Check.NotNull(other, nameof(other));

            return new AndSpecification<T>(specification, other);
        }

        public static ISpecification<T> Or<T>([NotNull] this ISpecification<T> specification,
            [NotNull] ISpecification<T> other)
        {
            Check.NotNull(specification, nameof(specification));
            Check.NotNull(other, nameof(other));

            return new OrSpecification<T>(specification, other);
        }

        public static ISpecification<T> AndNot<T>([NotNull] this ISpecification<T> specification,
            [NotNull] ISpecification<T> other)
        {
            Check.NotNull(specification, nameof(specification));
            Check.NotNull(other, nameof(other));

            return new AndNotSpecification<T>(specification, other);
        }

        public static ISpecification<T> Not<T>([NotNull] this ISpecification<T> specification)
        {
            Check.NotNull(specification, nameof(specification));

            return new NotSpecification<T>(specification);
        }

    }
}
