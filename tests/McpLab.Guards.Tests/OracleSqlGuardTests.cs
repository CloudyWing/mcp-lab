using NUnit.Framework;

using OracleSqlGuard = CloudyWing.McpLab.Oracle.SqlGuard;

namespace CloudyWing.McpLab.Guards.Tests;

internal sealed class OracleSqlGuardTests {
    [TestCase("SELECT * FROM USERS WHERE ROWNUM = 1")]
    [TestCase("DELETE FROM USERS WHERE ID = :id")]
    [TestCase("UPDATE USERS SET DISPLAY_NAME = :displayName WHERE ID = :id")]
    [TestCase("INSERT INTO AUDIT_LOG(MESSAGE) VALUES ('DELETE requested')")]
    [TestCase("INSERT INTO AUDIT_LOG(MESSAGE) VALUES ('UPDATE requested')")]
    [TestCase("/* DROP TABLE USERS */ SELECT 1 FROM DUAL")]
    [TestCase("-- DROP TABLE USERS\r\nSELECT 1 FROM DUAL")]
    public void Validate_ReadOrScopedWriteSql_DoesNotThrow(string sql) {
        Assert.DoesNotThrow(() => OracleSqlGuard.Validate(sql));
    }

    [TestCase("DROP TABLE USERS", "DROP TABLE")]
    [TestCase("DROP USER APPUSER", "DROP USER")]
    [TestCase("DROP TABLESPACE APPDATA", "DROP TABLESPACE")]
    [TestCase("TRUNCATE TABLE USERS", "TRUNCATE")]
    public void Validate_ForbiddenOperation_ThrowsInvalidOperationException(
        string sql,
        string expectedMessage
    ) {
        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(() => OracleSqlGuard.Validate(sql))!;

        Assert.That(exception.Message, Does.Contain(expectedMessage));
    }

    [TestCase("DELETE FROM USERS", "DELETE")]
    [TestCase("UPDATE USERS SET DISPLAY_NAME = :displayName", "UPDATE")]
    public void Validate_DeleteOrUpdateWithoutWhere_ThrowsInvalidOperationException(
        string sql,
        string expectedMessage
    ) {
        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(() => OracleSqlGuard.Validate(sql))!;

        Assert.That(exception.Message, Does.Contain(expectedMessage));
    }

    [TestCase("DELETE FROM USERS WHERE 1 = 1")]
    [TestCase("UPDATE USERS SET DISPLAY_NAME = :displayName WHERE 'x' = 'x'")]
    [TestCase("DELETE FROM USERS WHERE ID = :id OR 1 = 1")]
    [TestCase("DELETE FROM USERS WHERE IS_ACTIVE = 1 OR TRUE")]
    public void Validate_AlwaysTrueWhere_ThrowsInvalidOperationException(string sql) {
        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(() => OracleSqlGuard.Validate(sql))!;

        Assert.That(exception.Message, Does.Contain("Always-true WHERE"));
    }

    [TestCase("SELECT 1 AS value FROM DUAL")]
    [TestCase("WITH cte AS (SELECT 1 AS value FROM DUAL) SELECT value FROM cte")]
    [TestCase("SELECT 'DELETE' AS keyword FROM DUAL")]
    [TestCase("SELECT \"UPDATE\" FROM AUDIT_LOG")]
    public void ValidateReadOnlyQuery_ReadOnlySql_DoesNotThrow(string sql) {
        Assert.DoesNotThrow(() => OracleSqlGuard.ValidateReadOnlyQuery(sql));
    }

    [TestCase("WITH cte AS (SELECT 1 AS id FROM DUAL) DELETE FROM USERS WHERE ID IN (SELECT id FROM cte)")]
    [TestCase("SELECT 1 FROM DUAL; DROP TABLE USERS")]
    [TestCase("BEGIN NULL; END;")]
    public void ValidateReadOnlyQuery_WriteOrCommandSql_ThrowsInvalidOperationException(string sql) {
        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(() => OracleSqlGuard.ValidateReadOnlyQuery(sql))!;

        Assert.That(exception.Message, Does.Contain("Only SELECT / WITH read queries"));
    }
}
