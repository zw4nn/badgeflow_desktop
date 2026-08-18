using System.Windows;
namespace BadgeFlow.Desktop;
public partial class ResidenceEditWindow : Window
{
    public string ResidenceName => NameBox.Text.Trim(); public string Address => AddressBox.Text.Trim(); public string PostalCode => PostalCodeBox.Text.Trim(); public string City => CityBox.Text.Trim();
    public ResidenceEditWindow(Residence? residence = null)
    {
        InitializeComponent();
        if (residence is not null) { TitleText.Text = "Modifier la résidence"; NameBox.Text = residence.Name; AddressBox.Text = residence.Address; PostalCodeBox.Text = residence.PostalCode; CityBox.Text = residence.City; }
        else TitleText.Text = "Nouvelle résidence";
        Loaded += (_,_) => NameBox.Focus();
    }
    private void Save_Click(object sender, RoutedEventArgs e) { if (ResidenceName.Length == 0) { MessageBox.Show("Saisissez un nom de résidence."); return; } DialogResult = true; }
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
