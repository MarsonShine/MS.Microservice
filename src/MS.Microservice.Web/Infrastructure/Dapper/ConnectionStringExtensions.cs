using Dapper;
using MS.Microservice.Core.Functional;
using MySqlConnector;

namespace MS.Microservice.Web.Infrastructure.Dapper
{
    public static partial class ConnectionStringExtensions
    {
        extension(ConnectionString connectionString)
        {
            public MySqlConnection CreateConnection() => new(connectionString);

            public Func<Func<MySqlConnection, CancellationToken, Task<TResult>>, Func<CancellationToken, Task<TResult>>> WithConnection<TResult>()
                => work => async cancellationToken =>
                {
                    await using var connection = connectionString.CreateConnection();
                    await connection.OpenAsync(cancellationToken);
                    return await work(connection, cancellationToken);
                };

            public Func<string, Func<object?, Func<CancellationToken, Task<IEnumerable<TResult>>>>> QueryAsync<TResult>()
                => new Func<string, object?, CancellationToken, Task<IEnumerable<TResult>>>(async (sql, parameters, cancellationToken) =>
                    await connectionString
                        .WithConnection<IEnumerable<TResult>>()
                        ((connection, token) => connection.QueryAsync<TResult>(new CommandDefinition(sql, parameters, cancellationToken: token)))
                        (cancellationToken))
                    .Curry();

            public Func<string, Func<object?, Func<CancellationToken, Task<TResult>>>> ExecuteScalarAsync<TResult>()
                where TResult : notnull
                => new Func<string, object?, CancellationToken, Task<TResult>>(async (sql, parameters, cancellationToken) =>
                {
                    var result = await connectionString
                        .WithConnection<TResult?>()
                        ((connection, token) => connection.ExecuteScalarAsync<TResult>(new CommandDefinition(sql, parameters, cancellationToken: token)))
                        (cancellationToken);

                    return result ?? throw new InvalidOperationException("ExecuteScalarAsync returned null.");
                })
                    .Curry();
        }
    }
}
