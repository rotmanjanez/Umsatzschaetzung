using Microsoft.Data.Sqlite;

namespace Umsatzschaetzung.Tests.Rulestore;

static class Sql
{
    public static SqliteConnection Open(string path)
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        db.Open();
        return db;
    }

    public static void Exec(string path, string sql)
    {
        using var db = Open(path);
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public static object? Scalar(string path, string sql)
    {
        using var db = Open(path);
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    public static long UserVersion(string path) => (long)Scalar(path, "PRAGMA user_version")!;
}
