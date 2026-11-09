using System.Data.Common;
using System.Text.Json.Nodes;

namespace CloudyWing.McpLab.Shared;

/// <summary>
/// Reads a SQL result set into a bounded, JSON-friendly shape shared by the SQL Server and Oracle tools.
/// </summary>
public static class SqlResultSetReader {
    /// <summary>
    /// Default maximum characters kept per cell.
    /// </summary>
    public const int DefaultMaxCellChars = 4000;

    /// <summary>
    /// Upper bound for the configurable maximum characters per cell.
    /// </summary>
    public const int MaxCellCharsLimit = 100000;

    /// <summary>
    /// Reads at most <paramref name="maxRows"/> rows and cuts cell text longer than <paramref name="maxCellChars"/>.
    /// Reading stops at the first row that does not fit in <paramref name="maxResponseBytes"/> of cell text (UTF-8),
    /// and the truncation fields of <see cref="ResponseTruncation"/> are added to the result.
    /// </summary>
    public static async Task<object> ReadAsync(
        DbDataReader reader,
        int maxRows,
        int maxCellChars,
        int maxResponseBytes,
        CancellationToken cancellationToken = default
    ) {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCellChars);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxResponseBytes);

        string[] columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        List<IReadOnlyList<string>> rows = [];
        ResponseBudget budget = new(maxResponseBytes);
        int truncatedCells = 0;
        bool truncated = false;

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) {
            if (rows.Count >= maxRows) {
                truncated = true;
                break;
            }

            string[] row = new string[reader.FieldCount];
            long rowBytes = 0;
            int rowTruncatedCells = 0;

            for (int i = 0; i < reader.FieldCount; i++) {
                string text = reader.IsDBNull(i) ? "NULL" : reader[i]?.ToString() ?? "";

                if (text.Length > maxCellChars) {
                    text = $"{text[..maxCellChars]}…(truncated, {text.Length} chars)";
                    rowTruncatedCells++;
                }

                row[i] = text;
                rowBytes += ResponseBudget.Utf8Size(text);
            }

            // A row is either returned whole or not at all, so the cut falls on a row boundary.
            if (!budget.TryAdd(rowBytes)) {
                break;
            }

            truncatedCells += rowTruncatedCells;
            rows.Add(row);
        }

        var data = new {
            columns,
            rows,
            returned_count = rows.Count,
            truncated,
            max_rows = maxRows,
            truncated_cells = truncatedCells,
            max_cell_chars = maxCellChars,
        };

        return budget.Exhausted ? ResponseTruncation.Append(data, budget) : data;
    }

    /// <summary>
    /// Creates the successful tool response of a result produced by
    /// <see cref="ReadAsync(DbDataReader, int, int, int, CancellationToken)"/>.
    /// A result cut at the response limit carries a message that explains the cut.
    /// </summary>
    public static string ToResponse(object result) {
        ArgumentNullException.ThrowIfNull(result);

        if (result is not JsonObject node || node["original_bytes"] is not JsonNode original) {
            return ToolResponse.Ok(result);
        }

        string message = ResponseTruncation.Message(
            original.GetValue<long>(),
            node["original_bytes_exact"]?.GetValue<bool>() ?? false,
            node["max_response_bytes"]!.GetValue<int>()
        );

        if (node["returned_count"]?.GetValue<int>() == 0) {
            message += " The first row alone exceeds the limit; select fewer columns or lower the *_MAX_CELL_CHARS setting.";
        }

        return ToolResponse.Ok(result, message);
    }
}
