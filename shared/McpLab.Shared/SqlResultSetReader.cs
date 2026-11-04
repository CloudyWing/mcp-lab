using System.Data.Common;

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
    /// </summary>
    public static async Task<object> ReadAsync(DbDataReader reader, int maxRows, int maxCellChars, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCellChars);

        string[] columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        List<IReadOnlyList<string>> rows = [];
        int truncatedCells = 0;
        bool truncated = false;

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) {
            if (rows.Count >= maxRows) {
                truncated = true;
                break;
            }

            string[] row = new string[reader.FieldCount];

            for (int i = 0; i < reader.FieldCount; i++) {
                string text = reader.IsDBNull(i) ? "NULL" : reader[i]?.ToString() ?? "";

                if (text.Length > maxCellChars) {
                    text = $"{text[..maxCellChars]}…(truncated, {text.Length} chars)";
                    truncatedCells++;
                }

                row[i] = text;
            }

            rows.Add(row);
        }

        return new {
            columns,
            rows,
            returned_count = rows.Count,
            truncated,
            max_rows = maxRows,
            truncated_cells = truncatedCells,
            max_cell_chars = maxCellChars,
        };
    }
}
