using System;
using System.Linq;
using System.Linq.Expressions;

namespace MS.Microservice.Core.Specification
{
    public class AndSpecification<T> : CompositeSpecification<T>
    {
        public AndSpecification(ISpecification<T> left, ISpecification<T> right) : base(left, right)
        {
        }

        public override Expression<Func<T, bool>> ToExpression()
        {
            return Left.ToExpression().And(Right.ToExpression());
        }
    }
}
