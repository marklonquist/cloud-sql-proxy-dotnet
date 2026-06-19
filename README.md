# cloud-sql-proxy-dotnet

**In-process Google Cloud SQL connector for .NET.** Connect to Cloud SQL for **PostgreSQL** or
**MySQL** directly from your application over mTLS — with **no Cloud SQL Auth Proxy binary and no
sidecar process**.

Google ships in-process connectors for
[Go](https://github.com/GoogleCloudPlatform/cloud-sql-go-connector),
[Java](https://github.com/GoogleCloudPlatform/cloud-sql-jdbc-socket-factory),
[Python](https://github.com/GoogleCloudPlatform/cloud-sql-python-connector), and Node — but
**not for .NET**. The documented .NET path is the out-of-process
[Cloud SQL Auth Proxy](https://github.com/GoogleCloudPlatform/cloud-sql-proxy). This project fills
that gap: the same secure connection logic, embedded in your process.

> [!NOTE]
> This is an independent, community implementation of the Cloud SQL connector protocol. It is not
> affiliated with or endorsed by Google.

## Why in-process?

The Cloud SQL Auth Proxy runs as a separate process or Kubernetes sidecar that you deploy,
configure, monitor, and keep patched. An in-process connector removes that moving part entirely —
the secure connection is established by your own application code, on demand.

## Packages

| Package | Use |
| --- | --- |
| [`CloudSql.Connector`](src/CloudSql.Connector) | Core connector: metadata refresh, cert rotation, raw mTLS dialer, DI. |
| [`CloudSql.Connector.Npgsql`](src/CloudSql.Connector.Npgsql) | `NpgsqlDataSource` for Cloud SQL for PostgreSQL. |
| [`CloudSql.Connector.MySqlConnector`](src/CloudSql.Connector.MySqlConnector) | `MySqlDataSource` for Cloud SQL for MySQL. |

```bash
dotnet add package CloudSql.Connector.Npgsql
# or
dotnet add package CloudSql.Connector.MySqlConnector
```

Targets **.NET 10**.

## Authentication

The connector calls the Cloud SQL Admin API using
[Application Default Credentials](https://cloud.google.com/docs/authentication/application-default-credentials).
For local development:

```bash
gcloud auth application-default login
```

In Google Cloud (GKE, Cloud Run, GCE, …) the attached service account is used automatically. The
principal needs the **Cloud SQL Client** role (`roles/cloudsql.client`).

## Usage

### PostgreSQL (Npgsql)

```csharp
using CloudSql.Connector;
using CloudSql.Connector.Npgsql;

await using var connector = await CloudSqlConnector.CreateAsync();

await using var dataSource = await connector.CreateCloudSqlDataSourceAsync(
    instanceConnectionName: "my-project:europe-west1:my-instance",
    baseConnectionString: "Database=mydb;Username=postgres;Password=secret");

await using var cmd = dataSource.CreateCommand("SELECT version();");
Console.WriteLine(await cmd.ExecuteScalarAsync());
```

### MySQL (MySqlConnector)

```csharp
using CloudSql.Connector;
using CloudSql.Connector.MySql;

await using var connector = await CloudSqlConnector.CreateAsync();

await using var dataSource = await connector.CreateCloudSqlDataSourceAsync(
    instanceConnectionName: "my-project:europe-west1:my-instance",
    baseConnectionString: "Database=mydb;User ID=root;Password=secret");
```

### Dependency injection

```csharp
// PostgreSQL
builder.Services.AddCloudSqlNpgsqlDataSource(
    "my-project:europe-west1:my-instance",
    "Database=mydb;Username=postgres;Password=secret");

// MySQL
builder.Services.AddCloudSqlMySqlDataSource(
    "my-project:europe-west1:my-instance",
    "Database=mydb;User ID=root;Password=secret");
```

Inject `NpgsqlDataSource` / `MySqlDataSource` (or a `DbConnection`) anywhere.

### Auto-starting proxies from configuration

To start a loopback proxy for several instances automatically at startup — the in-process
equivalent of the Cloud SQL Auth Proxy binary's `--instances` list — list them in
`ConnectorOptions.Instances`. Registering the connector adds a hosted service that binds a
`127.0.0.1` listener per instance before the app serves requests.

```jsonc
// appsettings.json
{
  "CloudSql": {
    "Instances": [
      "my-project:europe-west1:db-a",
      "my-project:europe-west1:db-b"
    ]
  }
}
```

```csharp
builder.Services.AddCloudSqlConnector(
    options => builder.Configuration.GetSection("CloudSql").Bind(options));
```

Resolve a started proxy's loopback endpoint anywhere by calling `StartLocalProxyAsync` with the
same instance name — it is idempotent and returns the already-running proxy:

```csharp
var endpoint = await connector.StartLocalProxyAsync("my-project:europe-west1:db-a");
// endpoint is the 127.0.0.1:<port> the proxy bound at startup.
```

### Private IP / PSC

```csharp
await connector.CreateCloudSqlDataSourceAsync(
    "my-project:europe-west1:my-instance",
    "Database=mydb;Username=postgres;Password=secret",
    ipType: IpType.Private);   // or IpType.Psc
```

### IAM database authentication

No password — the connector supplies a Cloud SQL IAM access token as the password and refreshes it
automatically. Set the username to the IAM principal (a database user's email; for service accounts
drop the `.gserviceaccount.com` suffix).

```csharp
await using var connector = await CloudSqlConnector.CreateAsync(new ConnectorOptions
{
    EnableIamAuthentication = true,
});

await using var dataSource = await connector.CreateCloudSqlDataSourceAsync(
    "my-project:europe-west1:my-instance",
    "Database=mydb;Username=my-sa@my-project.iam",
    useIamAuthentication: true);
```

### Raw mTLS stream (advanced)

For drivers without an integration package, get an already-authenticated stream directly:

```csharp
await using var stream = await connector.ConnectAsync("my-project:europe-west1:my-instance");
// 'stream' is an mTLS connection straight to the instance's database server.
```

## How it works

Because no .NET ADO.NET driver (Npgsql, MySqlConnector, MySql.Data) exposes a hook to supply a
pre-connected socket, the connector runs the proxy logic **inside your process**:

1. **Metadata** — calls the Admin API `connectSettings` for the instance's IP addresses, server CA
   certificate, CA mode, and DNS name.
2. **Ephemeral certificate** — generates an RSA-2048 key pair locally and calls
   `generateEphemeralCert` to have a short-lived (~1h) client certificate signed. The private key
   never leaves the process. A background refresh replaces the certificate ~4 minutes before expiry.
3. **In-process loopback proxy** — binds `127.0.0.1:0`. For each connection a driver makes, it dials
   the instance on port `3307`, performs the **mTLS handshake** (presenting the ephemeral cert,
   validating the server against the instance CA and identity — legacy `project:instance` CN or CAS
   SAN/DNS), then pumps bytes between the driver and the instance. Loopback traffic never leaves the
   host, so the driver connects without its own TLS.

```
your app ──plaintext──▶ 127.0.0.1:<port> (in-process proxy) ──mTLS──▶ Cloud SQL instance :3307
```

This is exactly what the Cloud SQL Auth Proxy does — running as a task inside your process instead
of as a separate binary.

## Releasing

Tag-driven via GitHub Actions ([`.github/workflows/release.yml`](.github/workflows/release.yml)):

```bash
git tag v0.1.0
git push origin v0.1.0
```

The workflow derives the version from the tag, builds, tests, packs the three libraries, and pushes
to NuGet.org via [Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing)
(OIDC — no long-lived API key). A matching trusted-publishing policy must exist on nuget.org and the
repo must have a `NUGET_USER` secret (your nuget.org profile name).

## License

[Apache 2.0](LICENSE).
