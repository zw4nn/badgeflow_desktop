using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace BadgeFlow.Desktop;

public static class BadgeFlowExchange
{
    public static void Export(string path, AppData data)
    {
        var residenceIds=new Dictionary<Guid,long>();var residentIds=new Dictionary<Guid,long>();long nextResidence=1,nextResident=1,nextBadge=1;
        foreach(var r in data.Residences)residenceIds[r.Id]=nextResidence++;
        foreach(var r in data.Residences)foreach(var p in r.Residents)residentIds[p.Id]=nextResident++;
        var residences=data.Residences.Select(r=>new Dictionary<string,object?>{
            ["id"]=residenceIds[r.Id],["nom"]=r.Name,["adresse"]=r.Address,["ville"]=r.City,["codePostal"]=r.PostalCode,["gestionnaire"]=r.Manager,["technologies"]=r.Technologies,["modeGestion"]=r.ManagementMode,["logicielGestion"]=r.Software,["notesGestion"]=r.ManagementNotes,["dateCreation"]=ToMillis(r.CreatedAt)}).ToList();
        var residents=new List<Dictionary<string,object?>>();var badges=new List<Dictionary<string,object?>>();
        foreach(var r in data.Residences)
        {
            foreach(var p in r.Residents)
            {
                residents.Add(new Dictionary<string,object?>{{"id",residentIds[p.Id]},{"residenceId",residenceIds[r.Id]},{"nom",p.LastName},{"prenom",p.FirstName},{"telephone",p.Phone},{"mail",p.Email},{"batiment",p.Building},{"appartement",p.Apartment},{"etage",p.Floor},{"porte",p.Door},{"notes",p.Notes}});
                foreach(var b in p.Badges)badges.Add(ToBadge(nextBadge++,b,residentIds[p.Id],null));
            }
            foreach(var b in r.DirectBadges)badges.Add(ToBadge(nextBadge++,b,null,residenceIds[r.Id]));
        }
        var root=new Dictionary<string,object?>{{"formatVersion",1},{"residences",residences},{"residents",residents},{"badges",badges}};
        var meta=new Dictionary<string,object?>{{"formatVersion",1},{"application","BadgeFlow"},{"exportedAt",DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}};
        var options=new JsonSerializerOptions{WriteIndented=true};Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var fs=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.None);using var zip=new ZipArchive(fs,ZipArchiveMode.Create);WriteEntry(zip,"badgeflow-data.json",JsonSerializer.Serialize(root,options));WriteEntry(zip,"badgeflow-meta.json",JsonSerializer.Serialize(meta,options));
    }
    private static Dictionary<string,object?> ToBadge(long id,BadgeRecord b,long? residentId,long? residenceId)
    {
        var notes=b.Notes??"";if(b.Starprox&&!notes.Contains("[STARPROX]",StringComparison.OrdinalIgnoreCase))notes=string.IsNullOrWhiteSpace(notes)?"[STARPROX]":"[STARPROX] "+notes;
        return new Dictionary<string,object?>{{"id",id},{"residentId",residentId},{"residenceId",residenceId},{"technologie",NormalizeTechnology(b.Technology)},{"numeroAffiche",b.Number},{"hex",b.Hex},{"decimal",b.Decimal},{"trameBrute",""},{"dateLecture",ToMillis(b.ScannedAt)},{"dateCreation",ToMillis(b.CreatedAt)},{"dateModification",ToMillis(b.UpdatedAt)},{"libelle",b.Label},{"actif",true},{"notes",notes}};
    }
    public static AppData Import(string path)
    {
        using var fs=File.OpenRead(path);using var zip=new ZipArchive(fs,ZipArchiveMode.Read);var entry=zip.GetEntry("badgeflow-data.json")??throw new InvalidDataException("badgeflow-data.json est introuvable dans ce fichier.");using var reader=new StreamReader(entry.Open(),Encoding.UTF8);using var doc=JsonDocument.Parse(reader.ReadToEnd());var root=doc.RootElement;
        var result=new AppData();var residences=new Dictionary<long,Residence>();var residents=new Dictionary<long,Resident>();
        if(root.TryGetProperty("residences",out var rs))foreach(var x in rs.EnumerateArray()){long oldId=GetLong(x,"id");var r=new Residence{Name=GetString(x,"nom"),Address=GetString(x,"adresse"),City=GetString(x,"ville"),PostalCode=GetString(x,"codePostal"),Manager=GetString(x,"gestionnaire"),Technologies=GetString(x,"technologies"),ManagementMode=GetString(x,"modeGestion"),Software=GetString(x,"logicielGestion"),ManagementNotes=GetString(x,"notesGestion"),CreatedAt=FromMillis(GetLong(x,"dateCreation",DateTimeOffset.Now.ToUnixTimeMilliseconds()))};result.Residences.Add(r);residences[oldId]=r;}
        if(root.TryGetProperty("residents",out var ps))foreach(var x in ps.EnumerateArray()){long oldId=GetLong(x,"id"),rid=GetLong(x,"residenceId");if(!residences.TryGetValue(rid,out var res))continue;var p=new Resident{LastName=GetString(x,"nom"),FirstName=GetString(x,"prenom"),Phone=GetString(x,"telephone"),Email=GetString(x,"mail"),Building=GetString(x,"batiment"),Apartment=GetString(x,"appartement"),Floor=GetString(x,"etage"),Door=GetString(x,"porte"),Notes=GetString(x,"notes")};res.Residents.Add(p);residents[oldId]=p;}
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if(root.TryGetProperty("badges",out var bs))foreach(var x in bs.EnumerateArray())
        {
            string number=GetString(x,"numeroAffiche").Trim();if(number.Length>0&&!seen.Add(number))throw new InvalidDataException($"Le fichier contient le badge en doublon : {number}");string notes=GetString(x,"notes");long dc=GetLong(x,"dateCreation",GetLong(x,"dateLecture",DateTimeOffset.Now.ToUnixTimeMilliseconds()));
            var b=new BadgeRecord{Number=number,Hex=GetString(x,"hex"),Decimal=GetLong(x,"decimal"),Technology=NormalizeTechnology(GetString(x,"technologie")),Starprox=notes.Contains("[STARPROX]",StringComparison.OrdinalIgnoreCase),Label=GetString(x,"libelle"),Notes=notes.Replace("[STARPROX]","",StringComparison.OrdinalIgnoreCase).Trim(),ScannedAt=FromMillis(GetLong(x,"dateLecture",dc)),CreatedAt=FromMillis(dc),UpdatedAt=FromMillis(GetLong(x,"dateModification",dc))};
            long pid=GetLong(x,"residentId",-1),rid=GetLong(x,"residenceId",-1);if(pid>=0&&residents.TryGetValue(pid,out var p))p.Badges.Add(b);else if(rid>=0&&residences.TryGetValue(rid,out var r))r.DirectBadges.Add(b);
        }
        return result;
    }
    private static long ToMillis(DateTime d)=>new DateTimeOffset(d==default?DateTime.Now:d).ToUnixTimeMilliseconds();private static DateTime FromMillis(long v){try{return DateTimeOffset.FromUnixTimeMilliseconds(v).LocalDateTime;}catch{return DateTime.Now;}}
    private static void WriteEntry(ZipArchive zip,string name,string content){var e=zip.CreateEntry(name,CompressionLevel.Optimal);using var w=new StreamWriter(e.Open(),new UTF8Encoding(false));w.Write(content);}private static string GetString(JsonElement e,string name)=>e.TryGetProperty(name,out var p)&&p.ValueKind!=JsonValueKind.Null?p.ToString():"";private static long GetLong(JsonElement e,string name,long fallback=0)=>e.TryGetProperty(name,out var p)&&p.ValueKind!=JsonValueKind.Null&&p.TryGetInt64(out var v)?v:fallback;private static string NormalizeTechnology(string value)=>value.Trim().ToUpperInvariant() switch{"URMET"=>"URMET","HEXACT"=>"HEXACT","INTRATONE"=>"INTRATONE",_=>"AUTO"};
}
