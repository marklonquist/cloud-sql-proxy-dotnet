using System.Diagnostics.CodeAnalysis;

namespace CloudSql.Connector;

/// <summary>
/// A parsed Cloud SQL instance connection name of the form
/// <c>project:region:instance</c> (for example <c>my-project:europe-west1:my-db</c>).
/// </summary>
public sealed record InstanceConnectionName
{
    private InstanceConnectionName(string project, string region, string instance, string original)
    {
        Project = project;
        Region = region;
        Instance = instance;
        Original = original;
    }

    /// <summary>The GCP project that owns the instance.</summary>
    public string Project { get; }

    /// <summary>The instance's region (for example <c>europe-west1</c>).</summary>
    public string Region { get; }

    /// <summary>The short instance id.</summary>
    public string Instance { get; }

    /// <summary>The original, unparsed <c>project:region:instance</c> string.</summary>
    public string Original { get; }

    /// <summary>
    /// The instance identity embedded in the legacy server certificate's Common Name:
    /// <c>project:instance</c> (note: the region is intentionally omitted).
    /// </summary>
    public string ServerCertCommonName => $"{Project}:{Instance}";

    /// <summary>
    /// Parses a <c>project:region:instance</c> connection name.
    /// </summary>
    /// <exception cref="FormatException">The value is not a valid connection name.</exception>
    public static InstanceConnectionName Parse(string value)
    {
        if (!TryParse(value, out var result))
        {
            throw new FormatException(
                $"'{value}' is not a valid Cloud SQL instance connection name. " +
                "Expected the form 'project:region:instance'.");
        }

        return result;
    }

    /// <summary>
    /// Attempts to parse a <c>project:region:instance</c> connection name.
    /// </summary>
    public static bool TryParse(
        [NotNullWhen(true)] string? value,
        [NotNullWhen(true)] out InstanceConnectionName? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        // A project id may itself contain a single colon for domain-scoped projects
        // ("domain.com:project"), so split from the right: the last two segments are
        // always region and instance.
        var lastColon = value.LastIndexOf(':');
        if (lastColon <= 0 || lastColon == value.Length - 1)
        {
            return false;
        }

        var secondColon = value.LastIndexOf(':', lastColon - 1);
        if (secondColon <= 0)
        {
            return false;
        }

        var project = value[..secondColon];
        var region = value[(secondColon + 1)..lastColon];
        var instance = value[(lastColon + 1)..];

        if (project.Length == 0 || region.Length == 0 || instance.Length == 0)
        {
            return false;
        }

        result = new InstanceConnectionName(project, region, instance, value);
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => Original;
}
