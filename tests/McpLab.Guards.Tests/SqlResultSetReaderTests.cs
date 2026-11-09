using System.Data;
using System.Text.Json.Nodes;

using CloudyWing.McpLab.Shared;

using NUnit.Framework;

namespace CloudyWing.McpLab.Guards.Tests;

internal sealed class SqlResultSetReaderTests {
    private const int LargeBudget = 1_000_000;

    [TestCase(3, 3, false)]
    [TestCase(3, 5, false)]
    [TestCase(5, 3, true)]
    public async Task ReadAsync_RowCountAgainstLimit_ReportsTruncatedOnlyWhenRowsRemain(int rowCount, int maxRows, bool expectedTruncated) {
        using DataTableReader reader = CreateTable(rowCount, "v").CreateDataReader();

        JsonNode result = ToJson(await SqlResultSetReader.ReadAsync(reader, maxRows, 100, LargeBudget));

        Assert.Multiple(() => {
            Assert.That(result["returned_count"]!.GetValue<int>(), Is.EqualTo(Math.Min(rowCount, maxRows)));
            Assert.That(result["truncated"]!.GetValue<bool>(), Is.EqualTo(expectedTruncated));
        });
    }

    [Test]
    public async Task ReadAsync_LongCell_CutsTextAndCountsTruncatedCells() {
        using DataTableReader reader = CreateTable(2, new string('x', 50)).CreateDataReader();

        JsonNode result = ToJson(await SqlResultSetReader.ReadAsync(reader, 10, 10, LargeBudget));
        string cell = result["rows"]![0]![0]!.GetValue<string>();

        Assert.Multiple(() => {
            Assert.That(cell, Does.StartWith(new string('x', 10) + "…"));
            Assert.That(cell, Does.Contain("50 chars"));
            Assert.That(result["truncated_cells"]!.GetValue<int>(), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task ReadAsync_NullCell_ReturnsNullText() {
        DataTable table = new();
        table.Columns.Add("value", typeof(string));
        table.Rows.Add(DBNull.Value);
        using DataTableReader reader = table.CreateDataReader();

        JsonNode result = ToJson(await SqlResultSetReader.ReadAsync(reader, 10, 10, LargeBudget));

        Assert.That(result["rows"]![0]![0]!.GetValue<string>(), Is.EqualTo("NULL"));
    }

    [Test]
    public async Task ReadAsync_BudgetFitsOnlySomeRows_ReturnsWholeRowsAndTruncationFields() {
        using DataTableReader reader = CreateTable(5, "abcde").CreateDataReader();

        JsonNode result = ToJson(await SqlResultSetReader.ReadAsync(reader, 10, 100, 12));

        Assert.Multiple(() => {
            Assert.That(result["returned_count"]!.GetValue<int>(), Is.EqualTo(2));
            Assert.That(result["rows"]!.AsArray(), Has.Count.EqualTo(2));
            Assert.That(result["truncated"]!.GetValue<bool>(), Is.True);
            Assert.That(result["original_bytes"]!.GetValue<long>(), Is.EqualTo(15));
            Assert.That(result["original_bytes_exact"]!.GetValue<bool>(), Is.False);
            Assert.That(result["max_response_bytes"]!.GetValue<int>(), Is.EqualTo(12));
        });
    }

    [Test]
    public async Task ReadAsync_FirstRowExceedsBudget_ReturnsNoRows() {
        using DataTableReader reader = CreateTable(3, new string('x', 50)).CreateDataReader();

        JsonNode result = ToJson(await SqlResultSetReader.ReadAsync(reader, 10, 100, 20));

        Assert.Multiple(() => {
            Assert.That(result["returned_count"]!.GetValue<int>(), Is.Zero);
            Assert.That(result["rows"]!.AsArray(), Is.Empty);
            Assert.That(result["truncated"]!.GetValue<bool>(), Is.True);
            Assert.That(result["original_bytes"]!.GetValue<long>(), Is.EqualTo(50));
        });
    }

    [Test]
    public async Task ReadAsync_MaxRowsReachedBeforeBudget_KeepsRowLimitBehaviorWithoutNewFields() {
        using DataTableReader reader = CreateTable(5, "abcde").CreateDataReader();

        JsonNode result = ToJson(await SqlResultSetReader.ReadAsync(reader, 2, 100, 1000));

        Assert.Multiple(() => {
            Assert.That(result["returned_count"]!.GetValue<int>(), Is.EqualTo(2));
            Assert.That(result["truncated"]!.GetValue<bool>(), Is.True);
            Assert.That(result["original_bytes"], Is.Null);
        });
    }

    [Test]
    public async Task ReadAsync_BudgetReachedBeforeMaxRows_BudgetWins() {
        using DataTableReader reader = CreateTable(50, "abcde").CreateDataReader();

        JsonNode result = ToJson(await SqlResultSetReader.ReadAsync(reader, 40, 100, 25));

        Assert.Multiple(() => {
            Assert.That(result["returned_count"]!.GetValue<int>(), Is.EqualTo(5));
            Assert.That(result["original_bytes"], Is.Not.Null);
        });
    }

    [Test]
    public async Task ReadAsync_WithinLimits_OutputHasNoTruncationFields() {
        using DataTableReader reader = CreateTable(3, "abcde").CreateDataReader();

        JsonNode result = ToJson(await SqlResultSetReader.ReadAsync(reader, 10, 100, LargeBudget));

        Assert.Multiple(() => {
            Assert.That(result["truncated"]!.GetValue<bool>(), Is.False);
            Assert.That(result["original_bytes"], Is.Null);
            Assert.That(result["original_bytes_exact"], Is.Null);
            Assert.That(result["max_response_bytes"], Is.Null);
        });
    }

    [Test]
    public async Task ReadAsync_MultiByteCells_CountsUtf8Bytes() {
        using DataTableReader reader = CreateTable(3, "測試").CreateDataReader();

        // Each cell is 6 UTF-8 bytes (2 characters), so a 12 byte budget keeps two rows.
        JsonNode result = ToJson(await SqlResultSetReader.ReadAsync(reader, 10, 100, 12));

        Assert.That(result["returned_count"]!.GetValue<int>(), Is.EqualTo(2));
    }

    [Test]
    public async Task ToResponse_TruncatedResult_ReturnsOkWithTruncationMessage() {
        using DataTableReader reader = CreateTable(5, "abcde").CreateDataReader();
        object result = await SqlResultSetReader.ReadAsync(reader, 10, 100, 12);

        JsonNode response = JsonNode.Parse(SqlResultSetReader.ToResponse(result))!;

        Assert.Multiple(() => {
            Assert.That(response["ok"]!.GetValue<bool>(), Is.True);
            Assert.That(response["message"]!.GetValue<string>(), Does.Contain("MCPLAB_MAX_RESPONSE_BYTES"));
            Assert.That(response["data"]!["truncated"]!.GetValue<bool>(), Is.True);
        });
    }

    [Test]
    public async Task ToResponse_FirstRowExceedsBudget_MessageSuggestsNarrowingColumns() {
        using DataTableReader reader = CreateTable(2, new string('x', 50)).CreateDataReader();
        object result = await SqlResultSetReader.ReadAsync(reader, 10, 100, 20);

        JsonNode response = JsonNode.Parse(SqlResultSetReader.ToResponse(result))!;

        Assert.That(response["message"]!.GetValue<string>(), Does.Contain("MAX_CELL_CHARS"));
    }

    [Test]
    public async Task ToResponse_WithinLimits_ReturnsPlainOk() {
        using DataTableReader reader = CreateTable(2, "abcde").CreateDataReader();
        object result = await SqlResultSetReader.ReadAsync(reader, 10, 100, LargeBudget);

        JsonNode response = JsonNode.Parse(SqlResultSetReader.ToResponse(result))!;

        Assert.Multiple(() => {
            Assert.That(response["message"]!.GetValue<string>(), Is.EqualTo("OK"));
            Assert.That(response["data"]!["original_bytes"], Is.Null);
        });
    }

    [Test]
    public async Task ReadAsync_CjkCells_MeasuresThreeBytesPerCharacterAndKeepsWholeRows() {
        using DataTableReader reader = CreateTable(5, "中文中").CreateDataReader();

        // Each cell is 9 UTF-8 bytes (3 characters), so a 27 byte budget keeps three rows.
        JsonNode result = ToJson(await SqlResultSetReader.ReadAsync(reader, 10, 100, 27));

        Assert.Multiple(() => {
            Assert.That(result["returned_count"]!.GetValue<int>(), Is.EqualTo(3));
            Assert.That(result["rows"]!.AsArray(), Has.Count.EqualTo(3));
            Assert.That(result["truncated"]!.GetValue<bool>(), Is.True);
        });
    }

    [Test]
    public async Task ToResponse_CjkTruncatedCells_WritesOriginalCharactersAndTruncationFields() {
        using DataTableReader reader = CreateTable(5, "中文中文中文中文中文").CreateDataReader();
        object result = await SqlResultSetReader.ReadAsync(reader, 10, 3, 66);

        string json = SqlResultSetReader.ToResponse(result);
        JsonNode response = JsonNode.Parse(json)!;
        JsonNode data = response["data"]!;

        Assert.Multiple(() => {
            Assert.That(json, Does.Contain("中文中…"));
            Assert.That(json, Does.Not.Contain("\\u"));
            Assert.That(data["rows"]![0]![0]!.GetValue<string>(), Does.StartWith("中文中…"));
            Assert.That(data["truncated"]!.GetValue<bool>(), Is.True);
            Assert.That(data["original_bytes"], Is.Not.Null);
            Assert.That(data["original_bytes_exact"]!.GetValue<bool>(), Is.False);
            Assert.That(data["max_response_bytes"]!.GetValue<int>(), Is.EqualTo(66));
        });
    }

    private static DataTable CreateTable(int rowCount, string value) {
        DataTable table = new();
        table.Columns.Add("value", typeof(string));

        for (int i = 0; i < rowCount; i++) {
            table.Rows.Add(value);
        }

        return table;
    }

    private static JsonNode ToJson(object value) => JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(value))!;
}
