using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace BadgeFlow.Desktop;

public partial class MainWindow : Window
{
    private readonly DataStore _store = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly FdiBadgeReader _reader = new();
    private AppData _data;
    private AppSettings _settings;
    private readonly ObservableCollection<BadgeRecord> _workingBadges = new();
    private Resident? _editingResident;
    private bool _directScanMode;
    private bool _showByAgency;
    private readonly DispatcherTimer _syncTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private string _lastSyncHash = "";
    private DateTime _lastSyncWriteUtc = DateTime.MinValue;
    private bool _syncBusy;
    private Residence? SelectedResidence => ResidenceList.SelectedItem as Residence;

    private sealed class SearchHit
    {
        public string Display { get; set; } = "";
        public Residence Residence { get; set; } = null!;
        public Resident? Resident { get; set; }
    }

    public MainWindow()
    {
        InitializeComponent();
        _data = _store.Load();
        _settings = _settingsStore.Load();
        BadgeGrid.ItemsSource = _workingBadges;
        ReaderModeBox.SelectedIndex = 0;
        NewManagementMode.SelectedIndex = 0;
        LoadSettingsUi();
        RefreshSuggestions();
        RefreshResidences();
        RefreshResidents();
        RefreshGlobalSearch();
        UpdateTotals();
        _reader.PacketRead += p => Dispatcher.Invoke(() => AddPacket(p));
        _reader.StatusChanged += s => Dispatcher.Invoke(() => UpdateStatus(s));
        _reader.Start();
        _syncTimer.Tick += (_, _) => PollSharedSync();
        Loaded += (_, _) => { EnsureWindowFitsWorkArea(); ConfigureSharedSync(); };
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) AutoBackup(); if (MaximizeButton is not null) MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "□"; };
        Closing += (_, _) => { PushSharedSync(); AutoBackup(); };
        Closed += (_, _) => { _syncTimer.Stop(); _reader.Dispose(); };
        PreviewKeyDown += MainWindow_PreviewKeyDown;
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Enter) { SaveResident(true); e.Handled = true; }
        else if (e.Key == Key.Escape) { _directScanMode = false; ClearForm(); e.Handled = true; }
    }

    private void UpdateStatus(string text)
    {
        StatusText.Text = text;
        Color c = text.Contains("connecté", StringComparison.OrdinalIgnoreCase) ? Color.FromRgb(22, 138, 80) : text.Contains("Erreur", StringComparison.OrdinalIgnoreCase) || text.Contains("occupé", StringComparison.OrdinalIgnoreCase) ? Color.FromRgb(197, 59, 59) : Color.FromRgb(224, 145, 35);
        StatusDot.Fill = new SolidColorBrush(c);
    }

    private void AddPacket(BadgePacket p)
    {
        var residence = SelectedResidence;
        if (residence is null) { FooterText.Text = "Sélectionnez d'abord une résidence."; return; }
        string hex = p.HexNumber.ToUpperInvariant();
        if (hex == "7020656E") { LastBadgeText.Text = "Hexact bleu : UID non reçu — repassez le badge"; LastBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(224, 145, 35)); return; }
        string tech, number; int mode = ReaderModeBox.SelectedIndex;
        if (mode == 2) { tech = "INTRATONE"; number = p.DecimalNumber; }
        else if (mode == 1) { tech = "HEXACT"; number = hex; }
        else if (p.TypeByte22 == 0x14) { tech = "HEXACT"; number = hex; }
        else if (p.TypeByte22 == 0x04 || p.TypeByte22 == 0x00) { tech = "INTRATONE"; number = p.DecimalNumber; }
        else { tech = "URMET"; number = hex; }
        var duplicate = FindBadgeOwner(number, _editingResident?.Id);
        if (duplicate is not null) { MessageBox.Show($"Le badge {number} existe déjà : {duplicate}", "Badge déjà enregistré", MessageBoxButton.OK, MessageBoxImage.Warning); _directScanMode = false; return; }
        var now = DateTime.Now;
        var badge = new BadgeRecord { Number = number, Hex = hex, Decimal = uint.TryParse(p.DecimalNumber, out var d) ? d : 0, Technology = tech, ScannedAt = now, CreatedAt = now, UpdatedAt = now };
        if (_directScanMode)
        {
            residence.DirectBadges.Add(badge); _directScanMode = false; PersistData(); RefreshDirectBadges(); RefreshGlobalSearch(); UpdateTotals(); FooterText.Text = $"Badge {number} ajouté directement à {residence.Name}."; return;
        }
        if (_workingBadges.Any(b => b.Number.Equals(number, StringComparison.OrdinalIgnoreCase))) { LastBadgeText.Text = $"Déjà présent : {number}"; return; }
        _workingBadges.Add(badge); LastBadgeText.Text = $"✓ {number} · {tech}"; LastBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(22, 138, 80)); FooterText.Text = "Badge ajouté à la fiche en cours.";
    }

    private string? FindBadgeOwner(string number, Guid? currentResident = null, Guid? currentBadge = null)
    {
        foreach (var res in _data.Residences)
        {
            foreach (var b in res.DirectBadges) if (b.Id != currentBadge && b.Number.Equals(number, StringComparison.OrdinalIgnoreCase)) return $"{res.Name} · badge sans résident{(string.IsNullOrWhiteSpace(b.Label) ? "" : " · " + b.Label)}";
            foreach (var resident in res.Residents)
                if (resident.Id != currentResident)
                    foreach (var b in resident.Badges) if (b.Id != currentBadge && b.Number.Equals(number, StringComparison.OrdinalIgnoreCase)) return $"{resident.DisplayName} · {res.Name}";
        }
        return null;
    }

    private IEnumerable<string> ManagerSuggestions() => _settings.KnownManagers.Concat(_data.Residences.Select(r => r.Manager)).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x);
    private IEnumerable<string> SoftwareSuggestions() => _settings.KnownSoftware.Concat(_data.Residences.Select(r => r.Software)).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x);
    private void RememberManager(string value) { if (string.IsNullOrWhiteSpace(value)) return; if (!_settings.KnownManagers.Contains(value, StringComparer.OrdinalIgnoreCase)) _settings.KnownManagers.Add(value); _settingsStore.Save(_settings); RefreshSuggestions(); }
    private void RememberSoftware(string value) { if (string.IsNullOrWhiteSpace(value)) return; if (!_settings.KnownSoftware.Contains(value, StringComparer.OrdinalIgnoreCase)) _settings.KnownSoftware.Add(value); _settingsStore.Save(_settings); RefreshSuggestions(); }
    private void RefreshSuggestions() { NewSoftware.ItemsSource = SoftwareSuggestions().ToList(); NewManager.ItemsSource = ManagerSuggestions().ToList(); }

    private void RefreshResidences(Guid? select = null)
    {
        var keep = select ?? SelectedResidence?.Id;
        string q = ResidenceFilterBox.Text.Trim();
        var list = _data.Residences.Where(r => q.Length == 0 || $"{r.Name} {r.Manager} {r.Address} {r.City} {r.Technologies} {r.ManagementMode} {r.Software}".Contains(q, StringComparison.OrdinalIgnoreCase)).OrderBy(r => r.Name).ToList();
        ResidenceList.ItemsSource = list;
        if (list.Count > 0) ResidenceList.SelectedItem = list.FirstOrDefault(r => r.Id == keep) ?? list[0];
        else { SelectedResidenceTitle.Text = "Aucune résidence"; SelectedResidenceMeta.Text = ""; }
        var groups = list.GroupBy(r => string.IsNullOrWhiteSpace(r.Manager) ? "Sans gestionnaire" : r.Manager.Trim(), StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key).Select(g => new AgencyGroup { Name = g.Key, Residences = g.OrderBy(r => r.Name).ToList() }).ToList();
        AgencyTree.ItemsSource = groups;
    }

    private void RefreshResidents(Guid? select = null)
    {
        var r = SelectedResidence;
        if (r is null) { ResidentList.ItemsSource = null; DirectBadgeGrid.ItemsSource = null; return; }
        string q = ResidentSearchBox.Text.Trim();
        var list = r.Residents.Where(x => q.Length == 0 || x.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) || x.Location.Contains(q, StringComparison.OrdinalIgnoreCase) || x.Notes.Contains(q, StringComparison.OrdinalIgnoreCase) || x.Badges.Any(b => b.Number.Contains(q, StringComparison.OrdinalIgnoreCase) || b.Label.Contains(q, StringComparison.OrdinalIgnoreCase))).OrderBy(x => x.LastName).ThenBy(x => x.FirstName).ToList();
        ResidentList.ItemsSource = list;
        if (select is not null) ResidentList.SelectedItem = list.FirstOrDefault(x => x.Id == select);
        RefreshDirectBadges();
    }
    private void RefreshDirectBadges() { DirectBadgeGrid.ItemsSource = SelectedResidence?.DirectBadges.OrderBy(b => b.CreatedAt).ToList(); DirectBadgeGrid.Items.Refresh(); }
    private void UpdateTotals()
    {
        var r = SelectedResidence; if (r is null) { TotalsText.Text = ""; return; }
        int totalBadges = r.DirectBadges.Count + r.Residents.Sum(x => x.Badges.Count);
        TotalsText.Text = $"{r.Residents.Count} résident(s) · {totalBadges} badge(s)";
        SelectedResidenceTitle.Text = r.Name;
        SelectedResidenceMeta.Text = string.Join(" · ", new[] { r.Manager, r.City, r.Technologies, r.ManagementMode, r.Software }.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private void RefreshGlobalSearch()
    {
        string q = GlobalSearchBox.Text.Trim(); var hits = new List<SearchHit>();
        if (q.Length > 0)
        {
            foreach (var res in _data.Residences)
            {
                string rh = $"{res.Name} {res.Manager} {res.Address} {res.PostalCode} {res.City} {res.Technologies} {res.ManagementMode} {res.Software} {res.ManagementNotes}";
                if (rh.Contains(q, StringComparison.OrdinalIgnoreCase)) hits.Add(new SearchHit { Residence = res, Display = $"RÉSIDENCE · {res.Name} — {res.ManagementSummary}" });
                foreach (var b in res.DirectBadges) if ($"{b.Number} {b.Label} {b.Technology} {b.Notes}".Contains(q, StringComparison.OrdinalIgnoreCase)) hits.Add(new SearchHit { Residence = res, Display = $"BADGE · {b.Number} · {b.Label} — {res.Name}" });
                foreach (var resident in res.Residents)
                {
                    string h = $"{resident.LastName} {resident.FirstName} {resident.DisplayName} {resident.Location} {resident.Phone} {resident.Email} {resident.Notes}";
                    if (h.Contains(q, StringComparison.OrdinalIgnoreCase)) hits.Add(new SearchHit { Residence = res, Resident = resident, Display = $"RÉSIDENT · {resident.DisplayName} — {res.Name} {resident.Location}" });
                    foreach (var b in resident.Badges) if ($"{b.Number} {b.Label} {b.Technology} {b.Notes}".Contains(q, StringComparison.OrdinalIgnoreCase)) hits.Add(new SearchHit { Residence = res, Resident = resident, Display = $"BADGE · {b.Number} · {b.Technology} — {resident.DisplayName} / {res.Name}" });
                }
            }
        }
        GlobalResultsList.ItemsSource = hits.Take(250).ToList();
    }

    private static BadgeRecord CloneBadge(BadgeRecord b) => new() { Id = b.Id, Number = b.Number, Hex = b.Hex, Decimal = b.Decimal, Technology = b.Technology, Starprox = b.Starprox, Label = b.Label, Notes = b.Notes, ScannedAt = b.ScannedAt, CreatedAt = b.CreatedAt, UpdatedAt = b.UpdatedAt };
    private void LoadSelectedResident()
    {
        if (ResidentList.SelectedItem is not Resident r) return;
        _editingResident = r; ModeText.Text = "Modification du résident"; LastNameBox.Text = r.LastName; FirstNameBox.Text = r.FirstName; PhoneBox.Text = r.Phone; EmailBox.Text = r.Email; BuildingBox.Text = r.Building; ApartmentBox.Text = r.Apartment; FloorBox.Text = r.Floor; DoorBox.Text = r.Door; NotesBox.Text = r.Notes; _workingBadges.Clear(); foreach (var b in r.Badges) _workingBadges.Add(CloneBadge(b)); LastBadgeText.Text = r.Badges.LastOrDefault()?.Number ?? "Aucun badge enregistré";
    }
    private void ClearForm() { _editingResident = null; ModeText.Text = "Nouveau résident"; LastNameBox.Clear(); FirstNameBox.Clear(); PhoneBox.Clear(); EmailBox.Clear(); BuildingBox.Clear(); ApartmentBox.Clear(); FloorBox.Clear(); DoorBox.Clear(); NotesBox.Clear(); _workingBadges.Clear(); LastBadgeText.Text = "Posez un badge sur le lecteur"; LastBadgeText.Foreground = (Brush)FindResource("PrimaryBrush"); }
    private void SaveResident(bool clearAfter)
    {
        var residence = SelectedResidence; if (residence is null) { MessageBox.Show("Sélectionnez une résidence."); return; }
        string last = LastNameBox.Text.Trim(); if (last.Length == 0) { MessageBox.Show("Saisissez au minimum le nom du résident."); return; }
        var target = _editingResident ?? new Resident(); if (_editingResident is null) residence.Residents.Add(target);
        target.LastName = last; target.FirstName = FirstNameBox.Text.Trim(); target.Phone = PhoneBox.Text.Trim(); target.Email = EmailBox.Text.Trim(); target.Building = BuildingBox.Text.Trim(); target.Apartment = ApartmentBox.Text.Trim(); target.Floor = FloorBox.Text.Trim(); target.Door = DoorBox.Text.Trim(); target.Notes = NotesBox.Text.Trim(); target.Badges = _workingBadges.Select(CloneBadge).ToList();
        PersistData(); RefreshResidents(target.Id); RefreshGlobalSearch(); UpdateTotals(); FooterText.Text = $"{target.DisplayName} enregistré."; if (clearAfter) ClearForm(); else _editingResident = target;
    }

    private string SelectedNewTechnologies() => string.Join(", ", new[] { NewUrmet.IsChecked == true ? "Urmet" : null, NewHexact.IsChecked == true ? "Hexact" : null, NewIntratone.IsChecked == true ? "Intratone" : null, NewStarprox.IsChecked == true ? "Starprox" : null, NewOther.IsChecked == true ? "Autre" : null }.Where(x => x is not null));
    private void CreateResidenceFromTab_Click(object sender, RoutedEventArgs e)
    {
        string name = NewResidenceName.Text.Trim(); if (name.Length == 0) { MessageBox.Show("Saisissez un nom de résidence."); return; }
        var r = new Residence { Name = name, Manager = NewManager.Text.Trim(), Address = NewResidenceAddress.Text.Trim(), PostalCode = NewResidencePostal.Text.Trim(), City = NewResidenceCity.Text.Trim(), Technologies = SelectedNewTechnologies(), ManagementMode = (NewManagementMode.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "", Software = NewSoftware.Text.Trim(), ManagementNotes = NewManagementNotes.Text.Trim(), CreatedAt = DateTime.Now };
        _data.Residences.Add(r); RememberManager(r.Manager); RememberSoftware(r.Software); PersistData(); RefreshResidences(r.Id); RefreshGlobalSearch(); ClearNewResidenceForm(); MainTabs.SelectedIndex = 1; FooterText.Text = $"Résidence {r.Name} créée.";
    }
    private void ClearNewResidenceForm() { NewResidenceName.Clear(); NewManager.Text = ""; NewResidenceAddress.Clear(); NewResidencePostal.Clear(); NewResidenceCity.Clear(); NewUrmet.IsChecked = NewHexact.IsChecked = NewIntratone.IsChecked = NewStarprox.IsChecked = NewOther.IsChecked = false; NewManagementMode.SelectedIndex = 0; NewSoftware.Text = ""; NewManagementNotes.Clear(); }
    private void EditResidence_Click(object sender, RoutedEventArgs e)
    {
        var r = SelectedResidence; if (r is null) return; var w = new ResidenceEditWindow(r, SoftwareSuggestions(), ManagerSuggestions()) { Owner = this }; if (w.ShowDialog() != true) return;
        r.Name = w.ResidenceName; r.Manager = w.Manager; r.Address = w.Address; r.PostalCode = w.PostalCode; r.City = w.City; r.Technologies = w.Technologies; r.ManagementMode = w.ManagementMode; r.Software = w.Software; r.ManagementNotes = w.ManagementNotes; RememberManager(r.Manager); RememberSoftware(r.Software); PersistData(); RefreshResidences(r.Id); RefreshGlobalSearch(); UpdateTotals();
    }
    private void DeleteResidence_Click(object sender, RoutedEventArgs e) { var r = SelectedResidence; if (r is null) return; if (MessageBox.Show($"Supprimer {r.Name} et toutes ses données ?", "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return; _data.Residences.Remove(r); PersistData(); ClearForm(); RefreshResidences(); RefreshResidents(); RefreshGlobalSearch(); UpdateTotals(); }
    private void ConvertToManager_Click(object sender, RoutedEventArgs e)
    {
        var source=SelectedResidence;if(source is null)return;if(source.Residents.Count==0){MessageBox.Show("Cette fiche ne contient aucun résident à convertir.");return;}
        var w=new ManagerConversionWindow(source){Owner=this};if(w.ShowDialog()!=true)return;string manager=source.Name;RememberManager(manager);
        foreach(var row in w.Rows.Where(x=>x.Selected).ToList())
        {
            var p=row.Resident;var nr=new Residence{Name=row.ProposedName.Trim(),Manager=manager,Technologies=source.Technologies,ManagementMode=source.ManagementMode,Software=source.Software,ManagementNotes=string.IsNullOrWhiteSpace(p.Notes)?source.ManagementNotes:$"{source.ManagementNotes}\nAncienne fiche : {p.Notes}".Trim(),CreatedAt=DateTime.Now};
            foreach(var b in p.Badges)nr.DirectBadges.Add(CloneBadge(b));source.Residents.Remove(p);_data.Residences.Add(nr);
        }
        if(source.Residents.Count==0&&source.DirectBadges.Count==0)_data.Residences.Remove(source);PersistData();RefreshResidences();RefreshGlobalSearch();UpdateTotals();FooterText.Text=$"Conversion terminée : gestionnaire {manager}.";
    }

    private void AddManualBadge_Click(object sender, RoutedEventArgs e)
    {
        var w = new BadgeEditorWindow { Owner = this }; if (w.ShowDialog() != true) return; var duplicate = FindBadgeOwner(w.Result.Number, _editingResident?.Id); if (duplicate is not null || _workingBadges.Any(b => b.Number.Equals(w.Result.Number, StringComparison.OrdinalIgnoreCase))) { MessageBox.Show(duplicate is null ? "Ce badge est déjà présent sur cette fiche." : $"Ce badge existe déjà : {duplicate}", "Doublon interdit"); return; } _workingBadges.Add(w.Result);
    }
    private void EditBadge_Click(object sender, RoutedEventArgs e) { if (BadgeGrid.SelectedItem is not BadgeRecord b) return; var w = new BadgeEditorWindow(b) { Owner = this }; if (w.ShowDialog() != true) return; var duplicate = FindBadgeOwner(w.Result.Number, _editingResident?.Id, b.Id); if (duplicate is not null || _workingBadges.Any(x => x.Id != b.Id && x.Number.Equals(w.Result.Number, StringComparison.OrdinalIgnoreCase))) { MessageBox.Show("Ce numéro de badge existe déjà.", "Doublon interdit"); return; } int i = _workingBadges.IndexOf(b); _workingBadges[i] = w.Result; }
    private void RemoveBadge_Click(object sender, RoutedEventArgs e) { if (BadgeGrid.SelectedItem is BadgeRecord b) _workingBadges.Remove(b); }

    private void ScanDirectBadge_Click(object sender, RoutedEventArgs e) { if (SelectedResidence is null) return; _directScanMode = true; FooterText.Text = "Posez le badge sur le lecteur : il sera rattaché directement à la résidence."; }
    private void AddDirectBadge_Click(object sender, RoutedEventArgs e)
    {
        var r = SelectedResidence; if (r is null) return; var w = new BadgeEditorWindow { Owner = this }; if (w.ShowDialog() != true) return; var duplicate = FindBadgeOwner(w.Result.Number); if (duplicate is not null) { MessageBox.Show($"Ce badge existe déjà : {duplicate}", "Doublon interdit"); return; } r.DirectBadges.Add(w.Result); PersistData(); RefreshDirectBadges(); RefreshGlobalSearch(); UpdateTotals();
    }
    private void EditDirectBadge_Click(object sender, RoutedEventArgs e)
    {
        var r = SelectedResidence; if (r is null || DirectBadgeGrid.SelectedItem is not BadgeRecord b) return; var w = new BadgeEditorWindow(b) { Owner = this }; if (w.ShowDialog() != true) return; var duplicate = FindBadgeOwner(w.Result.Number, null, b.Id); if (duplicate is not null) { MessageBox.Show($"Ce badge existe déjà : {duplicate}", "Doublon interdit"); return; } var i = r.DirectBadges.FindIndex(x => x.Id == b.Id); if (i >= 0) r.DirectBadges[i] = w.Result; PersistData(); RefreshDirectBadges(); RefreshGlobalSearch();
    }
    private void RemoveDirectBadge_Click(object sender, RoutedEventArgs e) { var r = SelectedResidence; if (r is null || DirectBadgeGrid.SelectedItem is not BadgeRecord b) return; if (MessageBox.Show($"Retirer le badge {b.Number} ?", "Confirmation", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return; r.DirectBadges.RemoveAll(x => x.Id == b.Id); PersistData(); RefreshDirectBadges(); RefreshGlobalSearch(); UpdateTotals(); }

    private void ShowByResidence_Click(object sender, RoutedEventArgs e) { _showByAgency = false; ResidenceList.Visibility = Visibility.Visible; AgencyTree.Visibility = Visibility.Collapsed; ByResidenceButton.Style = (Style)FindResource("PrimaryButton"); ByAgencyButton.Style = (Style)FindResource("SecondaryButton"); }
    private void ShowByAgency_Click(object sender, RoutedEventArgs e) { _showByAgency = true; ResidenceList.Visibility = Visibility.Collapsed; AgencyTree.Visibility = Visibility.Visible; ByResidenceButton.Style = (Style)FindResource("SecondaryButton"); ByAgencyButton.Style = (Style)FindResource("PrimaryButton"); RefreshResidences(SelectedResidence?.Id); }
    private void AgencyTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) { if (e.NewValue is Residence r) ResidenceList.SelectedItem = r; }

    private void Share_Click(object sender, RoutedEventArgs e) { if (SelectedResidence is null) return; new ShareWindow(SelectedResidence) { Owner = this }.ShowDialog(); }
    private void PersistData() { _store.Save(_data); PushSharedSync(); }

    private string SemanticHash(AppData data)
    {
        var b = new StringBuilder(); foreach (var r in data.Residences.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)) { b.Append("R|").Append(r.Name).Append('|').Append(r.Manager).Append('|').Append(r.Address).Append('|').Append(r.PostalCode).Append('|').Append(r.City).Append('|').Append(r.Technologies).Append('|').Append(r.ManagementMode).Append('|').Append(r.Software).Append('|').Append(r.ManagementNotes).Append('\n'); foreach (var d in r.DirectBadges.OrderBy(x => x.Number)) b.Append("D|").Append(d.Number).Append('|').Append(d.Label).Append('|').Append(d.Technology).Append('|').Append(d.UpdatedAt.Ticks).Append('\n'); foreach (var p in r.Residents.OrderBy(x => x.LastName).ThenBy(x => x.FirstName)) { b.Append("P|").Append(p.LastName).Append('|').Append(p.FirstName).Append('|').Append(p.Phone).Append('|').Append(p.Email).Append('|').Append(p.Building).Append('|').Append(p.Apartment).Append('|').Append(p.Floor).Append('|').Append(p.Door).Append('|').Append(p.Notes).Append('\n'); foreach (var badge in p.Badges.OrderBy(x => x.Number)) b.Append("B|").Append(badge.Number).Append('|').Append(badge.Label).Append('|').Append(badge.Technology).Append('|').Append(badge.UpdatedAt.Ticks).Append('\n'); } } return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(b.ToString())));
    }
    private void ConfigureSharedSync() { UpdateSharedSyncUi(); if (!_settings.SharedSyncEnabled || string.IsNullOrWhiteSpace(_settings.SharedSyncFile) || !File.Exists(_settings.SharedSyncFile)) { _syncTimer.Stop(); return; } try { var remote = BadgeFlowExchange.Import(_settings.SharedSyncFile); _lastSyncHash = SemanticHash(remote); _lastSyncWriteUtc = File.GetLastWriteTimeUtc(_settings.SharedSyncFile); if (SemanticHash(_data) != _lastSyncHash) { _data = remote; _store.Save(_data); RefreshAllAfterSync(); FooterText.Text = "Base partagée chargée depuis Google Drive."; } } catch (Exception ex) { FooterText.Text = "Synchronisation : " + ex.Message; } _syncTimer.Start(); }
    private void RefreshAllAfterSync() { ClearForm(); RefreshSuggestions(); RefreshResidences(); RefreshResidents(); RefreshGlobalSearch(); UpdateTotals(); }
    private void PollSharedSync() { if (_syncBusy || !_settings.SharedSyncEnabled || string.IsNullOrWhiteSpace(_settings.SharedSyncFile) || !File.Exists(_settings.SharedSyncFile)) return; var write = File.GetLastWriteTimeUtc(_settings.SharedSyncFile); if (write <= _lastSyncWriteUtc) return; PullSharedSync(false); }
    private void PullSharedSync(bool force) { if (_syncBusy || !_settings.SharedSyncEnabled || string.IsNullOrWhiteSpace(_settings.SharedSyncFile) || !File.Exists(_settings.SharedSyncFile)) return; try { _syncBusy = true; var remote = BadgeFlowExchange.Import(_settings.SharedSyncFile); var remoteHash = SemanticHash(remote); var localHash = SemanticHash(_data); if (force || remoteHash != localHash) { if (!force && _lastSyncHash.Length > 0 && localHash != _lastSyncHash && remoteHash != _lastSyncHash) { FooterText.Text = "Conflit de synchronisation : les deux appareils ont modifié la base."; return; } _data = remote; _store.Save(_data); RefreshAllAfterSync(); FooterText.Text = "Base partagée mise à jour automatiquement."; } _lastSyncHash = remoteHash; _lastSyncWriteUtc = File.GetLastWriteTimeUtc(_settings.SharedSyncFile); UpdateSharedSyncUi(); } catch (Exception ex) { FooterText.Text = "Synchronisation impossible : " + ex.Message; } finally { _syncBusy = false; } }
    private void PushSharedSync() { if (_syncBusy || !_settings.SharedSyncEnabled || string.IsNullOrWhiteSpace(_settings.SharedSyncFile)) return; try { _syncBusy = true; var localHash = SemanticHash(_data); if (File.Exists(_settings.SharedSyncFile)) { var remote = BadgeFlowExchange.Import(_settings.SharedSyncFile); var remoteHash = SemanticHash(remote); if (_lastSyncHash.Length > 0 && remoteHash != _lastSyncHash && localHash != _lastSyncHash) { FooterText.Text = "Conflit de synchronisation : fichier distant modifié. Synchronisation suspendue."; return; } if (remoteHash == localHash) { _lastSyncHash = localHash; _lastSyncWriteUtc = File.GetLastWriteTimeUtc(_settings.SharedSyncFile); return; } } var dir = Path.GetDirectoryName(_settings.SharedSyncFile); if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir); var tmp = _settings.SharedSyncFile + ".tmp"; BadgeFlowExchange.Export(tmp, _data); File.Move(tmp, _settings.SharedSyncFile, true); _lastSyncHash = localHash; _lastSyncWriteUtc = File.GetLastWriteTimeUtc(_settings.SharedSyncFile); Dispatcher.Invoke(() => { SyncStatusText.Text = "Synchronisé"; FooterText.Text = "Base partagée enregistrée."; }); } catch (Exception ex) { Dispatcher.Invoke(() => { SyncStatusText.Text = "Erreur"; FooterText.Text = "Synchronisation impossible : " + ex.Message; }); } finally { _syncBusy = false; } }
    private void UpdateSharedSyncUi() { if (SharedSyncPathBox is null) return; SharedSyncPathBox.Text = _settings.SharedSyncFile; SyncStatusText.Text = _settings.SharedSyncEnabled && !string.IsNullOrWhiteSpace(_settings.SharedSyncFile) ? "Synchronisation active" : "Non configurée"; }
    private void ChooseSharedSync_Click(object sender, RoutedEventArgs e) { var d = new OpenFileDialog { Filter = "Base BadgeFlow (*.badgeflow)|*.badgeflow", Title = "Choisir la base BadgeFlow partagée dans Google Drive" }; if (d.ShowDialog() != true) return; if (MessageBox.Show("Connecter ce fichier partagé ? La base locale sera remplacée par son contenu pour la première synchronisation.", "Base partagée", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return; _settings.SharedSyncFile = d.FileName; _settings.SharedSyncEnabled = true; _settingsStore.Save(_settings); _lastSyncHash = ""; PullSharedSync(true); _syncTimer.Start(); UpdateSharedSyncUi(); }
    private void CreateSharedSync_Click(object sender, RoutedEventArgs e) { var d = new SaveFileDialog { Filter = "Base BadgeFlow (*.badgeflow)|*.badgeflow", FileName = "BadgeFlow-Sync.badgeflow", Title = "Créer la base BadgeFlow partagée dans Google Drive" }; if (d.ShowDialog() != true) return; _settings.SharedSyncFile = d.FileName; _settings.SharedSyncEnabled = true; _settingsStore.Save(_settings); _lastSyncHash = ""; PushSharedSync(); _syncTimer.Start(); UpdateSharedSyncUi(); }
    private void SyncNow_Click(object sender, RoutedEventArgs e) => PullSharedSync(false);
    private void DisableSharedSync_Click(object sender, RoutedEventArgs e) { _settings.SharedSyncEnabled = false; _settingsStore.Save(_settings); _syncTimer.Stop(); UpdateSharedSyncUi(); FooterText.Text = "Synchronisation partagée désactivée."; }

    private void LoadSettingsUi() { AutoBackupBox.IsChecked = _settings.AutoBackup; BackupFolderBox.Text = _settings.BackupFolder; BackupNameBox.Text = _settings.BackupName; UpdateBackupPreview(); }
    private void SaveSettingsFromUi() { _settings.AutoBackup = AutoBackupBox.IsChecked == true; _settings.BackupFolder = BackupFolderBox.Text.Trim(); _settings.BackupName = BackupNameBox.Text.Trim(); _settingsStore.Save(_settings); UpdateBackupPreview(); }
    private string BackupFileName() { string name = SafeFilePart(_settings.BackupName); return string.IsNullOrWhiteSpace(name) ? "BadgeFlow-auto.badgeflow" : $"BadgeFlow-auto-{name}.badgeflow"; }
    private void UpdateBackupPreview() { BackupFilePreview.Text = string.IsNullOrWhiteSpace(BackupFolderBox.Text) ? "Aucun dossier sélectionné" : $"Fichier : {BackupFileName()}"; }
    private void AutoBackup() { if (!_settings.AutoBackup || string.IsNullOrWhiteSpace(_settings.BackupFolder)) return; try { _store.Save(_data); BadgeFlowExchange.Export(Path.Combine(_settings.BackupFolder, BackupFileName()), _data); } catch (Exception ex) { Dispatcher.Invoke(() => FooterText.Text = "Sauvegarde auto impossible : " + ex.Message); } }
    private void ChooseBackupFolder_Click(object sender, RoutedEventArgs e) { var d = new OpenFolderDialog { Title = "Choisir le dossier de sauvegarde BadgeFlow" }; if (d.ShowDialog() == true) { BackupFolderBox.Text = d.FolderName; SaveSettingsFromUi(); } }
    private void BackupNow_Click(object sender, RoutedEventArgs e) { SaveSettingsFromUi(); if (string.IsNullOrWhiteSpace(_settings.BackupFolder)) { MessageBox.Show("Choisissez d'abord un dossier de sauvegarde."); return; } try { _store.Save(_data); BadgeFlowExchange.Export(Path.Combine(_settings.BackupFolder, BackupFileName()), _data); MessageBox.Show("Sauvegarde terminée."); } catch (Exception ex) { MessageBox.Show("Sauvegarde impossible :\n" + ex.Message); } }
    private void SettingsChanged(object sender, RoutedEventArgs e) => SaveSettingsFromUi();
    private void BackupNameBox_TextChanged(object sender, TextChangedEventArgs e) { if (IsLoaded) SaveSettingsFromUi(); }
    private void ExportDatabase_Click(object sender, RoutedEventArgs e) { var d = new SaveFileDialog { Filter = "Base BadgeFlow (*.badgeflow)|*.badgeflow", FileName = "BadgeFlow-export.badgeflow" }; if (d.ShowDialog() == true) { _store.Save(_data); BadgeFlowExchange.Export(d.FileName, _data); } }
    private void ImportDatabase_Click(object sender, RoutedEventArgs e) { var d = new OpenFileDialog { Filter = "Base BadgeFlow (*.badgeflow)|*.badgeflow|Ancienne base PC (*.db)|*.db" }; if (d.ShowDialog() != true) return; if (MessageBox.Show("Remplacer la base locale par ce fichier ?", "Importer la base", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return; try { if (Path.GetExtension(d.FileName).Equals(".db", StringComparison.OrdinalIgnoreCase)) { _reader.Dispose(); _store.RestoreFrom(d.FileName); _data = _store.Load(); MessageBox.Show("Ancienne base PC importée. Redémarrez BadgeFlow pour relancer le lecteur FDI."); } else { _data = BadgeFlowExchange.Import(d.FileName); _store.Save(_data); MessageBox.Show("Base .badgeflow importée avec succès."); } RefreshAllAfterSync(); } catch (Exception ex) { MessageBox.Show("Import impossible :\n" + ex.Message); } }

    private static readonly string[] DirectoryHeaders = { "Residence", "Gestionnaire", "Adresse", "CodePostal", "Ville", "Technologies", "ModeGestion", "LogicielGestion", "NotesGestion" };
    private void ExportDirectoryCsv_Click(object sender, RoutedEventArgs e) { var d = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "BadgeFlow-annuaire.csv" }; if (d.ShowDialog() == true) File.WriteAllText(d.FileName, BuildDirectoryCsv(_data.Residences), new UTF8Encoding(false)); }
    private void CreateDirectoryTemplate_Click(object sender, RoutedEventArgs e) { var d = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "BadgeFlow-modele-annuaire.csv" }; if (d.ShowDialog() == true) { var sample = new Residence { Name = "Residence Exemple", Manager = "Agence Exemple", Address = "1 rue Exemple", PostalCode = "66000", City = "Perpignan", Technologies = "Urmet, Hexact", ManagementMode = "Les deux", Software = "Logiciel Exemple", ManagementNotes = "Notes terrain" }; File.WriteAllText(d.FileName, BuildDirectoryCsv(new[] { sample }), new UTF8Encoding(false)); } }
    private string BuildDirectoryCsv(IEnumerable<Residence> rows) { var b = new StringBuilder(); b.AppendLine(string.Join(';', DirectoryHeaders)); foreach (var r in rows) b.AppendLine(string.Join(';', new[] { r.Name, r.Manager, r.Address, r.PostalCode, r.City, r.Technologies, r.ManagementMode, r.Software, r.ManagementNotes }.Select(Csv))); return b.ToString(); }
    private void ImportDirectoryCsv_Click(object sender, RoutedEventArgs e) { var d = new OpenFileDialog { Filter = "CSV (*.csv)|*.csv" }; if (d.ShowDialog() != true) return; try { ImportDirectoryCsv(d.FileName); PersistData(); RefreshAllAfterSync(); MessageBox.Show("Annuaire importé."); } catch (Exception ex) { MessageBox.Show("Import impossible :\n" + ex.Message); } }
    private void ImportDirectoryCsv(string path) { var lines = File.ReadAllLines(path, Encoding.UTF8); if (lines.Length < 2) throw new InvalidDataException("CSV vide."); char sep = lines[0].Contains(';') ? ';' : ','; var h = ParseCsv(lines[0], sep).Select(NormalizeHeader).ToArray(); int n = Find(h, "residence"), g = Find(h, "gestionnaire", "agence"), a = Find(h, "adresse"), p = Find(h, "codepostal", "code postal"), v = Find(h, "ville"), t = Find(h, "technologies"), m = Find(h, "modegestion", "mode gestion"), s = Find(h, "logicielgestion", "logiciel gestion"), no = Find(h, "notesgestion", "notes gestion"); if (n < 0) throw new InvalidDataException("Colonne Residence requise."); foreach (var line in lines.Skip(1)) { if (string.IsNullOrWhiteSpace(line)) continue; var c = ParseCsv(line, sep); string name = Get(c, n); if (name.Length == 0) continue; var r = _data.Residences.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? new Residence { Name = name }; if (!_data.Residences.Contains(r)) _data.Residences.Add(r); r.Manager = Get(c, g); r.Address = Get(c, a); r.PostalCode = Get(c, p); r.City = Get(c, v); r.Technologies = Get(c, t); r.ManagementMode = Get(c, m); r.Software = Get(c, s); r.ManagementNotes = Get(c, no); RememberManager(r.Manager); RememberSoftware(r.Software); } }

    private static string Csv(string s) => $"\"{(s ?? "").Replace("\"", "\"\"")}\"";
    private static string SafeFilePart(string s) => string.Concat(s.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch))).Trim().Replace(' ', '-');
    private static string NormalizeHeader(string s) { var n = s.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD); return new string(n.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).Normalize(NormalizationForm.FormC).Replace("_", "").Replace(" ", ""); }
    private static int Find(string[] h, params string[] names) { var nn = names.Select(NormalizeHeader).ToArray(); for (int i = 0; i < h.Length; i++) if (nn.Contains(h[i], StringComparer.OrdinalIgnoreCase)) return i; return -1; }
    private static string Get(List<string> c, int i) => i >= 0 && i < c.Count ? c[i].Trim() : "";
    private static List<string> ParseCsv(string line, char sep) { var r = new List<string>(); var b = new StringBuilder(); bool q = false; for (int i = 0; i < line.Length; i++) { char ch = line[i]; if (ch == '\"') { if (q && i + 1 < line.Length && line[i + 1] == '\"') { b.Append('\"'); i++; } else q = !q; } else if (ch == sep && !q) { r.Add(b.ToString()); b.Clear(); } else b.Append(ch); } r.Add(b.ToString()); return r; }

    private void ResidenceList_SelectionChanged(object sender, SelectionChangedEventArgs e) { _directScanMode = false; ClearForm(); RefreshResidents(); UpdateTotals(); }
    private void ResidenceFilterBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshResidences();
    private void ResidentSearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshResidents();
    private void GlobalSearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshGlobalSearch();
    private void GlobalResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) { if (GlobalResultsList.SelectedItem is not SearchHit hit) return; MainTabs.SelectedIndex = 1; RefreshResidences(hit.Residence.Id); if (hit.Resident is not null) { RefreshResidents(hit.Resident.Id); LoadSelectedResident(); } }
    private void ResidentList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => LoadSelectedResident();
    private void SaveResident_Click(object sender, RoutedEventArgs e) => SaveResident(false);
    private void SaveNext_Click(object sender, RoutedEventArgs e) => SaveResident(true);
    private void NewResident_Click(object sender, RoutedEventArgs e) => ClearForm();

    private void EnsureWindowFitsWorkArea() { var wa = SystemParameters.WorkArea; if (Width > wa.Width - 20) Width = Math.Max(MinWidth, wa.Width - 20); if (Height > wa.Height - 20) Height = Math.Max(MinHeight, wa.Height - 20); if (Left < wa.Left || Left + Width > wa.Right) Left = wa.Left + Math.Max(0, (wa.Width - Width) / 2); if (Top < wa.Top || Top + Height > wa.Bottom) Top = wa.Top + Math.Max(0, (wa.Height - Height) / 2); }
    private static bool IsInteractiveHeaderElement(DependencyObject? source) { for (var current = source; current is not null; current = System.Windows.Media.VisualTreeHelper.GetParent(current)) if (current is System.Windows.Controls.Primitives.ButtonBase || current is ComboBox || current is TextBox) return true; return false; }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ChangedButton != MouseButton.Left || IsInteractiveHeaderElement(e.OriginalSource as DependencyObject)) return; if (e.ClickCount == 2) { ToggleMaximizeRestore(); e.Handled = true; return; } if (WindowState == WindowState.Maximized) { var mouse = e.GetPosition(this); var screen = PointToScreen(mouse); var ratio = ActualWidth > 0 ? mouse.X / ActualWidth : 0.5; WindowState = WindowState.Normal; Left = screen.X - (Width * ratio); Top = Math.Max(SystemParameters.WorkArea.Top, screen.Y - 20); } try { DragMove(); } catch { } }
    private void MinimizeWindow_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeRestoreWindow_Click(object sender, RoutedEventArgs e) => ToggleMaximizeRestore();
    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();
    private void ToggleMaximizeRestore() { WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; if (MaximizeButton is not null) MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "□"; }
    private void ReaderModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (FooterText is null) return; FooterText.Text = ReaderModeBox.SelectedIndex switch { 1 => "Lecture forcée Urmet / Hexact", 2 => "Lecture forcée Intratone", _ => "Détection automatique" }; }
}
