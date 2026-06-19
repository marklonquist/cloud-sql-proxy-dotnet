namespace CloudSql.Connector.Internal;

/// <summary>
/// A parsed auto-start instance entry: the <c>project:region:instance</c> name plus any
/// per-instance listener overrides supplied as a query string, mirroring the Cloud SQL Auth
/// Proxy binary's <c>instance?port=&amp;address=&amp;unix-socket=&amp;unix-socket-path=</c> syntax.
/// </summary>
internal sealed record ProxyInstanceConfig(
    InstanceConnectionName Instance,
    int? Port,
    string? Address,
    string? UnixSocketDir,
    string? UnixSocketPath)
{
    /// <summary>
    /// Parses an instance entry of the form <c>project:region:instance[?key=value&amp;...]</c>.
    /// </summary>
    /// <exception cref="FormatException">The entry or one of its query parameters is invalid.</exception>
    public static ProxyInstanceConfig Parse(string entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entry);

        var queryStart = entry.IndexOf('?');
        var namePart = queryStart < 0 ? entry : entry[..queryStart];
        var instance = InstanceConnectionName.Parse(namePart);

        int? port = null;
        string? address = null;
        string? unixSocketDir = null;
        string? unixSocketPath = null;

        if (queryStart >= 0)
        {
            var query = entry[(queryStart + 1)..];
            foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                var key = (eq < 0 ? pair : pair[..eq]).Trim();
                var value = eq < 0 ? string.Empty : Uri.UnescapeDataString(pair[(eq + 1)..]);

                switch (key.ToLowerInvariant())
                {
                    case "port":
                        if (!int.TryParse(value, out var parsedPort) || parsedPort is < 1 or > 65535)
                        {
                            throw new FormatException(
                                $"Invalid '?port=' value '{value}' for instance '{namePart}'. " +
                                "Expected a port between 1 and 65535.");
                        }

                        port = parsedPort;
                        break;
                    case "address":
                        address = value;
                        break;
                    case "unix-socket":
                        unixSocketDir = value;
                        break;
                    case "unix-socket-path":
                        unixSocketPath = value;
                        break;
                    default:
                        throw new FormatException(
                            $"Unknown query parameter '{key}' for instance '{namePart}'. " +
                            "Supported: port, address, unix-socket, unix-socket-path.");
                }
            }
        }

        return new ProxyInstanceConfig(instance, port, address, unixSocketDir, unixSocketPath);
    }
}
