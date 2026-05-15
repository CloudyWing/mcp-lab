using NUnit.Framework;

using SqlServerSqlGuard = CloudyWing.McpLab.SqlServer.SqlGuard;

namespace CloudyWing.McpLab.Guards.Tests;

internal sealed class SqlServerSqlGuardTests {
    [TestCase("SELECT TOP 1 * FROM dbo.Users")]
    [TestCase("DELETE FROM dbo.Users WHERE Id = @id")]
    [TestCase("UPDATE dbo.Users SET DisplayName = @displayName WHERE Id = @id")]
    [TestCase("/* DROP TABLE dbo.Users */ SELECT 1")]
    [TestCase("-- DROP TABLE dbo.Users\r\nSELECT 1")]
    public void Validate_ReadOrScopedWriteSql_DoesNotThrow(string sql) {
        Assert.DoesNotThrow(() => SqlServerSqlGuard.Validate(sql));
    }

    [TestCase("DROP TABLE dbo.Users", "DROP TABLE")]
    [TestCase("DROP DATABASE AppDb", "DROP DATABASE")]
    [TestCase("DROP SCHEMA audit", "DROP SCHEMA")]
    [TestCase("TRUNCATE TABLE dbo.Users", "TRUNCATE")]
    public void Validate_ForbiddenOperation_ThrowsInvalidOperationException(
        string sql,
        string expectedMessage
    ) {
        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(() => SqlServerSqlGuard.Validate(sql))!;

        Assert.That(exception.Message, Does.Contain(expectedMessage));
    }

    [TestCase("DELETE FROM dbo.Users", "DELETE")]
    [TestCase("UPDATE dbo.Users SET DisplayName = @displayName", "UPDATE")]
    public void Validate_DeleteOrUpdateWithoutWhere_ThrowsInvalidOperationException(
        string sql,
        string expectedMessage
    ) {
        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(() => SqlServerSqlGuard.Validate(sql))!;

        Assert.That(exception.Message, Does.Contain(expectedMessage));
    }

    [TestCase("DELETE FROM dbo.Users WHERE 1 = 1")]
    [TestCase("UPDATE dbo.Users SET DisplayName = @displayName WHERE 'x' = 'x'")]
    [TestCase("UPDATE dbo.Users SET DisplayName = @displayName WHERE N'x' = N'x'")]
    [TestCase("DELETE FROM dbo.Users WHERE Id = @id OR 1 = 1")]
    [TestCase("DELETE FROM dbo.Users WHERE IsActive = 1 OR TRUE")]
    public void Validate_AlwaysTrueWhere_ThrowsInvalidOperationException(string sql) {
        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(() => SqlServerSqlGuard.Validate(sql))!;

        Assert.That(exception.Message, Does.Contain("Always-true WHERE"));
    }
}
