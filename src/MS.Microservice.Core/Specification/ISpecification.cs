using System;
using System.Linq.Expressions;

namespace MS.Microservice.Core.Specification
{
    /// <summary>
    /// 规格模式，封装查询条件
    /// 具体详见：https://www.codeproject.com/Articles/670115/Specification-pattern-in-Csharp
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface ISpecification<T>
    {
        bool IsSatisfiedBy(T obj);

        Expression<Func<T, bool>> ToExpression();
    }

    public interface ISpecification<T, TResult> : ISpecification<T>
    {
        Expression<Func<T, TResult>> Selector { get; }
    }
}
