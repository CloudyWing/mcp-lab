using System.Text;
using System.Text.Json;
using CloudyWing.McpLab.ApiContract;
using CloudyWing.McpLab.Shared;
using NUnit.Framework;

namespace CloudyWing.McpLab.Guards.Tests;

internal sealed class ApiContractTests {
    [Test]
    public void Parse_JsonSpec_ReturnsEndpointSummary() {
        OpenApiSpec spec = OpenApiSpec.Parse(BaselineSpec);

        IReadOnlyList<OpenApiEndpoint> endpoints = spec.ListEndpoints();

        using (Assert.EnterMultipleScope()) {
            Assert.That(spec.Title, Is.EqualTo("Sample API"));
            Assert.That(spec.Version, Is.EqualTo("3.0.3"));
            Assert.That(endpoints, Has.Count.EqualTo(2));
            Assert.That(endpoints[0].Path, Is.EqualTo("/users"));
            Assert.That(endpoints[0].Method, Is.EqualTo("GET"));
            Assert.That(endpoints[0].StatusCodes, Does.Contain("200"));
        }
    }

    [Test]
    public void Compare_RemovedOperation_ReturnsBreakingChange() {
        OpenApiSpec baseline = OpenApiSpec.Parse(BaselineSpec);
        OpenApiSpec candidate = OpenApiSpec.Parse(CandidateSpec);

        IReadOnlyList<ApiContractChange> changes = OpenApiDriftDetector.Compare(baseline, candidate);

        using (Assert.EnterMultipleScope()) {
            Assert.That(changes.Any(change => change.Kind == "operation_removed"), Is.True);
            Assert.That(changes.Any(change => change.Kind == "response_status_removed"), Is.True);
        }
    }

    [Test]
    public void Validate_MissingRequiredProperty_ReturnsIssue() {
        OpenApiSpec spec = OpenApiSpec.Parse(BaselineSpec);

        IReadOnlyList<string> issues = ApiResponseValidator.Validate(
            spec,
            "/users",
            "GET",
            200,
            "[{\"id\":1}]",
            20
        );

        Assert.That(issues, Has.Some.Contains("$[0].name is required"));
    }

    [Test]
    public void ParseUtf8_JsonSpec_MatchesParse() {
        OpenApiSpec fromText = OpenApiSpec.Parse(BaselineSpec);
        OpenApiSpec fromBytes = OpenApiSpec.ParseUtf8(Encoding.UTF8.GetBytes(BaselineSpec));

        using (Assert.EnterMultipleScope()) {
            Assert.That(fromBytes.Title, Is.EqualTo(fromText.Title));
            Assert.That(fromBytes.Version, Is.EqualTo(fromText.Version));
            Assert.That(
                fromBytes.ListEndpoints().Select(endpoint => $"{endpoint.Method} {endpoint.Path} {endpoint.OperationId}"),
                Is.EqualTo(fromText.ListEndpoints().Select(endpoint => $"{endpoint.Method} {endpoint.Path} {endpoint.OperationId}"))
            );
        }
    }

    [Test]
    public void ParseUtf8_JsonSpecWithBom_ParsesDocument() {
        byte[] bytes = [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(BaselineSpec)];

        OpenApiSpec spec = OpenApiSpec.ParseUtf8(bytes);

        Assert.That(spec.Title, Is.EqualTo("Sample API"));
    }

    [Test]
    public void ParseUtf8_YamlSpec_FallsBackToYamlParser() {
        const string yaml = """
            openapi: 3.0.3
            info:
              title: Yaml API
              version: 1.0.0
            paths:
              /ping:
                get:
                  responses:
                    "200":
                      description: OK
            """;

        OpenApiSpec spec = OpenApiSpec.ParseUtf8(Encoding.UTF8.GetBytes(yaml));

        using (Assert.EnterMultipleScope()) {
            Assert.That(spec.Title, Is.EqualTo("Yaml API"));
            Assert.That(spec.ListEndpoints(), Has.Count.EqualTo(1));
        }
    }

    [Test]
    public void ParseUtf8_YamlSpecWithBom_ParsesDocument() {
        byte[] bytes = [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes("openapi: 3.0.3\npaths: {}\n")];

        OpenApiSpec spec = OpenApiSpec.ParseUtf8(bytes);

        Assert.That(spec.Version, Is.EqualTo("3.0.3"));
    }

    [TestCase("not a spec at all {{{")]
    [TestCase("")]
    [TestCase("[1, 2, 3]")]
    [TestCase("""{"openapi":"3.0.3"}""")]
    public void ParseUtf8_InvalidContent_ThrowsLikeParse(string text) {
        Exception? expected = Assert.Catch(() => OpenApiSpec.Parse(text));

        Exception? actual = Assert.Catch(() => OpenApiSpec.ParseUtf8(Encoding.UTF8.GetBytes(text)));

        using (Assert.EnterMultipleScope()) {
            Assert.That(expected, Is.Not.Null);
            Assert.That(actual, Is.Not.Null);
            Assert.That(actual!.GetType(), Is.EqualTo(expected!.GetType()));
        }
    }

    [Test]
    public void Compare_EndpointMaps_MatchesSpecOverload() {
        OpenApiSpec baseline = OpenApiSpec.Parse(BaselineSpec);
        OpenApiSpec candidate = OpenApiSpec.Parse(CandidateSpec);

        IReadOnlyList<ApiContractChange> fromSpecs = OpenApiDriftDetector.Compare(baseline, candidate);
        IReadOnlyList<ApiContractChange> fromMaps = OpenApiDriftDetector.Compare(
            baseline.GetEndpointMap(),
            candidate.GetEndpointMap()
        );

        using (Assert.EnterMultipleScope()) {
            Assert.That(fromMaps, Is.Not.Empty);
            Assert.That(fromMaps, Is.EqualTo(fromSpecs));
        }
    }

    [Test]
    public void GetResponseKeys_ExistingOperation_ReturnsResponseKeys() {
        OpenApiSpec spec = OpenApiSpec.Parse(BaselineSpec);

        IReadOnlyList<string> keys = spec.GetResponseKeys("/users", "get");

        Assert.That(keys, Is.EquivalentTo(new[] { "200", "400" }));
    }

    [TestCase("/missing", "GET")]
    [TestCase("/users", "DELETE")]
    public void GetResponseKeys_MissingPathOrMethod_ReturnsEmpty(string path, string method) {
        OpenApiSpec spec = OpenApiSpec.Parse(BaselineSpec);

        IReadOnlyList<string> keys = spec.GetResponseKeys(path, method);

        Assert.That(keys, Is.Empty);
    }

    [TestCase(200, true)]
    [TestCase(404, false)]
    public void IsStatusDefined_ExactStatusCodes_MatchesGetResponse(int statusCode, bool expected) {
        OpenApiSpec spec = OpenApiSpec.Parse(BaselineSpec);
        IReadOnlyList<string> keys = spec.GetResponseKeys("/users/{id}", "GET");

        bool defined = OpenApiSpec.IsStatusDefined(keys, statusCode);

        using (Assert.EnterMultipleScope()) {
            Assert.That(defined, Is.EqualTo(expected));
            Assert.That(spec.GetResponse("/users/{id}", "GET", statusCode) is not null, Is.EqualTo(defined));
        }
    }

    [Test]
    public void IsStatusDefined_DefaultResponse_CoversAnyStatusCode() {
        string[] keys = ["200", "default"];

        Assert.That(OpenApiSpec.IsStatusDefined(keys, 503), Is.True);
    }

    [Test]
    public void IsStatusDefined_NoKeys_ReturnsFalse() {
        Assert.That(OpenApiSpec.IsStatusDefined([], 200), Is.False);
    }

    [Test]
    public void GetOperationJson_ChineseSummary_WritesOriginalCharacters() {
        OpenApiSpec spec = OpenApiSpec.Parse("""
            {
              "openapi": "3.0.3",
              "info": { "title": "Sample API", "version": "1.0.0" },
              "paths": {
                "/users": {
                  "get": {
                    "summary": "取得中文使用者清單",
                    "responses": { "200": { "description": "OK" } }
                  }
                }
              }
            }
            """);

        string json = spec.GetOperationJson("/users", "GET");

        using (Assert.EnterMultipleScope()) {
            Assert.That(json, Does.Contain("取得中文使用者清單"));
            Assert.That(json, Does.Not.Contain("\\u4E2D").And.Not.Contain("\\u4e2d"));
        }
    }

    [TestCase(200_000, 1_048_576, 200_000, false)]
    [TestCase(1_048_576, 200_000, 200_000, true)]
    [TestCase(300_000, 300_000, 300_000, true)]
    public void CreateInvokeResponse_TruncatedBody_ReportsEffectiveLimit(
        int maxBodyBytes,
        int maxResponseBytes,
        int expectedLimit,
        bool expectedMessageMentionsLimit
    ) {
        BoundedBody body = new(new byte[expectedLimit], true, 500_000, true);

        string json = ApiContractTools.CreateInvokeResponse(new { body_truncated = true, body = "x" }, body, maxBodyBytes, maxResponseBytes);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        JsonElement data = root.GetProperty("data");
        using (Assert.EnterMultipleScope()) {
            Assert.That(data.GetProperty("max_response_bytes").GetInt32(), Is.EqualTo(expectedLimit));
            Assert.That(data.GetProperty("truncated").GetBoolean(), Is.True);
            Assert.That(data.GetProperty("body_truncated").GetBoolean(), Is.True);
            Assert.That(data.GetProperty("original_bytes").GetInt64(), Is.EqualTo(500_000));
            Assert.That(data.GetProperty("original_bytes_exact").GetBoolean(), Is.True);
            Assert.That(root.GetProperty("message").GetString() != "OK", Is.EqualTo(expectedMessageMentionsLimit));
        }
    }

    [Test]
    public void CreateInvokeResponse_CompleteBody_OmitsTruncationFields() {
        BoundedBody body = new(new byte[10], false, 10, true);

        string json = ApiContractTools.CreateInvokeResponse(new { body_truncated = false, body = "x" }, body, 1000, 2000);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement data = document.RootElement.GetProperty("data");
        using (Assert.EnterMultipleScope()) {
            Assert.That(data.TryGetProperty("max_response_bytes", out _), Is.False);
            Assert.That(data.TryGetProperty("truncated", out _), Is.False);
        }
    }

    private const string BaselineSpec = """
        {
          "openapi": "3.0.3",
          "info": {
            "title": "Sample API",
            "version": "1.0.0"
          },
          "servers": [
            {
              "url": "https://api.example.test"
            }
          ],
          "paths": {
            "/users": {
              "get": {
                "operationId": "listUsers",
                "tags": ["Users"],
                "responses": {
                  "200": {
                    "description": "OK",
                    "content": {
                      "application/json": {
                        "schema": {
                          "type": "array",
                          "items": {
                            "type": "object",
                            "required": ["id", "name"],
                            "properties": {
                              "id": { "type": "integer" },
                              "name": { "type": "string" }
                            }
                          }
                        }
                      }
                    }
                  },
                  "400": {
                    "description": "Bad Request"
                  }
                }
              }
            },
            "/users/{id}": {
              "get": {
                "operationId": "getUser",
                "responses": {
                  "200": {
                    "description": "OK"
                  }
                }
              }
            }
          }
        }
        """;

    private const string CandidateSpec = """
        {
          "openapi": "3.0.3",
          "info": {
            "title": "Sample API",
            "version": "1.1.0"
          },
          "paths": {
            "/users": {
              "get": {
                "operationId": "listUsers",
                "responses": {
                  "200": {
                    "description": "OK"
                  }
                }
              }
            }
          }
        }
        """;
}
