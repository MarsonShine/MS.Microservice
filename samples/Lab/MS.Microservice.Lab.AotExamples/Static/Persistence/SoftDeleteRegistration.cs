using System.Linq.Expressions;

namespace MS.Microservice.Lab.AotExamples.Static.Persistence;

public interface ISoftDeleteExample { DateTime? DeletedAt { get; } }

public static class SoftDeleteRegistration
{
    // Closed generic expression construction; EF receives this expression without Compile().
    public static Expression<Func<T, bool>> Filter<T>() where T : ISoftDeleteExample => row => row.DeletedAt == null;
}
