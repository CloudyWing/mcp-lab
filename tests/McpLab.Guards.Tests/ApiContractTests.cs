using CloudyWing.McpLab.ApiContract;
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
