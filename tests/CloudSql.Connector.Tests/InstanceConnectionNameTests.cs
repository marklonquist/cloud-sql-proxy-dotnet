using CloudSql.Connector;
using Xunit;

namespace CloudSql.Connector.Tests;

public class InstanceConnectionNameTests
{
    [Theory]
    [InlineData("my-project:europe-west1:my-db", "my-project", "europe-west1", "my-db")]
    [InlineData("p:us-central1:i", "p", "us-central1", "i")]
    public void Parse_ValidName_SplitsParts(string value, string project, string region, string instance)
    {
        var icn = InstanceConnectionName.Parse(value);

        Assert.Equal(project, icn.Project);
        Assert.Equal(region, icn.Region);
        Assert.Equal(instance, icn.Instance);
        Assert.Equal(value, icn.Original);
    }

    [Fact]
    public void Parse_DomainScopedProject_KeepsColonInProject()
    {
        // Domain-scoped projects ("domain.com:project") contain a colon; only the last two
        // segments are region and instance.
        var icn = InstanceConnectionName.Parse("example.com:my-project:europe-west1:my-db");

        Assert.Equal("example.com:my-project", icn.Project);
        Assert.Equal("europe-west1", icn.Region);
        Assert.Equal("my-db", icn.Instance);
    }

    [Fact]
    public void ServerCertCommonName_OmitsRegion()
    {
        var icn = InstanceConnectionName.Parse("my-project:europe-west1:my-db");

        Assert.Equal("my-project:my-db", icn.ServerCertCommonName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("only-one")]
    [InlineData("project:region")]
    [InlineData("project:region:")]
    [InlineData(":region:instance")]
    [InlineData("project::instance")]
    public void TryParse_InvalidName_ReturnsFalse(string value)
    {
        Assert.False(InstanceConnectionName.TryParse(value, out var result));
        Assert.Null(result);
    }

    [Fact]
    public void Parse_InvalidName_Throws()
    {
        Assert.Throws<FormatException>(() => InstanceConnectionName.Parse("not-valid"));
    }
}
