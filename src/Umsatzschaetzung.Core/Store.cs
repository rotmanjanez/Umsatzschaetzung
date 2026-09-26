using System.Reflection;
using Microsoft.Data.Sqlite;

namespace Umsatzschaetzung;

// Der Speicher selbst ist nicht zu erreichen: Datei gesperrt, Platte voll, Rechte fehlen.
public sealed class StoreUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class SchemaTooNewException(int found, int known)
    : Exception($"stammt von einer neueren Version der Software. (Schema {found}, diese Version kennt bis Schema {known})");

// Der Index eines Schrittes ist die Version, auf die er hebt: Schritte werden angehängt,
// nie geändert. PRAGMA user_version steht im Dateikopf und wandert daher mit der Datei mit.
static class Schema
{
    // Steht in jeder Datei, die das Programm schreibt: jede Rückfrage des Supports will es wissen.
    public static string App { get; } =
        typeof(Schema).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";

    public static int Version(SqliteConnection db)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "PRAGMA user_version";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public static void Migrate(SqliteConnection db, string[] steps)
    {
        if (Known(db, steps) == steps.Length) return;
        using var tx = db.BeginTransaction(deferred: false);
        var from = Known(db, steps);
        if (from == steps.Length) return;
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

    // Der Stand wird noch einmal unter der Schreibsperre gelesen: sonst gehen zwei
    // gleichzeitig startende Prozesse von derselben Fassung aus und der zweite
    // wiederholte Schritte, die der erste schon angewandt hat.
    static int Known(SqliteConnection db, string[] steps)
    {
        var found = Version(db);
        if (found > steps.Length) throw new SchemaTooNewException(found, steps.Length);
        return found;
    }
}
