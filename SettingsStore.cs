using System.IO;
using System.Text.Json;

namespace BadgeFlow.Desktop;

public sealed class AppSettings
{
    public string BackupFolder { get; set; } = "";
    public string BackupName { get; set; } = "";
    public bool AutoBackup { get; set; } = true;
    public List<string> KnownSoftware { get; set; } = new();
}

public sealed class SettingsStore
{
    private readonly string _path;
    public SettingsStore()
    {
        var folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BadgeFlowDesktop");Directory.CreateDirectory(folder);_path=Path.Combine(folder,"settings.json");
    }
    public AppSettings Load(){try{return File.Exists(_path)?JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path))??new AppSettings():new AppSettings();}catch{return new AppSettings();}}
    public void Save(AppSettings settings)=>File.WriteAllText(_path,JsonSerializer.Serialize(settings,new JsonSerializerOptions{WriteIndented=true}));
}
