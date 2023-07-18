using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace System.Linq
{
    public class ExpressionStarter<T>
    {
        public ExpressionStarter() : this(false)
        {
        }

        public ExpressionStarter(bool defaultExpression)
        {
            if (defaultExpression)
            {
                DefaultExpression = f => true;
            }
            else
            {
                DefaultExpression = f => false;
            }
        }

        public ExpressionStarter(Expression<Func<T, bool>> exp) : this(false)
        {
            _predicate = exp;
        }

        /// <summary>The actual Predicate. It can only be set by calling Start.</summary>
        private Expression<Func<T, bool>> Predicate =>
            (IsStarted || !UseDefaultExpression) ? _predicate : DefaultExpression;
        [AllowNull]
        private Expression<Func<T, bool>> _predicate;

        /// <summary>Determines if the predicate is started.</summary>
        public bool IsStarted => _predicate != null;

        /// <summary> A default expression to use only when the expression is null </summary>
        public bool UseDefaultExpression => DefaultExpression != null;

        /// <summary>The default expression</summary>
        public Expression<Func<T, bool>> DefaultExpression { get; set; }

        /// <summary>Set the Expression predicate</summary>
        /// <param name="exp">The first expression</param>
        public Expression<Func<T, bool>> Start(Expression<Func<T, bool>> exp)
        {
            if (IsStarted)
            {
                throw new Exception("Predicate cannot be started again.");
            }

            return _predicate = exp;
        }

        /// <summary>Or</summary>
        public Expression<Func<T, bool>> Or([NotNull] Expression<Func<T, bool>> expr2)
        {
            return (IsStarted) ? _predicate = Predicate.Or(expr2) : Start(expr2);
        }

        /// <summary>And</summary>
        public Expression<Func<T, bool>> And([NotNull] Expression<Func<T, bool>> expr2)
        {
            return (IsStarted) ? _predicate = Predicate.And(expr2) : Start(expr2);
        }

        /// <summary> Show predicate string </summary>
        public override string ToString()
        {
            return Predicate?.ToString()!;
        }

        #region Implicit Operators

        /// <summary>
        /// Allows this object to be implicitely converted to an Expression{Func{T, bool}}.
        /// </summary>
        /// <param name="right"></param>
        public static implicit operator Expression<Func<T, bool>>(ExpressionStarter<T> right)
        {
            return right?.Predicate!;
        }

        /// <summary>
        /// Allows this object to be implicitely converted to an Expression{Func{T, bool}}.
        /// </summary>
        /// <param name="right"></param>
        public static implicit operator Func<T, bool>([AllowNull]ExpressionStarter<T> right)
        {
            return right == null! ? null! :
                (right.IsStarted || right.UseDefaultExpression) ? right.Predicate.Compile() : null!;
        }

        /// <summary>
        /// Allows this object to be implicitely converted to an Expression{Func{T, bool}}.
        /// </summary>
        /// <param name="right"></param>
        public static implicit operator ExpressionStarter<T>([AllowNull]Expression<Func<T, bool>> right)
        {
            return right == null! ? null! : new ExpressionStarter<T>(right);
        }

        #endregion

        #region Implement Expression<TDelagate> methods and properties

#if !(NET35)

        /// <summary></summary>
        public Func<T, bool> Compile()
        {
            return Predicate.Compile();
        }
#endif

#if !(NET35 || WINDOWS_APP || NETSTANDARD || PORTABLE || PORTABLE40 || UAP)
        /// <summary></summary>
        public Func<T, bool> Compile(DebugInfoGenerator debugInfoGenerator) { return Predicate.Compile(debugInfoGenerator); }

        /// <summary></summary>
        public Expression<Func<T, bool>> Update(Expression body, IEnumerable<ParameterExpression> parameters) { return Predicate.Update(body, parameters)!; }
#endif

        #endregion

        #region Implement LamdaExpression methods and properties

        /// <summary></summary>
        public Expression Body => Predicate.Body;


        /// <summary></summary>
        public ExpressionType NodeType => Predicate.NodeType;

        /// <summary></summary>
        public ReadOnlyCollection<ParameterExpression> Parameters => Predicate.Parameters;

        /// <summary></summary>
        public Type Type => Predicate.Type;

#if !(NET35)
        /// <summary></summary>
        public string Name => Predicate.Name!;

        /// <summary></summary>
        public Type ReturnType => Predicate.ReturnType;

        /// <summary></summary>
        public bool TailCall => Predicate.TailCall;
#endif

        #endregion

        #region Implement Expression methods and properties

#if !(NET35)
        /// <summary></summary>
        public virtual bool CanReduce => Predicate.CanReduce;
#endif

        #endregion
    }
}
