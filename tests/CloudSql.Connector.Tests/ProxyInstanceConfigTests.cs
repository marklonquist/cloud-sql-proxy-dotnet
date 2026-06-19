using CloudSql.Connector.Internal;
using Xunit;

namespace CloudSql.Connector.Tests;

public class ProxyInstanceConfigTests
{
    [Fact]
    public void Parse_BareName_HasNoOverrides()
    {
        var config = ProxyInstanceConfig.Parse("proj:europe-west1:db");

        Assert.Equal("proj:europe-west1:db", config.Instance.Original);
        Assert.Null(config.Port);
        Assert.Null(config.Address);
        Assert.Null(config.UnixSocketDir);
        Assert.Null(config.UnixSocketPath);
    }

    [Fact]
    public void Parse_AllQueryParams_AreExtracted()
    {
        var config = ProxyInstanceConfig.Parse(
            "proj:europe-west1:db?port=6000&address=0.0.0.0&unix-socket=/sockets&unix-socket-path=/sockets/db");

        Assert.Equal("proj:europe-west1:db", config.Instance.Original);
        Assert.Equal(6000, config.Port);
        Assert.Equal("0.0.0.0", config.Address);
        Assert.Equal("/sockets", config.UnixSocketDir);
        Assert.Equal("/sockets/db", config.UnixSocketPath);
    }

    [Fact]
    public void Parse_DomainScopedProjectWithPort_SplitsCorrectly()
    {
        var config = ProxyInstanceConfig.Parse("example.com:proj:europe-west1:db?port=5432");

        Assert.Equal("example.com:proj", config.Instance.Project);
        Assert.Equal("db", config.Instance.Instance);
        Assert.Equal(5432, config.Port);
    }

    [Theory]
    [InlineData("proj:europe-west1:db?port=0")]
    [InlineData("proj:europe-west1:db?port=70000")]
    [InlineData("proj:europe-west1:db?port=abc")]
    public void Parse_InvalidPort_Throws(string entry)
    {
        Assert.Throws<FormatException>(() => ProxyInstanceConfig.Parse(entry));
    }

    [Fact]
    public void Parse_UnknownQueryParam_Throws()
    {
        Assert.Throws<FormatException>(
            () => ProxyInstanceConfig.Parse("proj:europe-west1:db?bogus=1"));
    }
}
