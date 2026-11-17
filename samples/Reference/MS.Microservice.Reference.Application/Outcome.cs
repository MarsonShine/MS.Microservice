using MS.Microservice.Core.Functional;
using static MS.Microservice.Core.Functional.F;

namespace MS.Microservice.Reference.Application;

// Success types must not overlap Error; the union matches cases by runtime type.
internal union Outcome<T>(Error, T) where T : class;

internal static class OutcomeExtensions
{
    internal static Either<Error, T> ToEither<T>(this Outcome<T> result, string emptyMessage) where T : class
        => result switch
        {
            Error error => Left(error),
            T value => Right(value),
            null => throw new InvalidOperationException(emptyMessage)
        };
}
