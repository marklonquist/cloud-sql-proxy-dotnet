using CloudSql.Connector.Internal;
using Xunit;

namespace CloudSql.Connector.Tests;

public class PortAssignerTests
{
    [Fact]
    public void NoGlobalPort_UsesEngineDefaultsAndIncrementsPerEngine()
    {
        var assigner = new PortAssigner(0);

        Assert.False(assigner.UsesGlobalPort);
        Assert.Equal(5432, assigner.NextEnginePort("POSTGRES_15"));
        Assert.Equal(5433, assigner.NextEnginePort("POSTGRES_15"));
        Assert.Equal(3306, assigner.NextEnginePort("MYSQL_8_0"));
        Assert.Equal(3307, assigner.NextEnginePort("MYSQL_8_0"));
        Assert.Equal(1433, assigner.NextEnginePort("SQLSERVER_2019_STANDARD"));
    }

    [Fact]
    public void GlobalPort_IncrementsAcrossInstances()
    {
        var assigner = new PortAssigner(6000);

        Assert.True(assigner.UsesGlobalPort);
        Assert.Equal(6000, assigner.NextGlobalPort());
        Assert.Equal(6001, assigner.NextGlobalPort());
        Assert.Equal(6002, assigner.NextGlobalPort());
    }

    [Fact]
    public void UnknownEngine_YieldsEphemeralPort()
    {
        var assigner = new PortAssigner(0);

        Assert.Equal(0, assigner.NextEnginePort("SOMETHING_ELSE"));
    }
}
