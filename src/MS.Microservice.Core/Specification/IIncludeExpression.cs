namespace MS.Microservice.Core.Specification;

/// <summary>
/// Include 表达式抽象 - 支持访问者模式
/// </summary>
public interface IIncludeExpression
{
    /// <summary>
    /// 接受访问者处理
    /// </summary>
    TQuery Accept<TQuery, TEntity>(IIncludeExpressionVisitor<TQuery, TEntity> visitor, TQuery query);

    /// <summary>
    /// 用于调试的表达式字符串
    /// </summary>
    string ExpressionString { get; }
}
