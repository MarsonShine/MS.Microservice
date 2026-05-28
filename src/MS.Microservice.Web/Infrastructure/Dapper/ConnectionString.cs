namespace MS.Microservice.Web.Infrastructure.Dapper
{
    public sealed class ConnectionString : IEquatable<ConnectionString>
    {
        public string Value { get; }

        public ConnectionString(string value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(value));
            Value = value;
        }

        public static implicit operator string(ConnectionString connectionString) => connectionString.Value;

        public static implicit operator ConnectionString(string value) => new(value);

        public bool Equals(ConnectionString? other)
            => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is ConnectionString other && Equals(other);

        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

        public override string ToString() => Value;
    }
}
