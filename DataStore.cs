using System.IO;
using Microsoft.Data.Sqlite;

namespace BadgeFlow.Desktop;

public sealed class DataStore
{
    private readonly string _folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BadgeFlowDesktop");
    public string DatabasePath => Path.Combine(_folder, "badgeflow-desktop.db");

    public DataStore() { Directory.CreateDirectory(_folder); Initialize(); }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection($"Data Source={DatabasePath}"); c.Open();
        using var cmd = c.CreateCommand(); cmd.CommandText = "PRAGMA foreign_keys=ON;"; cmd.ExecuteNonQuery(); return c;
    }

    private void Initialize()
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = """
        CREATE TABLE IF NOT EXISTS residences(
          id TEXT PRIMARY KEY, name TEXT NOT NULL, address TEXT NOT NULL DEFAULT '', postal_code TEXT NOT NULL DEFAULT '', city TEXT NOT NULL DEFAULT '',
          technologies TEXT NOT NULL DEFAULT '', management_mode TEXT NOT NULL DEFAULT '', software TEXT NOT NULL DEFAULT '', management_notes TEXT NOT NULL DEFAULT '');
        CREATE TABLE IF NOT EXISTS residents(
          id TEXT PRIMARY KEY, residence_id TEXT NOT NULL, last_name TEXT NOT NULL DEFAULT '', first_name TEXT NOT NULL DEFAULT '',
          phone TEXT NOT NULL DEFAULT '', email TEXT NOT NULL DEFAULT '', building TEXT NOT NULL DEFAULT '', apartment TEXT NOT NULL DEFAULT '',
          floor TEXT NOT NULL DEFAULT '', door TEXT NOT NULL DEFAULT '', notes TEXT NOT NULL DEFAULT '',
          FOREIGN KEY(residence_id) REFERENCES residences(id) ON DELETE CASCADE);
        CREATE TABLE IF NOT EXISTS badges(
          id TEXT PRIMARY KEY, resident_id TEXT NOT NULL, number TEXT NOT NULL COLLATE NOCASE, hex TEXT NOT NULL DEFAULT '', decimal_value INTEGER NOT NULL DEFAULT 0,
          technology TEXT NOT NULL DEFAULT 'AUTO', starprox INTEGER NOT NULL DEFAULT 0, notes TEXT NOT NULL DEFAULT '', scanned_at TEXT NOT NULL,
          FOREIGN KEY(resident_id) REFERENCES residents(id) ON DELETE CASCADE);
        CREATE UNIQUE INDEX IF NOT EXISTS idx_badge_number ON badges(number COLLATE NOCASE);
        """;
        cmd.ExecuteNonQuery();
        EnsureColumn(c, "residences", "technologies", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(c, "residences", "management_mode", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(c, "residences", "software", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(c, "residences", "management_notes", "TEXT NOT NULL DEFAULT ''");
    }

    private static void EnsureColumn(SqliteConnection c, string table, string column, string type)
    {
        using var check = c.CreateCommand(); check.CommandText = $"PRAGMA table_info({table});"; using var r = check.ExecuteReader();
        while (r.Read()) if (r.GetString(1).Equals(column, StringComparison.OrdinalIgnoreCase)) return;
        using var alter = c.CreateCommand(); alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type};"; alter.ExecuteNonQuery();
    }

    public AppData Load()
    {
        var data = new AppData(); var residences = new Dictionary<Guid, Residence>(); var residents = new Dictionary<Guid, Resident>(); using var c = Open();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT id,name,address,postal_code,city,technologies,management_mode,software,management_notes FROM residences ORDER BY name;";
            using var r = cmd.ExecuteReader(); while (r.Read())
            {
                var x = new Residence { Id=Guid.Parse(r.GetString(0)), Name=r.GetString(1), Address=r.GetString(2), PostalCode=r.GetString(3), City=r.GetString(4), Technologies=r.GetString(5), ManagementMode=r.GetString(6), Software=r.GetString(7), ManagementNotes=r.GetString(8) };
                data.Residences.Add(x); residences[x.Id]=x;
            }
        }
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT id,residence_id,last_name,first_name,phone,email,building,apartment,floor,door,notes FROM residents ORDER BY last_name,first_name;";
            using var r=cmd.ExecuteReader(); while(r.Read())
            {
                var x=new Resident{Id=Guid.Parse(r.GetString(0)),LastName=r.GetString(2),FirstName=r.GetString(3),Phone=r.GetString(4),Email=r.GetString(5),Building=r.GetString(6),Apartment=r.GetString(7),Floor=r.GetString(8),Door=r.GetString(9),Notes=r.GetString(10)};
                var rid=Guid.Parse(r.GetString(1)); if(residences.TryGetValue(rid,out var res)){res.Residents.Add(x);residents[x.Id]=x;}
            }
        }
        using (var cmd=c.CreateCommand())
        {
            cmd.CommandText="SELECT id,resident_id,number,hex,decimal_value,technology,starprox,notes,scanned_at FROM badges ORDER BY scanned_at;"; using var r=cmd.ExecuteReader();
            while(r.Read()) { var id=Guid.Parse(r.GetString(1)); if(!residents.TryGetValue(id,out var resident))continue; resident.Badges.Add(new BadgeRecord{Id=Guid.Parse(r.GetString(0)),Number=r.GetString(2),Hex=r.GetString(3),Decimal=r.GetInt64(4),Technology=r.GetString(5),Starprox=r.GetInt32(6)!=0,Notes=r.GetString(7),ScannedAt=DateTime.TryParse(r.GetString(8),out var d)?d:DateTime.Now}); }
        }
        return data;
    }

    public void Save(AppData data)
    {
        using var c=Open(); using var tx=c.BeginTransaction(); Exec(c,tx,"DELETE FROM badges; DELETE FROM residents; DELETE FROM residences;");
        foreach(var residence in data.Residences)
        {
            using(var cmd=c.CreateCommand()) { cmd.Transaction=tx; cmd.CommandText="INSERT INTO residences(id,name,address,postal_code,city,technologies,management_mode,software,management_notes) VALUES($id,$n,$a,$p,$c,$t,$m,$s,$mn);"; cmd.Parameters.AddWithValue("$id",residence.Id.ToString());cmd.Parameters.AddWithValue("$n",residence.Name);cmd.Parameters.AddWithValue("$a",residence.Address);cmd.Parameters.AddWithValue("$p",residence.PostalCode);cmd.Parameters.AddWithValue("$c",residence.City);cmd.Parameters.AddWithValue("$t",residence.Technologies);cmd.Parameters.AddWithValue("$m",residence.ManagementMode);cmd.Parameters.AddWithValue("$s",residence.Software);cmd.Parameters.AddWithValue("$mn",residence.ManagementNotes);cmd.ExecuteNonQuery(); }
            foreach(var resident in residence.Residents)
            {
                using(var cmd=c.CreateCommand()){cmd.Transaction=tx;cmd.CommandText="INSERT INTO residents VALUES($id,$rid,$ln,$fn,$ph,$em,$b,$ap,$fl,$d,$no);";cmd.Parameters.AddWithValue("$id",resident.Id.ToString());cmd.Parameters.AddWithValue("$rid",residence.Id.ToString());cmd.Parameters.AddWithValue("$ln",resident.LastName);cmd.Parameters.AddWithValue("$fn",resident.FirstName);cmd.Parameters.AddWithValue("$ph",resident.Phone);cmd.Parameters.AddWithValue("$em",resident.Email);cmd.Parameters.AddWithValue("$b",resident.Building);cmd.Parameters.AddWithValue("$ap",resident.Apartment);cmd.Parameters.AddWithValue("$fl",resident.Floor);cmd.Parameters.AddWithValue("$d",resident.Door);cmd.Parameters.AddWithValue("$no",resident.Notes);cmd.ExecuteNonQuery();}
                foreach(var badge in resident.Badges){using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="INSERT INTO badges VALUES($id,$rid,$n,$h,$dec,$t,$s,$no,$date);";cmd.Parameters.AddWithValue("$id",badge.Id.ToString());cmd.Parameters.AddWithValue("$rid",resident.Id.ToString());cmd.Parameters.AddWithValue("$n",badge.Number);cmd.Parameters.AddWithValue("$h",badge.Hex);cmd.Parameters.AddWithValue("$dec",badge.Decimal);cmd.Parameters.AddWithValue("$t",badge.Technology);cmd.Parameters.AddWithValue("$s",badge.Starprox?1:0);cmd.Parameters.AddWithValue("$no",badge.Notes);cmd.Parameters.AddWithValue("$date",badge.ScannedAt.ToString("O"));cmd.ExecuteNonQuery();}
            }
        }
        tx.Commit();
    }

    public void BackupTo(string destination) { Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(DatabasePath,destination,true); }
    public void RestoreFrom(string source) { File.Copy(source,DatabasePath,true); Initialize(); }
    private static void Exec(SqliteConnection c,SqliteTransaction tx,string sql){using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText=sql;cmd.ExecuteNonQuery();}
}
