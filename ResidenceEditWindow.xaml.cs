using System.Windows;
using System.Windows.Controls;
namespace BadgeFlow.Desktop;
public partial class ResidenceEditWindow : Window
{
    public string ResidenceName=>NameBox.Text.Trim(); public string Address=>AddressBox.Text.Trim(); public string PostalCode=>PostalCodeBox.Text.Trim(); public string City=>CityBox.Text.Trim();
    public string Technologies=>string.Join(", ",new[]{UrmetBox.IsChecked==true?"Urmet":null,HexactBox.IsChecked==true?"Hexact":null,IntratoneBox.IsChecked==true?"Intratone":null,StarproxBox.IsChecked==true?"Starprox":null,OtherBox.IsChecked==true?"Autre":null}.Where(x=>x is not null));
    public string ManagementMode=>(ManagementBox.SelectedItem as ComboBoxItem)?.Content?.ToString()??""; public string Software=>SoftwareBox.Text.Trim(); public string ManagementNotes=>ManagementNotesBox.Text.Trim();
    public ResidenceEditWindow(Residence? residence=null,IEnumerable<string>? softwareSuggestions=null)
    {
        InitializeComponent(); SoftwareBox.ItemsSource=(softwareSuggestions??Array.Empty<string>()).OrderBy(x=>x).ToList();
        if(residence is not null){TitleText.Text="Modifier la résidence";NameBox.Text=residence.Name;AddressBox.Text=residence.Address;PostalCodeBox.Text=residence.PostalCode;CityBox.Text=residence.City;UrmetBox.IsChecked=Has(residence,"Urmet");HexactBox.IsChecked=Has(residence,"Hexact");IntratoneBox.IsChecked=Has(residence,"Intratone");StarproxBox.IsChecked=Has(residence,"Starprox");OtherBox.IsChecked=Has(residence,"Autre");for(int i=0;i<ManagementBox.Items.Count;i++)if(((ComboBoxItem)ManagementBox.Items[i]).Content?.ToString()==residence.ManagementMode)ManagementBox.SelectedIndex=i;SoftwareBox.Text=residence.Software;ManagementNotesBox.Text=residence.ManagementNotes;}
        else {TitleText.Text="Nouvelle résidence";ManagementBox.SelectedIndex=0;} Loaded+=(_,_)=>NameBox.Focus();
    }
    private static bool Has(Residence r,string value)=>r.Technologies.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Any(x=>x.Equals(value,StringComparison.OrdinalIgnoreCase));
    private void Save_Click(object sender,RoutedEventArgs e){if(ResidenceName.Length==0){MessageBox.Show("Saisissez un nom de résidence.");return;}DialogResult=true;}
    private void Cancel_Click(object sender,RoutedEventArgs e)=>DialogResult=false;
}
