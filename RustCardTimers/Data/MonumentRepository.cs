using Microsoft.Data.Sqlite;

namespace RustCardTimers.Data;

public sealed class MonumentRepository
{
    private readonly string _cs;

    public MonumentRepository(IConfiguration config)
    {
        _cs = config.GetConnectionString("Sqlite") ?? "Data Source=cardtimers.db";
        Initialize();
    }

    private void Initialize()
    {
        using var con = new SqliteConnection(_cs);
        con.Open();

        using var cmd = con.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS monuments(
  id   INTEGER PRIMARY KEY AUTOINCREMENT,
  name TEXT NOT NULL UNIQUE
);

CREATE TABLE IF NOT EXISTS monument_state(
  monument_id     INTEGER PRIMARY KEY,
  last_swipe_utc  TEXT NULL,
  last_player     TEXT NULL,
  last_message_id TEXT NULL,
  FOREIGN KEY(monument_id) REFERENCES monuments(id)
);

CREATE TABLE IF NOT EXISTS server_state(
  id INTEGER PRIMARY KEY CHECK(id=1),
  last_reset_utc TEXT NOT NULL
);

INSERT INTO server_state(id, last_reset_utc)
VALUES (1, '1970-01-01T00:00:00Z')
ON CONFLICT(id) DO NOTHING;
";
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var con = new SqliteConnection(_cs);
        con.Open();
        return con;
    }

    public DateTime GetLastResetUtc()
    {
        using var con = Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT last_reset_utc FROM server_state WHERE id=1;";
        var s = (string)cmd.ExecuteScalar()!;
        return DateTime.Parse(s, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
    }

    public void SetLastResetUtc(DateTime utcNow)
    {
        using var con = Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "UPDATE server_state SET last_reset_utc = $t WHERE id=1;";
        cmd.Parameters.AddWithValue("$t", utcNow.ToUniversalTime().ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public void UpsertLastSwipe(string monumentName, DateTime eventUtc, string player, string messageId)
    {
        using var con = Open();
        using var tx = con.BeginTransaction();

        // Ensure monument exists
        long monumentId;
        using (var cmd = con.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO monuments(name) VALUES ($n) ON CONFLICT(name) DO NOTHING;";
            cmd.Parameters.AddWithValue("$n", monumentName);
            cmd.ExecuteNonQuery();
        }

        using (var cmd = con.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT id FROM monuments WHERE name = $n;";
            cmd.Parameters.AddWithValue("$n", monumentName);
            monumentId = (long)cmd.ExecuteScalar()!;
        }

        // Dedupe exact same message id (optional but useful)
        using (var cmd = con.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT last_message_id FROM monument_state WHERE monument_id = $id;";
            cmd.Parameters.AddWithValue("$id", monumentId);
            var existing = cmd.ExecuteScalar() as string;
            if (!string.IsNullOrWhiteSpace(existing) && existing == messageId)
            {
                tx.Rollback();
                return;
            }
        }

        // Rule A: last swipe wins (overwrite)
        using (var cmd = con.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO monument_state(monument_id, last_swipe_utc, last_player, last_message_id)
VALUES ($id, $t, $p, $m)
ON CONFLICT(monument_id) DO UPDATE SET
  last_swipe_utc = excluded.last_swipe_utc,
  last_player = excluded.last_player,
  last_message_id = excluded.last_message_id;
";
            cmd.Parameters.AddWithValue("$id", monumentId);
            cmd.Parameters.AddWithValue("$t", eventUtc.ToUniversalTime().ToString("O"));
            cmd.Parameters.AddWithValue("$p", player);
            cmd.Parameters.AddWithValue("$m", messageId);
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public IReadOnlyList<MonumentRow> GetAllMonuments()
    {
        using var con = Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"
SELECT m.name, s.last_swipe_utc, s.last_player
FROM monuments m
LEFT JOIN monument_state s ON s.monument_id = m.id
ORDER BY m.name ASC;";
        using var r = cmd.ExecuteReader();

        var list = new List<MonumentRow>();
        while (r.Read())
        {
            var name = r.GetString(0);

            DateTime? lastSwipeUtc = null;
            if (!r.IsDBNull(1))
            {
                var t = r.GetString(1);
                lastSwipeUtc = DateTime.Parse(t, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
            }

            var player = r.IsDBNull(2) ? null : r.GetString(2);

            list.Add(new MonumentRow(name, lastSwipeUtc, player));
        }

        return list;
    }
}

public sealed record MonumentRow(string Name, DateTime? LastSwipeUtc, string? LastPlayer);
