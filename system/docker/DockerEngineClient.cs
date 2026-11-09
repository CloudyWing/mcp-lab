using CloudyWing.McpLab.Shared.DotEnv;

namespace CloudyWing.McpLab.Docker;

/// <summary>
/// Provides read-only HTTP access to the local Docker Engine API.
/// </summary>
public sealed class DockerEngineClient {
    private const string DefaultSocketPath = "/var/run/docker.sock";
    private readonly HttpClient http;
    private readonly IDotEnvSnapshotProvider? snapshotProvider;

    /// <summary>
    /// Initializes a new instance of <see cref="DockerEngineClient"/> using <c>DOCKER_HOST</c> (for example
    /// <c>tcp://mcp-docker-proxy:2375</c>) when set, otherwise the Docker socket at <c>DOCKER_SOCKET_PATH</c>.
    /// </summary>
    /// <param name="snapshotProvider">The .env snapshot provider used to read MCPLAB_MAX_RESPONSE_BYTES.</param>
    public DockerEngineClient(IDotEnvSnapshotProvider snapshotProvider) : this(CreateHttpClient(), snapshotProvider) {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="DockerEngineClient"/> using the specified HTTP client.
    /// </summary>
    /// <param name="http">The HTTP client that reaches Docker Engine.</param>
    /// <param name="snapshotProvider">
    /// The .env snapshot provider used to read MCPLAB_MAX_RESPONSE_BYTES; the default limit applies when omitted.
    /// </param>
    public DockerEngineClient(HttpClient http, IDotEnvSnapshotProvider? snapshotProvider = null) {
        this.http = http;
        this.snapshotProvider = snapshotProvider;
    }

    /// <summary>
    /// Gets the effective response limit, read from the current snapshot on every call.
    /// </summary>
    public int MaxResponseBytes => ServiceSettings.Common.MaxResponseBytes.Read(
        snapshotProvider?.Current ?? new Dictionary<string, string>()
    );

    /// <summary>
    /// Gets a plain text response from Docker Engine.
    /// </summary>
    /// <exception cref="ResponseTooLargeException">The response is larger than <see cref="MaxResponseBytes"/>.</exception>
    public async Task<string> GetStringAsync(string path) {
        int maxBytes = MaxResponseBytes;
        BoundedBody body = await GetBoundedAsync(path, maxBytes).ConfigureAwait(false);

        return body.RequireComplete(maxBytes);
    }

    /// <summary>
    /// Gets a binary response from Docker Engine, reading at most <see cref="MaxResponseBytes"/> bytes.
    /// A longer response is returned as a truncated body instead of an exception.
    /// </summary>
    public Task<BoundedBody> GetBoundedAsync(string path) => GetBoundedAsync(path, MaxResponseBytes);

    /// <summary>
    /// Gets a JSON object response from Docker Engine.
    /// </summary>
    /// <exception cref="ResponseTooLargeException">The response is larger than <see cref="MaxResponseBytes"/>.</exception>
    public async Task<JsonObject> GetObjectAsync(string path) {
        string body = await GetStringAsync(path).ConfigureAwait(false);

        return JsonNode.Parse(body)?.AsObject()
            ?? throw new InvalidOperationException("Docker Engine returned an invalid JSON object.");
    }

    /// <summary>
    /// Gets a JSON array response from Docker Engine.
    /// </summary>
    /// <exception cref="ResponseTooLargeException">The response is larger than <see cref="MaxResponseBytes"/>.</exception>
    public async Task<JsonArray> GetArrayAsync(string path) {
        string body = await GetStringAsync(path).ConfigureAwait(false);

        return JsonNode.Parse(body)?.AsArray()
            ?? throw new InvalidOperationException("Docker Engine returned an invalid JSON array.");
    }

    /// <summary>
    /// Converts a <c>DOCKER_HOST</c> value to an HTTP base address, or returns <see langword="null"/> when it is
    /// empty or a unix socket.
    /// </summary>
    public static Uri? ParseDockerHost(string? dockerHost) {
        if (string.IsNullOrWhiteSpace(dockerHost) || dockerHost.StartsWith("unix://", StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        string value = dockerHost.Trim();
        value = value.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase) ? "http://" + value[6..] : value;

        return Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https"
            ? uri
            : throw new InvalidOperationException($"DOCKER_HOST must be tcp://host:port, http://host:port, or unix://path (got '{dockerHost}').");
    }

    private async Task<BoundedBody> GetBoundedAsync(string path, int maxBytes) {
        using BoundedResponse response = await BoundedHttpContent.GetAsync(http, path, maxBytes).ConfigureAwait(false);
        EnsureSuccess(response.Response, response.Body.ToText());

        return response.Body;
    }

    private static HttpClient CreateHttpClient() {
        if (ParseDockerHost(Environment.GetEnvironmentVariable("DOCKER_HOST")) is Uri baseAddress) {
            return new HttpClient { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(30) };
        }

        string socketPath = Environment.GetEnvironmentVariable("DOCKER_SOCKET_PATH") ?? DefaultSocketPath;
        SocketsHttpHandler handler = new() {
            ConnectCallback = async (_, cancellationToken) => {
                Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

                try {
                    await socket.ConnectAsync(
                        new UnixDomainSocketEndPoint(socketPath),
                        cancellationToken
                    ).ConfigureAwait(false);

                    return new NetworkStream(socket, ownsSocket: true);
                } catch {
                    socket.Dispose();
                    throw;
                }
            }
        };

        return new HttpClient(handler) {
            BaseAddress = new Uri("http://docker")
        };
    }

    private static void EnsureSuccess(HttpResponseMessage response, string body) {
        if (response.IsSuccessStatusCode) {
            return;
        }

        string message = string.IsNullOrWhiteSpace(body)
            ? $"{(int)response.StatusCode} {response.StatusCode}"
            : body;

        throw new InvalidOperationException($"Docker Engine returned {message}");
    }
}
