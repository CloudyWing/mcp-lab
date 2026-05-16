namespace CloudyWing.McpLab.ApiContract;

/// <summary>
/// Detects endpoint and response status drift between OpenAPI documents.
/// </summary>
public static class OpenApiDriftDetector {
    /// <summary>
    /// Compares <paramref name="baseline"/> against <paramref name="candidate"/>.
    /// </summary>
    public static IReadOnlyList<ApiContractChange> Compare(OpenApiSpec baseline, OpenApiSpec candidate) {
        Dictionary<string, OpenApiEndpoint> baselineMap = new(baseline.GetEndpointMap(), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, OpenApiEndpoint> candidateMap = new(candidate.GetEndpointMap(), StringComparer.OrdinalIgnoreCase);
        List<ApiContractChange> changes = [];

        foreach ((string key, OpenApiEndpoint baselineEndpoint) in baselineMap) {
            if (!candidateMap.TryGetValue(key, out OpenApiEndpoint? candidateEndpoint)) {
                changes.Add(new ApiContractChange(
                    "breaking",
                    "operation_removed",
                    baselineEndpoint.Method,
                    baselineEndpoint.Path,
                    $"{baselineEndpoint.Method} {baselineEndpoint.Path} exists in baseline but not candidate."
                ));
                continue;
            }

            AddStatusCodeChanges(changes, baselineEndpoint, candidateEndpoint);

            if (!baselineEndpoint.RequestBodyRequired && candidateEndpoint.RequestBodyRequired) {
                changes.Add(new ApiContractChange(
                    "breaking",
                    "request_body_became_required",
                    baselineEndpoint.Method,
                    baselineEndpoint.Path,
                    "Candidate requires a request body that was optional or absent in baseline."
                ));
            }
        }

        foreach ((string key, OpenApiEndpoint candidateEndpoint) in candidateMap) {
            if (baselineMap.ContainsKey(key)) {
                continue;
            }

            changes.Add(new ApiContractChange(
                "non_breaking",
                "operation_added",
                candidateEndpoint.Method,
                candidateEndpoint.Path,
                $"{candidateEndpoint.Method} {candidateEndpoint.Path} exists in candidate but not baseline."
            ));
        }

        return changes
            .OrderBy(change => change.Severity, StringComparer.Ordinal)
            .ThenBy(change => change.Path, StringComparer.Ordinal)
            .ThenBy(change => change.Method, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddStatusCodeChanges(
        ICollection<ApiContractChange> changes,
        OpenApiEndpoint baseline,
        OpenApiEndpoint candidate
    ) {
        HashSet<string> candidateStatuses = candidate.StatusCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> baselineStatuses = baseline.StatusCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string statusCode in baseline.StatusCodes.Where(status => !candidateStatuses.Contains(status))) {
            changes.Add(new ApiContractChange(
                "breaking",
                "response_status_removed",
                baseline.Method,
                baseline.Path,
                $"Response status {statusCode} exists in baseline but not candidate."
            ));
        }

        foreach (string statusCode in candidate.StatusCodes.Where(status => !baselineStatuses.Contains(status))) {
            changes.Add(new ApiContractChange(
                "non_breaking",
                "response_status_added",
                candidate.Method,
                candidate.Path,
                $"Response status {statusCode} exists in candidate but not baseline."
            ));
        }
    }
}
