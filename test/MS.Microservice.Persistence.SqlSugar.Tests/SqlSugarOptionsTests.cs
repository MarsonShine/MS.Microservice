namespace MS.Microservice.Persistence.SqlSugar.Tests;

public class SqlSugarOptionsTests
{
    [Fact]
    public void Defaults_ShouldHavePrintLogDisabled()
    {
        var options = new SqlSugarOptions();

        options.PrintLog.Should().BeFalse();
    }

    [Fact]
    public void Defaults_ShouldHaveAutoCloseConnectionDisabled()
    {
        var options = new SqlSugarOptions();

        options.IsAutoCloseConnection.Should().BeFalse();
    }
}

public class SqlSugarClientBuilderOptionsTests
{
    [Fact]
    public void Defaults_ShouldHaveEmptyConnectionString()
    {
        var options = new SqlSugarClientBuilderOptions();

        options.ConnectionString.Should().BeEmpty();
    }
}