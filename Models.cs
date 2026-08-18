namespace BadgeFlow.Desktop;

public sealed class AppData
{
    public List<Residence> Residences { get; set; } = new();
}

public sealed class Residence
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public string PostalCode { get; set; } = "";
    public string City { get; set; } = "";
    public List<Resident> Residents { get; set; } = new();
    public override string ToString() => string.IsNullOrWhiteSpace(City) ? Name : $"{Name} · {City}";
}

public sealed class Resident
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string LastName { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    public string Building { get; set; } = "";
    public string Apartment { get; set; } = "";
    public string Floor { get; set; } = "";
    public string Door { get; set; } = "";
    public string Notes { get; set; } = "";
    public List<BadgeRecord> Badges { get; set; } = new();
    public string DisplayName => $"{LastName.ToUpperInvariant()} {FirstName}".Trim();
    public string Location => string.Join(" · ", new[] { Building, Apartment }.Where(x => !string.IsNullOrWhiteSpace(x)));
    public override string ToString() => string.IsNullOrWhiteSpace(Location) ? DisplayName : $"{DisplayName} — {Location}";
}

public sealed class BadgeRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Number { get; set; } = "";
    public string Hex { get; set; } = "";
    public long Decimal { get; set; }
    public string Technology { get; set; } = "AUTO";
    public bool Starprox { get; set; }
    public string Notes { get; set; } = "";
    public DateTime ScannedAt { get; set; } = DateTime.Now;
}
