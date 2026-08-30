using System.Globalization;
using System.Windows;
using System.Windows.Controls;
namespace BadgeFlow.Desktop;
public partial class BadgeEditorWindow : Window
{
    public BadgeRecord Result { get; private set; }
    public BadgeEditorWindow(BadgeRecord? source = null)
    {
        InitializeComponent();
        Result = source is null ? new BadgeRecord() : new BadgeRecord { Id=source.Id, Number=source.Number, Hex=source.Hex, Decimal=source.Decimal, Technology=source.Technology, Starprox=source.Starprox, Label=source.Label, Notes=source.Notes, ScannedAt=source.ScannedAt, CreatedAt=source.CreatedAt, UpdatedAt=source.UpdatedAt };
        NumberBox.Text = Result.Number; LabelBox.Text = Result.Label; NotesBox.Text = Result.Notes; StarproxBox.IsChecked = Result.Starprox;
        TechnologyBox.SelectedIndex = Math.Max(0, new[]{"URMET","HEXACT","INTRATONE","AUTO"}.ToList().IndexOf(Result.Technology));
        Loaded += (_,_) => NumberBox.Focus();
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        string number = NumberBox.Text.Trim().ToUpperInvariant(); if (number.Length == 0) { MessageBox.Show("Saisissez un numéro."); return; }
        string tech = ((ComboBoxItem)TechnologyBox.SelectedItem).Content?.ToString() ?? "AUTO";
        Result.Number = number; Result.Technology = tech; Result.Starprox = StarproxBox.IsChecked == true; Result.Label = LabelBox.Text.Trim(); Result.Notes = NotesBox.Text.Trim(); Result.UpdatedAt = DateTime.Now; if(Result.CreatedAt==default) Result.CreatedAt=DateTime.Now;
        if (ulong.TryParse(number, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex)) { Result.Hex = number; Result.Decimal = unchecked((long)hex); }
        else if (long.TryParse(number, out var dec)) { Result.Decimal = dec; Result.Hex = dec.ToString("X"); }
        Result.ScannedAt = Result.ScannedAt == default ? DateTime.Now : Result.ScannedAt; DialogResult = true;
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
