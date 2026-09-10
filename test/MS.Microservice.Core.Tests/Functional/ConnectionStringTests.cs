using MS.Microservice.Core.Functional;
using MS.Microservice.Lab.Infrastructure.Dapper;
using Npgsql;

namespace MS.Microservice.Core.Tests.Functional
{
    public class ConnectionStringTests
    {
        [Fact]
        public void ImplicitConversion_FromString_RoundTripsValue()
        {
            ConnectionString connectionString = "Host=localhost;Port=5432;Database=test;Username=postgres;Password=secret";

            string value = connectionString;

            Assert.Equal("Host=localhost;Port=5432;Database=test;Username=postgres;Password=secret", value);
        }

        [Fact]
        public void CreateConnection_WhenCalled_UsesWrappedValue()
        {
            ConnectionString connectionString = "Host=localhost;Port=5432;Database=test;Username=postgres;Password=secret";

            using NpgsqlConnection connection = connectionString.CreateConnection();

            Assert.Equal(connectionString.Value, connection.ConnectionString);
        }

        [Fact]
        public void QueryAsync_WhenAppliedProgressively_ReturnsDeferredOperation()
        {
            ConnectionString connectionString = "Host=localhost;Port=5432;Database=test;Username=postgres;Password=secret";

            var operation = connectionString
                .QueryAsync<int>()
                .Apply("select 1")
                .Apply(null);

            Assert.NotNull(operation);
        }
    }
}
