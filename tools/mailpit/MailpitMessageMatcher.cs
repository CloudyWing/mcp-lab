namespace CloudyWing.McpLab.Mailpit;

/// <summary>
/// Matches Mailpit message summaries against optional recipient and subject filters.
/// </summary>
public static class MailpitMessageMatcher {
    /// <summary>
    /// Determines whether a message summary matches all supplied filters.
    /// </summary>
    public static bool Matches(JsonNode? message, string to, string subjectContains) {
        if (!string.IsNullOrWhiteSpace(subjectContains)
            && !GetString(message, "Subject").Contains(subjectContains, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        return string.IsNullOrWhiteSpace(to) || AddressListContains(message?["To"], to);
    }

    private static bool AddressListContains(JsonNode? addresses, string expected) {
        if (addresses is not JsonArray arr) {
            return false;
        }

        foreach (JsonNode? address in arr) {
            if (string.Equals(GetString(address, "Address"), expected, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static string GetString(JsonNode? node, string property) =>
        node?[property]?.ToString() ?? "";
}
