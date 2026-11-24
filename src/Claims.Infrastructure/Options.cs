namespace Claims.Infrastructure;

public sealed class JwtOptions
{
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = "claims-platform";
    public string Audience { get; set; } = "claims-platform-clients";
    public int ExpiryMinutes { get; set; } = 60;
}

public sealed class KafkaOptions
{
    public bool Enabled { get; set; }
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string Topic { get; set; } = "claims.events";
    public string ConsumerGroup { get; set; } = "claims-auto-assignment";
}

public sealed class StorageOptions
{
    public string Path { get; set; } = "App_Data/uploads";
}

public sealed class DatabaseOptions
{
    /// <summary>SqlServer (default) or Sqlite (documented fallback, also used by integration tests).</summary>
    public string Provider { get; set; } = "SqlServer";
}
