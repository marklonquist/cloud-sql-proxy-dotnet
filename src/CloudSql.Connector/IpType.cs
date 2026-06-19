namespace CloudSql.Connector;

/// <summary>
/// Selects which of a Cloud SQL instance's IP addresses the connector dials.
/// </summary>
public enum IpType
{
    /// <summary>The instance's public (PRIMARY) IP address.</summary>
    Public,

    /// <summary>The instance's private (VPC) IP address.</summary>
    Private,

    /// <summary>The instance's Private Service Connect (PSC) endpoint.</summary>
    Psc,
}
