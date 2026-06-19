# CloudSql.Connector

In-process [Google Cloud SQL](https://cloud.google.com/sql) connector for .NET — connect to
Cloud SQL for **PostgreSQL** or **MySQL** directly from your application over mTLS, with **no
Cloud SQL Auth Proxy binary or sidecar process**.

This is the in-process connector .NET has been missing — the equivalent of Google's
[Go](https://github.com/GoogleCloudPlatform/cloud-sql-go-connector),
[Java](https://github.com/GoogleCloudPlatform/cloud-sql-jdbc-socket-factory), and
[Python](https://github.com/GoogleCloudPlatform/cloud-sql-python-connector) connectors.

## What it does

- Discovers instance metadata via the Cloud SQL Admin API (`connectSettings`).
- Mints and **automatically rotates** a short-lived client certificate (`generateEphemeralCert`)
  ahead of expiry.
- Performs the **mTLS handshake** to the instance itself (port 3307), validating the server
  against the instance CA and identity (legacy CN or CAS SAN).
- Exposes an in-process loopback endpoint any ADO.NET driver can connect to.
- Supports **IAM database authentication** (access-token passwords, auto-refreshed).
- Selects **public, private (VPC), or PSC** IPs.

## Packages

| Package | Use |
| --- | --- |
| `CloudSql.Connector` | Core connector + raw mTLS dialer + DI. |
| `CloudSql.Connector.Npgsql` | `NpgsqlDataSource` for Cloud SQL for PostgreSQL. |
| `CloudSql.Connector.MySqlConnector` | `MySqlDataSource` for Cloud SQL for MySQL. |

## Quick start (PostgreSQL)

```csharp
using CloudSql.Connector;
using CloudSql.Connector.Npgsql;

await using var connector = await CloudSqlConnector.CreateAsync();

await using var dataSource = await connector.CreateCloudSqlDataSourceAsync(
    "my-project:europe-west1:my-instance",
    "Database=mydb;Username=postgres;Password=...");

await using var cmd = dataSource.CreateCommand("SELECT version();");
Console.WriteLine(await cmd.ExecuteScalarAsync());
```

### Dependency injection

```csharp
builder.Services.AddCloudSqlNpgsqlDataSource(
    "my-project:europe-west1:my-instance",
    "Database=mydb;Username=postgres;Password=...");
```

Authentication uses [Application Default Credentials](https://cloud.google.com/docs/authentication/application-default-credentials).

See the [project README](https://github.com/marklonquist/cloud-sql-proxy-dotnet) for full
documentation, IAM auth, private IP, and how it works.
