using MS.Microservice.Core.Functional;
using MS.Microservice.Web.Infrastructure.Dapper;
using MySqlConnector;

namespace MS.Microservice.Core.Tests.Functional
{
    public class ConnectionStringTests
    {
        [Fact]
        public void ImplicitConversion_FromString_RoundTripsValue()
        {
            ConnectionString connectionString = "Server=localhost;Database=test;Uid=root;Pwd=secret;";

            string value = connectionString;

            Assert.Equal("Server=localhost;Database=test;Uid=root;Pwd=secret;", value);
        }

        [Fact]
        public void CreateConnection_WhenCalled_UsesWrappedValue()
        {
            ConnectionString connectionString = "Server=localhost;Database=test;Uid=root;Pwd=secret;";

            using MySqlConnection connection = connectionString.CreateConnection();

            Assert.Equal(connectionString.Value, connection.ConnectionString);
        }

        [Fact]
        public void QueryAsync_WhenAppliedProgressively_ReturnsDeferredOperation()
        {
            ConnectionString connectionString = "Server=localhost;Database=test;Uid=root;Pwd=secret;";

            var operation = connectionString
                .QueryAsync<int>()
                .Apply("select 1")
                .Apply(null);

            Assert.NotNull(operation);
        }
    }
}
