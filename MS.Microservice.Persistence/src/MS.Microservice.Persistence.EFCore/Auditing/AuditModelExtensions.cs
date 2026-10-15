using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MS.Microservice.Persistence.EFCore.Auditing;

public static class AuditModelExtensions
{
    internal const string FieldsAnnotation = "MS:Audit:Fields";
    internal const string ResourceAnnotation = "MS:Audit:Resource";

    /// <summary>Opts mapped scalar properties into automatic auditing; expressions are read during model construction.</summary>
    public static EntityTypeBuilder<TEntity> AuditChanges<TEntity>(this EntityTypeBuilder<TEntity> entity,
        string resourceType, Expression<Func<TEntity, object?>> fields) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceType);
        if (resourceType.Length > 96 || resourceType.Any(value => !char.IsAsciiLetterOrDigit(value) && value is not '.' and not '-' and not '_'))
            throw new ArgumentException("Use at most 96 ASCII letters, digits, dots, hyphens or underscores.", nameof(resourceType));
        Expression body = fields.Body;
        if (body is UnaryExpression conversion) body = conversion.Operand;
        var expressions = body is NewExpression creation ? creation.Arguments : new[] { body }.AsEnumerable();
        var names = expressions.Select(expression =>
        {
            if (expression is UnaryExpression convert) expression = convert.Operand;
            if (expression is not MemberExpression member || member.Expression != fields.Parameters[0])
                throw new ArgumentException("Select direct scalar properties, for example x => new { x.Status, x.Amount }.", nameof(fields));
            return member.Member.Name;
        }).Distinct(StringComparer.Ordinal).ToArray();
        if (names.Length is < 1 or > 16) throw new ArgumentException("Select 1–16 audit properties.", nameof(fields));
        entity.HasAnnotation(FieldsAnnotation, string.Join('|', names));
        entity.HasAnnotation(ResourceAnnotation, resourceType);
        return entity;
    }
}

public sealed record AuditCollectionOptions
{
    public int MaximumRecordBytes { get; init; } = 8192;
    public int MaximumRecordsPerSave { get; init; } = 1000;
    public int MaximumBatchBytes { get; init; } = 1024 * 1024;
}
