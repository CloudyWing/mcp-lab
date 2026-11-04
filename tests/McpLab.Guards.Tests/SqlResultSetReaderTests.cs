using System.Data;
using System.Text.Json.Nodes;

using CloudyWing.McpLab.Shared;

using NUnit.Framework;

namespace CloudyWing.McpLab.Guards.Tests;

internal sealed class SqlResultSetReaderTests {
    [TestCase(3, 3, false)]
    [TestCase(3, 5, false)]
    [TestCase(5, 3, true)]
    public void Read_RowCountAgainstLimit_ReportsTruncatedOnlyWhenRowsRemain(int rowCount, int maxRows, bool expectedTruncated) {
        using DataTableReader reader = CreateTable(rowCount, "v").CreateDataReader();

        JsonNode result = ToJson(SqlResultSetReader.Read(reader, maxRows, 100));

        Assert.Multiple(() => {
            Assert.That(result["returned_count"]!.GetValue<int>(), Is.EqualTo(Math.Min(rowCount, maxRows)));
            Assert.That(result["truncated"]!.GetValue<bool>(), Is.EqualTo(expectedTruncated));
        });
    }

    [Test]
    public void Read_LongCell_CutsTextAndCountsTruncatedCells() {
        using DataTableReader reader = CreateTable(2, new string('x', 50)).CreateDataReader();

        JsonNode result = ToJson(SqlResultSetReader.Read(reader, 10, 10));
        string cell = result["rows"]![0]![0]!.GetValue<string>();

        Assert.Multiple(() => {
            Assert.That(cell, Does.StartWith(new string('x', 10) + "…"));
            Assert.That(cell, Does.Contain("50 chars"));
            Assert.That(result["truncated_cells"]!.GetValue<int>(), Is.EqualTo(2));
        });
    }

    [Test]
    public void Read_NullCell_ReturnsNullText() {
        DataTable table = new();
        table.Columns.Add("value", typeof(string));
        table.Rows.Add(DBNull.Value);
        using DataTableReader reader = table.CreateDataReader();

        JsonNode result = ToJson(SqlResultSetReader.Read(reader, 10, 10));

        Assert.That(result["rows"]![0]![0]!.GetValue<string>(), Is.EqualTo("NULL"));
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
