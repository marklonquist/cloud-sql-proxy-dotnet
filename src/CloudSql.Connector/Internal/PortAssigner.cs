namespace CloudSql.Connector.Internal;

/// <summary>
/// Assigns local TCP ports to auto-started instances exactly as the Cloud SQL Auth Proxy binary
/// does. When a global starting port is configured, ports increment from it across all instances;
/// otherwise each database engine has its own counter seeded at the engine's default port and
/// incremented per instance of that engine. Per-instance explicit ports bypass this entirely.
/// </summary>
internal sealed class PortAssigner
{
    private readonly bool _globalConfigured;
    private int _global;
    private int _postgres = 5432;
    private int _mysql = 3306;
    private int _sqlserver = 1433;

    /// <param name="globalPort">
    /// The configured starting port (<see cref="ConnectorOptions.Port"/>); <c>0</c> means
    /// "use engine defaults".
    /// </param>
    public PortAssigner(int globalPort)
    {
        _globalConfigured = globalPort != 0;
        _global = globalPort;
    }

    /// <summary>Whether a global starting port was configured.</summary>
    public bool UsesGlobalPort => _globalConfigured;

    /// <summary>Returns the global starting port, then increments it for the next instance.</summary>
    public int NextGlobalPort() => _global++;

    /// <summary>
    /// Returns the next port for the engine identified by <paramref name="databaseVersion"/>
    /// (for example <c>POSTGRES_15</c>), incrementing that engine's counter. An unrecognised
    /// version yields <c>0</c>, matching the binary (an OS-assigned ephemeral port).
    /// </summary>
    public int NextEnginePort(string databaseVersion)
    {
        if (databaseVersion.StartsWith("MYSQL", StringComparison.OrdinalIgnoreCase))
        {
            return _mysql++;
        }

        if (databaseVersion.StartsWith("POSTGRES", StringComparison.OrdinalIgnoreCase))
        {
            return _postgres++;
        }

        if (databaseVersion.StartsWith("SQLSERVER", StringComparison.OrdinalIgnoreCase))
        {
            return _sqlserver++;
        }

        return 0;
    }
}
