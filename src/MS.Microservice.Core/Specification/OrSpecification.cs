using System;
using System.Linq;
using System.Linq.Expressions;

namespace MS.Microservice.Core.Specification
{
    public class OrSpecification<T> : CompositeSpecification<T>
    {
        public OrSpecification(ISpecification<T> left, ISpecification<T> right) : base(left, right)
        {
        }

        public override Expression<Func<T, bool>> ToExpression()
        {
            return Left.ToExpression().Or(Right.ToExpression());
        }
    }
}