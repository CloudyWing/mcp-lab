namespace CloudyWing.McpLab.Docker;

/// <summary>
/// Provides read-only HTTP access to the local Docker Engine API.
/// </summary>
public sealed class DockerEngineClient {
    private const string DefaultSocketPath = "/var/run/docker.sock";
    private readonly HttpClient http;

    /// <summary>
    /// Initializes a new instance of <see cref="DockerEngineClient"/> using the configured Docker socket.
    /// </summary>
    public DockerEngineClient() : this(CreateHttpClient()) {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="DockerEngineClient"/> using the specified HTTP client.
    /// </summary>
    public DockerEngineClient(HttpClient http) {
        this.http = http;
    }

    /// <summary>
    /// Gets a plain text response from Docker Engine.
    /// </summary>
    public async Task<string> GetStringAsync(string path) {
        using HttpResponseMessage response = await http.GetAsync(path).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        EnsureSuccess(response, body);

        return body;
    }

    /// <summary>
    /// Gets a binary response from Docker Engine.
    /// </summary>
    public async Task<byte[]> GetBytesAsync(string path) {
        using HttpResponseMessage response = await http.GetAsync(path).ConfigureAwait(false);
        byte[] body = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        EnsureSuccess(response, Encoding.UTF8.GetString(body));

        return body;
    }

    /// <summary>
    /// Gets a JSON object response from Docker Engine.
    /// </summary>
    public async Task<JsonObject> GetObjectAsync(string path) {
        string body = await GetStringAsync(path).ConfigureAwait(false);

        return JsonNode.Parse(body)?.AsObject()
            ?? throw new InvalidOperationException("Docker Engine returned an invalid JSON object.");
    }

    /// <summary>
    /// Gets a JSON array response from Docker Engine.
    /// </summary>
    public async Task<JsonArray> GetArrayAsync(string path) {
        string body = await GetStringAsync(path).ConfigureAwait(false);

        return JsonNode.Parse(body)?.AsArray()
            ?? throw new InvalidOperationException("Docker Engine returned an invalid JSON array.");
    }

    private static HttpClient CreateHttpClient() {
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
