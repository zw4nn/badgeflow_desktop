using System.IO;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BadgeFlow.Desktop;

public partial class MainWindow : Window
{
    private readonly DataStore _store = new();
    private readonly FdiBadgeReader _reader = new();
    private readonly AppData _data;
    private readonly ObservableCollection<BadgeRecord> _workingBadges = new();
    private Resident? _editingResident;
    private Residence? SelectedResidence => ResidenceBox.SelectedItem as Residence;

    public MainWindow()
    {
        InitializeComponent(); _data = _store.Load(); BadgeGrid.ItemsSource = _workingBadges; ReaderModeBox.SelectedIndex = 0;
        RefreshResidences(); RefreshResidents(); UpdateTotals();
        _reader.PacketRead += p => Dispatcher.Invoke(() => AddPacket(p));
        _reader.StatusChanged += s => Dispatcher.Invoke(() => UpdateStatus(s)); _reader.Start();
        Closed += (_,_) => _reader.Dispose(); PreviewKeyDown += MainWindow_PreviewKeyDown;
        PreviewMouseWheel += MainWindow_PreviewMouseWheel;
    }

    private void MainWindow_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // WPF peut avaler la molette au-dessus de certains contrôles imbriqués.
        // On route alors le mouvement vers le ScrollViewer le plus proche.
        DependencyObject? current = e.OriginalSource as DependencyObject;
        while (current is not null)
        {
            if (current is ScrollViewer sv && sv.ScrollableHeight > 0)
            {
                sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta);
                e.Handled = true;
                return;
            }
            current = VisualTreeHelper.GetParent(current);
        }
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Enter) { SaveResident(true); e.Handled = true; }
        else if (e.Key == Key.Escape) { ClearForm(); e.Handled = true; }
    }

    private void UpdateStatus(string text)
    {
        StatusText.Text = text; StatusDot.Fill = new SolidColorBrush(text.Contains("connecté",StringComparison.OrdinalIgnoreCase) ? Color.FromRgb(22,138,80) : text.Contains("Erreur",StringComparison.OrdinalIgnoreCase) || text.Contains("occupé",StringComparison.OrdinalIgnoreCase) ? Color.FromRgb(197,59,59) : Color.FromRgb(224,145,35));
    }

    private void AddPacket(BadgePacket p)
    {
        if (SelectedResidence is null) { FooterText.Text = "Sélectionnez d'abord une résidence."; return; }
        string hex = p.HexNumber.ToUpperInvariant();
        if (hex == "7020656E") { LastBadgeText.Text = "Hexact bleu : UID non reçu — repassez le badge"; LastBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(224,145,35)); return; }

        string technology; string number;
        int mode = ReaderModeBox.SelectedIndex;
        if (mode == 2) { technology="INTRATONE"; number=p.DecimalNumber; }
        else if (mode == 1) { technology="HEXACT"; number=hex; }
        else if (p.TypeByte22 == 0x14) { technology="HEXACT"; number=hex; }
        else if (p.TypeByte22 == 0x04 || p.TypeByte22 == 0x00) { technology="INTRATONE"; number=p.DecimalNumber; }
        else { technology="URMET"; number=hex; }

        var owner = _data.Residences.SelectMany(x=>x.Residents).FirstOrDefault(r => r.Id != _editingResident?.Id && r.Badges.Any(b => b.Number.Equals(number,StringComparison.OrdinalIgnoreCase)));
        if (owner is not null) { MessageBox.Show($"Le badge {number} est déjà affecté à {owner.DisplayName}.","Badge déjà attribué",MessageBoxButton.OK,MessageBoxImage.Warning); return; }
        if (_workingBadges.Any(b=>b.Number.Equals(number,StringComparison.OrdinalIgnoreCase))) { LastBadgeText.Text = $"Déjà présent : {number}"; return; }

        _workingBadges.Add(new BadgeRecord { Number=number, Hex=hex, Decimal=uint.TryParse(p.DecimalNumber,out var d)?d:0, Technology=technology, ScannedAt=DateTime.Now });
        LastBadgeText.Text = $"✓ {number}  ·  {technology}"; LastBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(22,138,80)); FooterText.Text = "Badge ajouté à la fiche en cours.";
    }

    private void SaveResident(bool clearAfter)
    {
        var residence = SelectedResidence; if (residence is null) { MessageBox.Show("Sélectionnez une résidence."); return; }
        string lastName=LastNameBox.Text.Trim(); if (lastName.Length==0) { MessageBox.Show("Saisissez au minimum le nom du résident."); LastNameBox.Focus(); return; }
        var target = _editingResident ?? new Resident(); if (_editingResident is null) residence.Residents.Add(target);
        target.LastName=lastName; target.FirstName=FirstNameBox.Text.Trim(); target.Phone=PhoneBox.Text.Trim(); target.Email=EmailBox.Text.Trim(); target.Building=BuildingBox.Text.Trim(); target.Apartment=ApartmentBox.Text.Trim(); target.Floor=FloorBox.Text.Trim(); target.Door=DoorBox.Text.Trim(); target.Notes=NotesBox.Text.Trim();
        target.Badges=_workingBadges.Select(CloneBadge).ToList(); _store.Save(_data); RefreshResidents(target.Id); UpdateTotals(); FooterText.Text=$"{target.DisplayName} enregistré.";
        if (clearAfter) ClearForm(); else { _editingResident=target; ModeText.Text="Modification en cours"; }
    }

    private static BadgeRecord CloneBadge(BadgeRecord b) => new() { Id=b.Id, Number=b.Number, Hex=b.Hex, Decimal=b.Decimal, Technology=b.Technology, Starprox=b.Starprox, Notes=b.Notes, ScannedAt=b.ScannedAt };

    private void ClearForm()
    {
        _editingResident=null; ModeText.Text="Nouveau résident"; ModeText.Foreground=(Brush)FindResource("PrimaryBrush");
        LastNameBox.Clear(); FirstNameBox.Clear(); PhoneBox.Clear(); EmailBox.Clear(); BuildingBox.Clear(); ApartmentBox.Clear(); FloorBox.Clear(); DoorBox.Clear(); NotesBox.Clear(); _workingBadges.Clear();
        LastBadgeText.Text="Posez un badge sur le lecteur"; LastBadgeText.Foreground=(Brush)FindResource("PrimaryBrush");
    }

    private void RefreshResidences(Guid? select=null)
    {
        Guid? keep=select??SelectedResidence?.Id; ResidenceBox.ItemsSource=null; ResidenceBox.ItemsSource=_data.Residences.OrderBy(r=>r.Name).ToList();
        if (ResidenceBox.Items.Count>0) ResidenceBox.SelectedItem=ResidenceBox.Items.Cast<Residence>().FirstOrDefault(r=>r.Id==keep)??ResidenceBox.Items[0];
    }

    private void RefreshResidents(Guid? select=null)
    {
        if (SelectedResidence is null) { ResidentList.ItemsSource=null; return; }
        string q=SearchBox.Text.Trim(); var list=SelectedResidence.Residents.Where(r=>q.Length==0 || r.DisplayName.Contains(q,StringComparison.OrdinalIgnoreCase) || r.Location.Contains(q,StringComparison.OrdinalIgnoreCase) || r.Badges.Any(b=>b.Number.Contains(q,StringComparison.OrdinalIgnoreCase))).OrderBy(r=>r.LastName).ThenBy(r=>r.FirstName).ToList();
        ResidentList.ItemsSource=list; if(select is not null) ResidentList.SelectedItem=list.FirstOrDefault(r=>r.Id==select);
    }

    private void UpdateTotals()
    {
        int residents=SelectedResidence?.Residents.Count??0; int badges=SelectedResidence?.Residents.Sum(r=>r.Badges.Count)??0; TotalsText.Text=$"{residents} résident(s)  •  {badges} badge(s)";
    }

    private void LoadSelectedResident()
    {
        if (ResidentList.SelectedItem is not Resident r) return; _editingResident=r; ModeText.Text="Modification en cours"; ModeText.Foreground=new SolidColorBrush(Color.FromRgb(224,145,35));
        LastNameBox.Text=r.LastName; FirstNameBox.Text=r.FirstName; PhoneBox.Text=r.Phone; EmailBox.Text=r.Email; BuildingBox.Text=r.Building; ApartmentBox.Text=r.Apartment; FloorBox.Text=r.Floor; DoorBox.Text=r.Door; NotesBox.Text=r.Notes;
        _workingBadges.Clear(); foreach(var b in r.Badges) _workingBadges.Add(CloneBadge(b)); LastBadgeText.Text=r.Badges.LastOrDefault()?.Number??"Aucun badge enregistré";
    }

    private void CreateResidence_Click(object sender, RoutedEventArgs e)
    {
        var w=new ResidenceEditWindow{Owner=this}; if(w.ShowDialog()!=true)return; var r=new Residence{Name=w.ResidenceName,Address=w.Address,PostalCode=w.PostalCode,City=w.City}; _data.Residences.Add(r); _store.Save(_data); RefreshResidences(r.Id); UpdateTotals();
    }
    private void EditResidence_Click(object sender, RoutedEventArgs e)
    {
        var r=SelectedResidence;if(r is null)return;var w=new ResidenceEditWindow(r){Owner=this};if(w.ShowDialog()!=true)return;r.Name=w.ResidenceName;r.Address=w.Address;r.PostalCode=w.PostalCode;r.City=w.City;_store.Save(_data);RefreshResidences(r.Id);
    }
    private void DeleteResidence_Click(object sender, RoutedEventArgs e)
    {
        var r=SelectedResidence;if(r is null)return;if(MessageBox.Show($"Supprimer {r.Name} et toutes ses données ?","Confirmation",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;_data.Residences.Remove(r);_store.Save(_data);RefreshResidences();ClearForm();RefreshResidents();UpdateTotals();
    }

    private void AddManualBadge_Click(object sender, RoutedEventArgs e)
    {
        var w=new BadgeEditorWindow{Owner=this};if(w.ShowDialog()!=true)return;if(BadgeExistsElsewhere(w.Result.Number)){MessageBox.Show("Ce badge existe déjà dans la base.");return;}_workingBadges.Add(w.Result);
    }
    private void EditBadge_Click(object sender, RoutedEventArgs e)
    {
        if(BadgeGrid.SelectedItem is not BadgeRecord b)return;var w=new BadgeEditorWindow(b){Owner=this};if(w.ShowDialog()!=true)return;if(BadgeExistsElsewhere(w.Result.Number,b.Id)){MessageBox.Show("Ce badge existe déjà dans la base.");return;}int i=_workingBadges.IndexOf(b);_workingBadges[i]=w.Result;
    }
    private bool BadgeExistsElsewhere(string n, Guid? current=null)=>_data.Residences.SelectMany(x=>x.Residents).SelectMany(x=>x.Badges).Any(b=>b.Id!=current && b.Number.Equals(n,StringComparison.OrdinalIgnoreCase));
    private void RemoveBadge_Click(object sender, RoutedEventArgs e){if(BadgeGrid.SelectedItem is BadgeRecord b)_workingBadges.Remove(b);}

    private void Share_Click(object sender, RoutedEventArgs e){if(SelectedResidence is null)return;new ShareWindow(SelectedResidence){Owner=this}.ShowDialog();}
    private void BackupDatabase_Click(object sender, RoutedEventArgs e){var d=new SaveFileDialog{Filter="Base BadgeFlow (*.db)|*.db",FileName=$"badgeflow-backup-{DateTime.Now:yyyyMMdd-HHmm}.db"};if(d.ShowDialog()==true){_store.BackupTo(d.FileName);MessageBox.Show("Sauvegarde terminée.");}}
    private void ExportResidence_Click(object sender, RoutedEventArgs e){if(SelectedResidence is null)return;new ShareWindow(SelectedResidence){Owner=this}.ShowDialog();}

    private void ImportCsv_Click(object sender, RoutedEventArgs e)
    {
        var d=new OpenFileDialog{Filter="CSV (*.csv)|*.csv"};if(d.ShowDialog()!=true)return;
        try{ImportCsv(d.FileName);_store.Save(_data);RefreshResidences();RefreshResidents();UpdateTotals();MessageBox.Show("Import CSV terminé.");}catch(Exception ex){MessageBox.Show("Import impossible :\n"+ex.Message);}
    }
    private void ImportCsv(string path)
    {
        var lines=File.ReadAllLines(path,Encoding.UTF8);if(lines.Length<2)throw new InvalidDataException("CSV vide.");char sep=lines[0].Count(c=>c==';')>=lines[0].Count(c=>c==',')?';':',';var headers=ParseCsv(lines[0],sep).Select(NormalizeHeader).ToArray();
        int ri=Find(headers,"residence"), ni=Find(headers,"nom"), pi=Find(headers,"prenom"), bi=Find(headers,"batiment"), ai=Find(headers,"appartement"), ti=Find(headers,"technologie"), numi=Find(headers,"numero badge","numero","badge"), si=Find(headers,"starprox");
        if(ni<0||numi<0)throw new InvalidDataException("Colonnes Nom et Numero badge requises."); Residence? currentResidence=null; Resident? currentResident=null;
        foreach(var line in lines.Skip(1)){if(string.IsNullOrWhiteSpace(line))continue;var c=ParseCsv(line,sep);string res=Get(c,ri),last=Get(c,ni),first=Get(c,pi);if(!string.IsNullOrWhiteSpace(res)) currentResidence=_data.Residences.FirstOrDefault(x=>x.Name.Equals(res,StringComparison.OrdinalIgnoreCase))??new Residence{Name=res};if(currentResidence is not null&&!_data.Residences.Contains(currentResidence))_data.Residences.Add(currentResidence);
            if(!string.IsNullOrWhiteSpace(last)){if(currentResidence is null){currentResidence=new Residence{Name="Import CSV"};_data.Residences.Add(currentResidence);} currentResident=new Resident{LastName=last,FirstName=first,Building=Get(c,bi),Apartment=Get(c,ai)};currentResidence.Residents.Add(currentResident);} if(currentResident is null)continue;string number=Get(c,numi);if(number.Length>0)currentResident.Badges.Add(new BadgeRecord{Number=number,Hex=number,Technology=Get(c,ti).ToUpperInvariant(),Starprox=Get(c,si).Equals("OUI",StringComparison.OrdinalIgnoreCase),ScannedAt=DateTime.Now});}
    }
    private static string NormalizeHeader(string s){var n=s.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);return new string(n.Where(c=>CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark).ToArray()).Normalize(NormalizationForm.FormC);}
    private static int Find(string[] h,params string[] names){for(int i=0;i<h.Length;i++)if(names.Any(n=>h[i].Equals(n,StringComparison.OrdinalIgnoreCase)))return i;return-1;}
    private static string Get(List<string> c,int i)=>i>=0&&i<c.Count?c[i].Trim():"";
    private static List<string> ParseCsv(string line,char sep){var r=new List<string>();var b=new StringBuilder();bool q=false;for(int i=0;i<line.Length;i++){char ch=line[i];if(ch=='\"'){if(q&&i+1<line.Length&&line[i+1]=='\"'){b.Append('\"');i++;}else q=!q;}else if(ch==sep&&!q){r.Add(b.ToString());b.Clear();}else b.Append(ch);}r.Add(b.ToString());return r;}

    private void ResidenceBox_SelectionChanged(object sender, SelectionChangedEventArgs e){ClearForm();RefreshResidents();UpdateTotals();}
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)=>RefreshResidents();
    private void ResidentList_MouseDoubleClick(object sender, MouseButtonEventArgs e)=>LoadSelectedResident();
    private void SaveResident_Click(object sender, RoutedEventArgs e)=>SaveResident(false);
    private void SaveNext_Click(object sender, RoutedEventArgs e)=>SaveResident(true);
    private void NewResident_Click(object sender, RoutedEventArgs e)=>ClearForm();
    private void ReaderModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e){FooterText.Text=ReaderModeBox.SelectedIndex switch{1=>"Lecture forcée Urmet / Hexact",2=>"Lecture forcée Intratone",_=>"Détection automatique"};}
}
