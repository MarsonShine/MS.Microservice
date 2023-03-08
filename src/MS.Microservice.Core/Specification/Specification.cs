using System;
using System.Linq.Expressions;

namespace MS.Microservice.Core.Specification
{
    public abstract class Specification<T> : ISpecification<T>
    {
        public virtual bool IsSatisfiedBy(T obj)
        {
            return ToExpression().Compile()(obj);
        }

        public abstract Expression<Func<T, bool>> ToExpression();

        //隐式转换
        public static implicit operator Expression<Func<T, bool>>(Specification<T> specification)
        {
            return specification.ToExpression();
        }
    }
}
