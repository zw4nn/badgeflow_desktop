using System.IO;
using Microsoft.Data.Sqlite;

namespace BadgeFlow.Desktop;

public sealed class DataStore
{
    private readonly string _folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BadgeFlowDesktop");
    public string DatabasePath => Path.Combine(_folder, "badgeflow-desktop.db");
    public DataStore(){Directory.CreateDirectory(_folder);Initialize();}
    private SqliteConnection Open(){var c=new SqliteConnection($"Data Source={DatabasePath}");c.Open();using var cmd=c.CreateCommand();cmd.CommandText="PRAGMA foreign_keys=ON;";cmd.ExecuteNonQuery();return c;}

    private void Initialize()
    {
        using var c=Open(); using(var cmd=c.CreateCommand()){cmd.CommandText="""
        CREATE TABLE IF NOT EXISTS residences(id TEXT PRIMARY KEY,name TEXT NOT NULL,address TEXT NOT NULL DEFAULT '',postal_code TEXT NOT NULL DEFAULT '',city TEXT NOT NULL DEFAULT '',manager TEXT NOT NULL DEFAULT '',technologies TEXT NOT NULL DEFAULT '',management_mode TEXT NOT NULL DEFAULT '',software TEXT NOT NULL DEFAULT '',management_notes TEXT NOT NULL DEFAULT '',created_at TEXT NOT NULL DEFAULT '');
        CREATE TABLE IF NOT EXISTS residents(id TEXT PRIMARY KEY,residence_id TEXT NOT NULL,last_name TEXT NOT NULL DEFAULT '',first_name TEXT NOT NULL DEFAULT '',phone TEXT NOT NULL DEFAULT '',email TEXT NOT NULL DEFAULT '',building TEXT NOT NULL DEFAULT '',apartment TEXT NOT NULL DEFAULT '',floor TEXT NOT NULL DEFAULT '',door TEXT NOT NULL DEFAULT '',notes TEXT NOT NULL DEFAULT '',FOREIGN KEY(residence_id) REFERENCES residences(id) ON DELETE CASCADE);
        """;cmd.ExecuteNonQuery();}
        EnsureColumn(c,"residences","manager","TEXT NOT NULL DEFAULT ''"); EnsureColumn(c,"residences","created_at","TEXT NOT NULL DEFAULT ''");
        EnsureBadgeSchema(c);
    }
    private static void EnsureColumn(SqliteConnection c,string table,string column,string type){using var q=c.CreateCommand();q.CommandText=$"PRAGMA table_info({table});";using var r=q.ExecuteReader();while(r.Read())if(r.GetString(1).Equals(column,StringComparison.OrdinalIgnoreCase))return;using var a=c.CreateCommand();a.CommandText=$"ALTER TABLE {table} ADD COLUMN {column} {type};";a.ExecuteNonQuery();}
    private static void EnsureBadgeSchema(SqliteConnection c)
    {
        bool exists=false, hasResidence=false, residentNotNull=false, hasLabel=false;
        using(var q=c.CreateCommand()){q.CommandText="PRAGMA table_info(badges);";using var r=q.ExecuteReader();while(r.Read()){exists=true;var n=r.GetString(1);if(n.Equals("residence_id",StringComparison.OrdinalIgnoreCase))hasResidence=true;if(n.Equals("resident_id",StringComparison.OrdinalIgnoreCase))residentNotNull=r.GetInt32(3)!=0;if(n.Equals("label",StringComparison.OrdinalIgnoreCase))hasLabel=true;}}
        if(!exists){CreateBadges(c);return;}
        if(hasResidence&&!residentNotNull&&hasLabel){EnsureColumn(c,"badges","created_at","TEXT NOT NULL DEFAULT ''");EnsureColumn(c,"badges","updated_at","TEXT NOT NULL DEFAULT ''");return;}
        using(var cmd=c.CreateCommand()){cmd.CommandText="ALTER TABLE badges RENAME TO badges_old;";cmd.ExecuteNonQuery();}
        CreateBadges(c);
        var cols=new HashSet<string>(StringComparer.OrdinalIgnoreCase);using(var q=c.CreateCommand()){q.CommandText="PRAGMA table_info(badges_old);";using var r=q.ExecuteReader();while(r.Read())cols.Add(r.GetString(1));}
        string Col(string name,string fallback)=>cols.Contains(name)?name:fallback;
        var now=DateTime.Now.ToString("O");
        using(var cmd=c.CreateCommand()){cmd.CommandText=$"INSERT OR IGNORE INTO badges(id,resident_id,residence_id,number,hex,decimal_value,technology,starprox,label,notes,scanned_at,created_at,updated_at) SELECT id,resident_id,NULL,number,hex,decimal_value,technology,starprox,{Col("label","''")},notes,scanned_at,{Col("created_at","scanned_at")},{Col("updated_at","scanned_at")} FROM badges_old; DROP TABLE badges_old; CREATE UNIQUE INDEX IF NOT EXISTS idx_badge_number ON badges(number COLLATE NOCASE);";cmd.ExecuteNonQuery();}
    }
    private static void CreateBadges(SqliteConnection c){using var cmd=c.CreateCommand();cmd.CommandText="""
      CREATE TABLE IF NOT EXISTS badges(id TEXT PRIMARY KEY,resident_id TEXT NULL,residence_id TEXT NULL,number TEXT NOT NULL COLLATE NOCASE,hex TEXT NOT NULL DEFAULT '',decimal_value INTEGER NOT NULL DEFAULT 0,technology TEXT NOT NULL DEFAULT 'AUTO',starprox INTEGER NOT NULL DEFAULT 0,label TEXT NOT NULL DEFAULT '',notes TEXT NOT NULL DEFAULT '',scanned_at TEXT NOT NULL,created_at TEXT NOT NULL,updated_at TEXT NOT NULL,FOREIGN KEY(resident_id) REFERENCES residents(id) ON DELETE CASCADE,FOREIGN KEY(residence_id) REFERENCES residences(id) ON DELETE CASCADE);
      CREATE UNIQUE INDEX IF NOT EXISTS idx_badge_number ON badges(number COLLATE NOCASE);
      """;cmd.ExecuteNonQuery();}

    public AppData Load()
    {
        var data=new AppData();var residences=new Dictionary<Guid,Residence>();var residents=new Dictionary<Guid,Resident>();using var c=Open();
        using(var cmd=c.CreateCommand()){cmd.CommandText="SELECT id,name,address,postal_code,city,manager,technologies,management_mode,software,management_notes,created_at FROM residences ORDER BY name;";using var r=cmd.ExecuteReader();while(r.Read()){var x=new Residence{Id=Guid.Parse(r.GetString(0)),Name=r.GetString(1),Address=r.GetString(2),PostalCode=r.GetString(3),City=r.GetString(4),Manager=r.GetString(5),Technologies=r.GetString(6),ManagementMode=r.GetString(7),Software=r.GetString(8),ManagementNotes=r.GetString(9),CreatedAt=ParseDate(r.GetString(10))};data.Residences.Add(x);residences[x.Id]=x;}}
        using(var cmd=c.CreateCommand()){cmd.CommandText="SELECT id,residence_id,last_name,first_name,phone,email,building,apartment,floor,door,notes FROM residents ORDER BY last_name,first_name;";using var r=cmd.ExecuteReader();while(r.Read()){var x=new Resident{Id=Guid.Parse(r.GetString(0)),LastName=r.GetString(2),FirstName=r.GetString(3),Phone=r.GetString(4),Email=r.GetString(5),Building=r.GetString(6),Apartment=r.GetString(7),Floor=r.GetString(8),Door=r.GetString(9),Notes=r.GetString(10)};var rid=Guid.Parse(r.GetString(1));if(residences.TryGetValue(rid,out var res)){res.Residents.Add(x);residents[x.Id]=x;}}}
        using(var cmd=c.CreateCommand()){cmd.CommandText="SELECT id,resident_id,residence_id,number,hex,decimal_value,technology,starprox,label,notes,scanned_at,created_at,updated_at FROM badges ORDER BY created_at;";using var r=cmd.ExecuteReader();while(r.Read()){var b=new BadgeRecord{Id=Guid.Parse(r.GetString(0)),Number=r.GetString(3),Hex=r.GetString(4),Decimal=r.GetInt64(5),Technology=r.GetString(6),Starprox=r.GetInt32(7)!=0,Label=r.GetString(8),Notes=r.GetString(9),ScannedAt=ParseDate(r.GetString(10)),CreatedAt=ParseDate(r.GetString(11)),UpdatedAt=ParseDate(r.GetString(12))};if(!r.IsDBNull(1)&&Guid.TryParse(r.GetString(1),out var pid)&&residents.TryGetValue(pid,out var p))p.Badges.Add(b);else if(!r.IsDBNull(2)&&Guid.TryParse(r.GetString(2),out var rid)&&residences.TryGetValue(rid,out var res))res.DirectBadges.Add(b);}}
        return data;
    }
    public void Save(AppData data)
    {
        using var c=Open();using var tx=c.BeginTransaction();Exec(c,tx,"DELETE FROM badges; DELETE FROM residents; DELETE FROM residences;");
        foreach(var res in data.Residences){using(var cmd=c.CreateCommand()){cmd.Transaction=tx;cmd.CommandText="INSERT INTO residences(id,name,address,postal_code,city,manager,technologies,management_mode,software,management_notes,created_at) VALUES($id,$n,$a,$p,$c,$g,$t,$m,$s,$mn,$ca);";cmd.Parameters.AddWithValue("$id",res.Id.ToString());cmd.Parameters.AddWithValue("$n",res.Name);cmd.Parameters.AddWithValue("$a",res.Address);cmd.Parameters.AddWithValue("$p",res.PostalCode);cmd.Parameters.AddWithValue("$c",res.City);cmd.Parameters.AddWithValue("$g",res.Manager);cmd.Parameters.AddWithValue("$t",res.Technologies);cmd.Parameters.AddWithValue("$m",res.ManagementMode);cmd.Parameters.AddWithValue("$s",res.Software);cmd.Parameters.AddWithValue("$mn",res.ManagementNotes);cmd.Parameters.AddWithValue("$ca",res.CreatedAt.ToString("O"));cmd.ExecuteNonQuery();}
          foreach(var p in res.Residents){using(var cmd=c.CreateCommand()){cmd.Transaction=tx;cmd.CommandText="INSERT INTO residents VALUES($id,$rid,$ln,$fn,$ph,$em,$b,$ap,$fl,$d,$no);";cmd.Parameters.AddWithValue("$id",p.Id.ToString());cmd.Parameters.AddWithValue("$rid",res.Id.ToString());cmd.Parameters.AddWithValue("$ln",p.LastName);cmd.Parameters.AddWithValue("$fn",p.FirstName);cmd.Parameters.AddWithValue("$ph",p.Phone);cmd.Parameters.AddWithValue("$em",p.Email);cmd.Parameters.AddWithValue("$b",p.Building);cmd.Parameters.AddWithValue("$ap",p.Apartment);cmd.Parameters.AddWithValue("$fl",p.Floor);cmd.Parameters.AddWithValue("$d",p.Door);cmd.Parameters.AddWithValue("$no",p.Notes);cmd.ExecuteNonQuery();}foreach(var b in p.Badges)InsertBadge(c,tx,b,p.Id,null);}
          foreach(var b in res.DirectBadges)InsertBadge(c,tx,b,null,res.Id);
        }tx.Commit();
    }
    private static void InsertBadge(SqliteConnection c,SqliteTransaction tx,BadgeRecord b,Guid? residentId,Guid? residenceId){using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="INSERT INTO badges VALUES($id,$pid,$rid,$n,$h,$dec,$t,$s,$l,$no,$scan,$ca,$ua);";cmd.Parameters.AddWithValue("$id",b.Id.ToString());cmd.Parameters.AddWithValue("$pid",(object?)residentId?.ToString()??DBNull.Value);cmd.Parameters.AddWithValue("$rid",(object?)residenceId?.ToString()??DBNull.Value);cmd.Parameters.AddWithValue("$n",b.Number);cmd.Parameters.AddWithValue("$h",b.Hex);cmd.Parameters.AddWithValue("$dec",b.Decimal);cmd.Parameters.AddWithValue("$t",b.Technology);cmd.Parameters.AddWithValue("$s",b.Starprox?1:0);cmd.Parameters.AddWithValue("$l",b.Label);cmd.Parameters.AddWithValue("$no",b.Notes);cmd.Parameters.AddWithValue("$scan",b.ScannedAt.ToString("O"));cmd.Parameters.AddWithValue("$ca",b.CreatedAt.ToString("O"));cmd.Parameters.AddWithValue("$ua",b.UpdatedAt.ToString("O"));cmd.ExecuteNonQuery();}
    private static DateTime ParseDate(string s)=>DateTime.TryParse(s,out var d)?d:DateTime.Now;
    private static void Exec(SqliteConnection c,SqliteTransaction tx,string sql){using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText=sql;cmd.ExecuteNonQuery();}
    public void BackupTo(string path){Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);File.Copy(DatabasePath,path,true);} public void RestoreFrom(string path){File.Copy(path,DatabasePath,true);Initialize();}
}
