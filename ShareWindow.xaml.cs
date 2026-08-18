using Microsoft.Win32;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace BadgeFlow.Desktop;

public partial class ShareWindow : Window
{
    private readonly Residence _residence;
    private readonly (string Key, string Title, bool ResidentLevel)[] _columns =
    {
        ("residence","Residence",true),("ville","Ville",true),("batiment","Batiment",true),("appartement","Appartement",true),
        ("etage","Etage",true),("porte","Porte",true),("nom","Nom",true),("prenom","Prenom",true),("telephone","Telephone",true),
        ("email","Email",true),("technologie","Technologie",false),("numero","Numero badge",false),("hex","Hex",false),("decimal","Decimal",false),
        ("starprox","Starprox",false),("notes","Notes badge",false)
    };

    public ShareWindow(Residence residence)
    {
        InitializeComponent(); _residence = residence; ResidenceText.Text = residence.ToString();
        foreach (var resident in residence.Residents.OrderBy(r => r.LastName).ThenBy(r => r.FirstName))
        {
            ResidentsPanel.Children.Add(new CheckBox { Content = $"{resident.DisplayName}   ({resident.Badges.Count} badge(s))", Tag = resident, IsChecked = true, Margin = new Thickness(0,4,0,4) });
        }
        var defaults = new HashSet<string>(StringComparer.OrdinalIgnoreCase){"residence","batiment","appartement","nom","prenom","technologie","numero","starprox"};
        foreach (var c in _columns) ColumnsPanel.Children.Add(new CheckBox { Content = c.Title, Tag = c.Key, IsChecked = defaults.Contains(c.Key), Margin = new Thickness(0,4,0,4) });
    }

    private List<Resident> SelectedResidents() => ResidentsPanel.Children.OfType<CheckBox>().Where(x => x.IsChecked == true).Select(x => (Resident)x.Tag).ToList();
    private List<(string Key,string Title,bool ResidentLevel)> SelectedColumns()
    {
        var selected = ColumnsPanel.Children.OfType<CheckBox>().Where(x => x.IsChecked == true).Select(x => (string)x.Tag).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return _columns.Where(c => selected.Contains(c.Key)).ToList();
    }

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        var residents = SelectedResidents(); var cols = SelectedColumns();
        if (residents.Count == 0 || cols.Count == 0) { MessageBox.Show("Sélectionnez au moins un résident et une colonne."); return; }
        var dialog = new SaveFileDialog { Filter = "CSV compatible Excel (*.csv)|*.csv", FileName = $"badgeflow-{SafeName(Ascii(_residence.Name))}.csv" };
        if (dialog.ShowDialog() != true) return;
        File.WriteAllText(dialog.FileName, BuildCsv(residents, cols), new UTF8Encoding(false));
        MessageBox.Show("Tableau CSV exporté.", "BadgeFlow", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CopyText_Click(object sender, RoutedEventArgs e)
    {
        var residents = SelectedResidents(); if (residents.Count == 0) { MessageBox.Show("Sélectionnez au moins un résident."); return; }
        var b = new StringBuilder(); b.AppendLine($"Résidence : {_residence.Name}"); if (!string.IsNullOrWhiteSpace(_residence.City)) b.AppendLine($"Ville : {_residence.City}");
        foreach (var r in residents)
        {
            b.AppendLine(); b.AppendLine($"Résident : {r.DisplayName}"); if (!string.IsNullOrWhiteSpace(r.Building)) b.AppendLine($"Bâtiment : {r.Building}"); if (!string.IsNullOrWhiteSpace(r.Apartment)) b.AppendLine($"Appartement : {r.Apartment}");
            if (r.Badges.Count == 0) b.AppendLine("Badge : aucun badge associé");
            foreach (var badge in r.Badges) { b.AppendLine(); if (badge.Starprox) b.AppendLine("ATTENTION BADGE STARPROX"); b.AppendLine($"Technologie : {badge.Technology}"); b.AppendLine($"Numéro : {badge.Number}"); }
        }
        Clipboard.SetText(b.ToString().Trim()); MessageBox.Show("Texte copié dans le presse-papiers.");
    }

    private string BuildCsv(List<Resident> residents, List<(string Key,string Title,bool ResidentLevel)> cols)
    {
        var b = new StringBuilder(); b.AppendLine(string.Join(";", cols.Select(c => Csv(c.Title))));
        foreach (var r in residents)
        {
            var badgeRows = r.Badges.Count == 0 ? new BadgeRecord?[] { null } : r.Badges.Cast<BadgeRecord?>().ToArray();
            for (int i=0;i<badgeRows.Length;i++)
            {
                var badge = badgeRows[i]; bool first = i == 0;
                b.AppendLine(string.Join(";", cols.Select(c => Csv(Ascii(c.ResidentLevel && !first ? "" : Value(c.Key,r,badge))))));
            }
        }
        return b.ToString();
    }

    private string Value(string key, Resident r, BadgeRecord? b) => key switch
    {
        "residence" => _residence.Name, "ville" => _residence.City, "batiment" => r.Building, "appartement" => r.Apartment, "etage" => r.Floor, "porte" => r.Door,
        "nom" => r.LastName, "prenom" => r.FirstName, "telephone" => r.Phone, "email" => r.Email, "technologie" => b?.Technology ?? "", "numero" => b?.Number ?? "",
        "hex" => b?.Hex ?? "", "decimal" => b?.Decimal.ToString(CultureInfo.InvariantCulture) ?? "", "starprox" => b?.Starprox == true ? "OUI" : "", "notes" => b?.Notes ?? "", _ => ""
    };
    private static string Csv(string v) => "\"" + (v ?? "").Replace("\"","\"\"") + "\"";
    private static string Ascii(string value) { var n=value.Normalize(NormalizationForm.FormD); var chars=n.Where(c => CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark).ToArray(); return new string(chars).Normalize(NormalizationForm.FormC).Replace('’','\'').Replace('–','-').Replace('—','-'); }
    private static string SafeName(string v) { foreach (var c in Path.GetInvalidFileNameChars()) v=v.Replace(c,'-'); return Regex.Replace(v,"\\s+","-"); }
}
