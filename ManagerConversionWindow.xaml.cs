using System.Collections.ObjectModel;
using System.Windows;
namespace BadgeFlow.Desktop;
public sealed class ConversionRow
{
    public bool Selected { get; set; } = true;
    public Resident Resident { get; set; } = null!;
    public string OriginalName => Resident.DisplayName;
    public string ProposedName { get; set; } = "";
}
public partial class ManagerConversionWindow : Window
{
    public ObservableCollection<ConversionRow> Rows { get; } = new();
    public ManagerConversionWindow(Residence source)
    {
        InitializeComponent();
        IntroText.Text=$"Gestionnaire : {source.Name}\nCoche les anciens résidents qui représentent en réalité des résidences/adresses. Le nom de chaque nouvelle résidence est modifiable avant validation.";
        foreach(var r in source.Residents)Rows.Add(new ConversionRow{Resident=r,ProposedName=r.DisplayName});
        RowsPanel.ItemsSource=Rows;
    }
    private void Convert_Click(object sender,RoutedEventArgs e){if(!Rows.Any(x=>x.Selected)){MessageBox.Show("Sélectionnez au moins une ligne.");return;}if(Rows.Where(x=>x.Selected).Any(x=>string.IsNullOrWhiteSpace(x.ProposedName))){MessageBox.Show("Chaque résidence sélectionnée doit avoir un nom.");return;}DialogResult=true;}
    private void Cancel_Click(object sender,RoutedEventArgs e)=>DialogResult=false;
}
