using Microsoft.Data.Sqlite;

namespace Umsatzschaetzung;

// Der Speicher selbst ist nicht zu erreichen: Datei gesperrt, Platte voll, Rechte fehlen.
public sealed class StoreUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class SchemaTooNewException(int found, int known)
    : Exception($"stammt aus einer neueren Programmversion (Schema {found}, unterstützt wird {known})");

// Der Index eines Schrittes ist die Version, auf die er hebt: Schritte werden angehängt,
// nie geändert. PRAGMA user_version steht im Dateikopf und wandert daher mit der Datei mit.
static class Schema
{
    public static int Version(SqliteConnection db)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "PRAGMA user_version";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public static void Migrate(SqliteConnection db, string[] steps)
    {
        var from = Version(db);
        if (from > steps.Length) throw new SchemaTooNewException(from, steps.Length);
        if (from == steps.Length) return;
        using var tx = db.BeginTransaction(deferred: false);
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        for (var i = from; i < steps.Length; i++)
        {
            cmd.CommandText = steps[i];
            cmd.ExecuteNonQuery();
        }
        cmd.CommandText = $"PRAGMA user_version = {steps.Length}";
        cmd.ExecuteNonQuery();
        tx.Commit();
    }
}
