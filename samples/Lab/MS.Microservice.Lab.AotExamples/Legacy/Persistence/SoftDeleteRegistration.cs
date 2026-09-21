using System.Linq.Expressions;
using System.Reflection;
using MS.Microservice.Lab.AotExamples.Static.Persistence;

namespace MS.Microservice.Lab.AotExamples.Legacy.Persistence;

public static class SoftDeleteRegistration
{
    public static LambdaExpression Filter(Type modelType) => (LambdaExpression)typeof(SoftDeleteRegistration)
        .GetMethod(nameof(CreateFilter), BindingFlags.NonPublic | BindingFlags.Static)!
        .MakeGenericMethod(modelType).Invoke(null, null)!;

    private static Expression<Func<T, bool>> CreateFilter<T>() where T : ISoftDeleteExample => row => row.DeletedAt == null;
}
