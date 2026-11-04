using System.Text.Json;
using System.Text.Json.Serialization;
using CloudyWing.McpLab.Shared;

namespace CloudyWing.McpLab.Admin.Services;

/// <summary>
/// The <c>/config/status</c> response of a service.
/// </summary>
public sealed class ServiceStatus {
    /// <summary>Gets or sets the service name.</summary>
    [JsonPropertyName("service")]
    public string Service { get; set; } = "";

    /// <summary>Gets or sets the config file state.</summary>
    [JsonPropertyName("config_file")]
    public ConfigFileStatus? ConfigFile { get; set; }

    /// <summary>Gets or sets per-connection load results.</summary>
    [JsonPropertyName("connections")]
    public List<ConnectionLoadStatus>? Connections { get; set; }

    /// <summary>Gets or sets ignored settings.</summary>
    [JsonPropertyName("ignored_settings")]
    public List<IgnoredSettingStatus>? IgnoredSettings { get; set; }

    /// <summary>Gets or sets when connections were last rebuilt.</summary>
    [JsonPropertyName("connections_loaded_at")]
    public DateTimeOffset? ConnectionsLoadedAt { get; set; }

    /// <summary>Gets or sets service-level settings with effective values.</summary>
    [JsonPropertyName("settings")]
    public List<SettingStatus>? Settings { get; set; }
}

/// <summary>Config file state.</summary>
public sealed class ConfigFileStatus {
    /// <summary>Gets or sets whether the file exists.</summary>
    [JsonPropertyName("exists")]
    public bool Exists { get; set; }

    /// <summary>Gets or sets when the file was last read successfully.</summary>
    [JsonPropertyName("last_loaded_at")]
    public DateTimeOffset? LastLoadedAt { get; set; }

    /// <summary>Gets or sets the latest syntax error.</summary>
    [JsonPropertyName("last_error")]
    public string? LastError { get; set; }
}

/// <summary>Load result of one connection.</summary>
public sealed class ConnectionLoadStatus {
    /// <summary>Gets or sets the alias.</summary>
    [JsonPropertyName("alias")]
    public string Alias { get; set; } = "";

    /// <summary>Gets or sets the connection name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>Gets or sets the state.</summary>
    [JsonPropertyName("state")]
    public string State { get; set; } = "";

    /// <summary>Gets or sets the rejection reasons.</summary>
    [JsonPropertyName("errors")]
    public List<string> Errors { get; set; } = [];
}

/// <summary>An ignored setting.</summary>
public sealed class IgnoredSettingStatus {
    /// <summary>Gets or sets the variable name.</summary>
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    /// <summary>Gets or sets why it was ignored.</summary>
    [JsonPropertyName("reason")]
    public string Reason { get; set; } = "";
}

/// <summary>A service-level setting with its effective value.</summary>
public sealed class SettingStatus {
    /// <summary>Gets or sets the variable name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>Gets or sets the description.</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    /// <summary>Gets or sets the unit.</summary>
    [JsonPropertyName("unit")]
    public string Unit { get; set; } = "";

    /// <summary>Gets or sets the default value.</summary>
    [JsonPropertyName("default_value")]
    public double DefaultValue { get; set; }

    /// <summary>Gets or sets the smallest accepted value.</summary>
    [JsonPropertyName("min")]
    public double Min { get; set; }

    /// <summary>Gets or sets the largest accepted value.</summary>
    [JsonPropertyName("max")]
    public double Max { get; set; }

    /// <summary>Gets or sets older names read when the setting is empty.</summary>
    [JsonPropertyName("fallbacks")]
    public List<string> Fallbacks { get; set; } = [];

    /// <summary>Gets or sets the value in effect.</summary>
    [JsonPropertyName("effective")]
    public double Effective { get; set; }
}

/// <summary>
/// Result of reading a service's status.
/// </summary>
/// <param name="Status">The status, or <see langword="null"/> when the service could not be reached.</param>
/// <param name="Error">Why the status could not be read.</param>
public sealed record ServiceStatusResult(ServiceStatus? Status, string? Error);

/// <summary>
/// Reads <c>/config/status</c> from MCP services over the Compose network.
/// </summary>
public sealed class ServiceStatusClient {
    private readonly HttpClient http;
    private readonly ConfigStore store;
    private readonly ServiceEndpoints endpoints;

    /// <summary>
    /// Initializes a new instance of <see cref="ServiceStatusClient"/>.
    /// </summary>
    public ServiceStatusClient(HttpClient http, ConfigStore store, ServiceEndpoints endpoints) {
        this.http = http;
        this.store = store;
        this.endpoints = endpoints;
    }

    /// <summary>
    /// Reads the status of a service.
    /// </summary>
    public async Task<ServiceStatusResult> GetAsync(string service, CancellationToken cancellationToken = default) {
        try {
            using HttpRequestMessage request = new(HttpMethod.Get, endpoints.BaseUrl(service) + "/config/status");
            string token = endpoints.TokenFor(store, service);

            if (token.Length > 0) {
                request.Headers.Authorization = new("Bearer", token);
            }

            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using HttpResponseMessage response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode) {
                return new(null, $"服務回應 HTTP {(int)response.StatusCode}。");
            }

            ServiceStatus? status = await JsonSerializer
                .DeserializeAsync<ServiceStatus>(await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false), cancellationToken: timeout.Token)
                .ConfigureAwait(false);

            return new(status, status is null ? "服務回應的格式不正確。" : null);
        } catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) {
            return new(null, "無法連線到服務，請確認容器已啟動。");
        }
    }
}

/// <summary>
/// Builds service URLs and finds the token each service expects.
/// </summary>
/// <param name="UrlTemplate">URL template where <c>{0}</c> is the service name, for example <c>http://{0}:8080</c>.</param>
public sealed record ServiceEndpoints(string UrlTemplate) {
    /// <summary>
    /// Gets the base URL of a service.
    /// </summary>
    public string BaseUrl(string service) => string.Format(System.Globalization.CultureInfo.InvariantCulture, UrlTemplate, service).TrimEnd('/');

    /// <summary>
    /// Reads the MCP token configured for a service, or an empty string.
    /// </summary>
    public string TokenFor(ConfigStore store, string service) {
        try {
            return EnvFile.Parse(store.Read(service)).Values.GetValueOrDefault(McpAccessToken.VariableName)?.Trim() ?? "";
        } catch (FormatException) {
            return "";
        }
    }
}
