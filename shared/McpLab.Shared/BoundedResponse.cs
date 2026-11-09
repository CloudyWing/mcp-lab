namespace CloudyWing.McpLab.Shared;

/// <summary>
/// An HTTP response together with its body read with a size limit.
/// Disposing it aborts a connection whose body was not read completely.
/// </summary>
public sealed class BoundedResponse : IDisposable {
    /// <summary>
    /// Initializes a new instance of <see cref="BoundedResponse"/>.
    /// </summary>
    public BoundedResponse(HttpResponseMessage response, BoundedBody body) {
        Response = response;
        Body = body;
    }

    /// <summary>
    /// Gets the HTTP response, which exposes the status code and headers.
    /// </summary>
    public HttpResponseMessage Response { get; }

    /// <summary>
    /// Gets the body that was read.
    /// </summary>
    public BoundedBody Body { get; }

    /// <summary>
    /// Gets a value indicating whether the status code is in the 2xx range.
    /// </summary>
    public bool IsSuccessStatusCode => Response.IsSuccessStatusCode;

    /// <inheritdoc/>
    public void Dispose() => Response.Dispose();
}
