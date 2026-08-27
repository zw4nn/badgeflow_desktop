using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace BadgeFlow.Desktop;

public static class BadgeFlowExchange
{
    public static void Export(string path, AppData data)
    {
        var residenceIds = new Dictionary<Guid,long>();
        var residentIds = new Dictionary<Guid,long>();
        long nextResidence = 1, nextResident = 1, nextBadge = 1;

        foreach (var r in data.Residences) residenceIds[r.Id] = nextResidence++;
        foreach (var r in data.Residences)
            foreach (var p in r.Residents) residentIds[p.Id] = nextResident++;

        var residences = data.Residences.Select(r => new Dictionary<string,object?>
        {
            ["id"] = residenceIds[r.Id], ["nom"] = r.Name, ["adresse"] = r.Address,
            ["ville"] = r.City, ["codePostal"] = r.PostalCode, ["technologies"] = r.Technologies,
            ["modeGestion"] = r.ManagementMode, ["logicielGestion"] = r.Software,
            ["notesGestion"] = r.ManagementNotes, ["dateCreation"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        }).ToList();

        var residents = new List<Dictionary<string,object?>>();
        var badges = new List<Dictionary<string,object?>>();
        foreach (var r in data.Residences)
        {
            foreach (var p in r.Residents)
            {
                residents.Add(new Dictionary<string,object?>
                {
                    ["id"] = residentIds[p.Id], ["residenceId"] = residenceIds[r.Id],
                    ["nom"] = p.LastName, ["prenom"] = p.FirstName, ["telephone"] = p.Phone,
                    ["mail"] = p.Email, ["batiment"] = p.Building, ["appartement"] = p.Apartment,
                    ["etage"] = p.Floor, ["porte"] = p.Door, ["notes"] = p.Notes
                });
                foreach (var b in p.Badges)
                {
                    var notes = b.Notes ?? "";
                    if (b.Starprox && !notes.Contains("[STARPROX]", StringComparison.OrdinalIgnoreCase))
                        notes = string.IsNullOrWhiteSpace(notes) ? "[STARPROX]" : "[STARPROX] " + notes;
                    badges.Add(new Dictionary<string,object?>
                    {
                        ["id"] = nextBadge++, ["residentId"] = residentIds[p.Id],
                        ["technologie"] = NormalizeTechnology(b.Technology), ["numeroAffiche"] = b.Number,
                        ["hex"] = b.Hex, ["decimal"] = b.Decimal, ["trameBrute"] = "",
                        ["dateLecture"] = new DateTimeOffset(b.ScannedAt).ToUnixTimeMilliseconds(),
                        ["dateCreation"] = new DateTimeOffset(b.ScannedAt).ToUnixTimeMilliseconds(),
                        ["actif"] = true, ["notes"] = notes
                    });
                }
            }
        }

        var root = new Dictionary<string,object?> { ["formatVersion"] = 1, ["residences"] = residences, ["residents"] = residents, ["badges"] = badges };
        var meta = new Dictionary<string,object?> { ["formatVersion"] = 1, ["application"] = "BadgeFlow", ["exportedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
        var options = new JsonSerializerOptions { WriteIndented = true };

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        WriteEntry(zip, "badgeflow-data.json", JsonSerializer.Serialize(root, options));
        WriteEntry(zip, "badgeflow-meta.json", JsonSerializer.Serialize(meta, options));
    }

    public static AppData Import(string path)
    {
        using var fs = File.OpenRead(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
        var entry = zip.GetEntry("badgeflow-data.json") ?? throw new InvalidDataException("badgeflow-data.json est introuvable dans ce fichier.");
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        using var doc = JsonDocument.Parse(reader.ReadToEnd());
        var root = doc.RootElement;
        var result = new AppData();
        var residences = new Dictionary<long,Residence>();
        var residents = new Dictionary<long,Resident>();

        if (root.TryGetProperty("residences", out var rs))
        foreach (var x in rs.EnumerateArray())
        {
            long oldId = GetLong(x,"id");
            var r = new Residence
            {
                Name=GetString(x,"nom"), Address=GetString(x,"adresse"), City=GetString(x,"ville"),
                PostalCode=GetString(x,"codePostal"), Technologies=GetString(x,"technologies"),
                ManagementMode=GetString(x,"modeGestion"), Software=GetString(x,"logicielGestion"),
                ManagementNotes=GetString(x,"notesGestion")
            };
            result.Residences.Add(r); residences[oldId]=r;
        }

        if (root.TryGetProperty("residents", out var ps))
        foreach (var x in ps.EnumerateArray())
        {
            long oldId=GetLong(x,"id"), residenceId=GetLong(x,"residenceId");
            if(!residences.TryGetValue(residenceId,out var residence)) continue;
            var p=new Resident
            {
                LastName=GetString(x,"nom"), FirstName=GetString(x,"prenom"), Phone=GetString(x,"telephone"),
                Email=GetString(x,"mail"), Building=GetString(x,"batiment"), Apartment=GetString(x,"appartement"),
                Floor=GetString(x,"etage"), Door=GetString(x,"porte"), Notes=GetString(x,"notes")
            };
            residence.Residents.Add(p); residents[oldId]=p;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("badges", out var bs))
        foreach (var x in bs.EnumerateArray())
        {
            long residentId=GetLong(x,"residentId"); if(!residents.TryGetValue(residentId,out var resident)) continue;
            string number=GetString(x,"numeroAffiche").Trim();
            if(number.Length>0 && !seen.Add(number)) throw new InvalidDataException($"Le fichier contient le badge en doublon : {number}");
            string notes=GetString(x,"notes"); long millis=GetLong(x,"dateLecture",DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            resident.Badges.Add(new BadgeRecord
            {
                Number=number, Hex=GetString(x,"hex"), Decimal=GetLong(x,"decimal"),
                Technology=NormalizeTechnology(GetString(x,"technologie")), Starprox=notes.Contains("[STARPROX]",StringComparison.OrdinalIgnoreCase),
                Notes=notes.Replace("[STARPROX]", "", StringComparison.OrdinalIgnoreCase).Trim(),
                ScannedAt=DateTimeOffset.FromUnixTimeMilliseconds(millis).LocalDateTime
            });
        }
        return result;
    }

    private static void WriteEntry(ZipArchive zip,string name,string content){var e=zip.CreateEntry(name,CompressionLevel.Optimal);using var w=new StreamWriter(e.Open(),new UTF8Encoding(false));w.Write(content);}
    private static string GetString(JsonElement e,string name)=>e.TryGetProperty(name,out var p)&&p.ValueKind!=JsonValueKind.Null?p.ToString():"";
    private static long GetLong(JsonElement e,string name,long fallback=0)=>e.TryGetProperty(name,out var p)&&p.TryGetInt64(out var v)?v:fallback;
    private static string NormalizeTechnology(string value)=>value.Trim().ToUpperInvariant() switch { "URMET"=>"URMET", "HEXACT"=>"HEXACT", "INTRATONE"=>"INTRATONE", _=>"AUTO" };
}
