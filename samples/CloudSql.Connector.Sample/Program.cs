using CloudSql.Connector;
using CloudSql.Connector.Npgsql;

// Demonstrates connecting to a Cloud SQL for PostgreSQL instance in-process — no Cloud SQL
// Auth Proxy binary or sidecar. Requires Application Default Credentials with access to the
// instance (`gcloud auth application-default login`).
//
// Usage: dotnet run -- <project:region:instance> <database> <user> [password]

if (args.Length < 3)
{
    Console.Error.WriteLine(
        "Usage: dotnet run -- <project:region:instance> <database> <user> [password]");
    return 1;
}

var instanceConnectionName = args[0];
var database = args[1];
var user = args[2];
var password = args.Length > 3 ? args[3] : null;
var useIam = password is null;

await using var connector = await CloudSqlConnector.CreateAsync(new ConnectorOptions
{
    DefaultIpType = IpType.Public,
    EnableIamAuthentication = useIam,
});

var baseConnectionString =
    $"Database={database};Username={user};" + (password is null ? "" : $"Password={password};");

await using var dataSource = await connector.CreateCloudSqlDataSourceAsync(
    instanceConnectionName,
    baseConnectionString,
    useIamAuthentication: useIam);

await using var command = dataSource.CreateCommand("SELECT version();");
var version = await command.ExecuteScalarAsync();

Console.WriteLine($"Connected to Cloud SQL instance '{instanceConnectionName}'.");
Console.WriteLine($"Server version: {version}");
return 0;
