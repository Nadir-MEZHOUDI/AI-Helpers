namespace MyApp.Mobile.Layout;

/// <summary>Every page of the menu. <see cref="InTabBar"/> ones are also in the phone's bottom bar.</summary>
public sealed record NavItem(string Href, string Label, string Icon, bool InTabBar = false, string? Short = null)
{
    public static readonly IReadOnlyList<NavItem> All =
    [
        new("", "Accueil", "M3 12l9-9 9 9M5 10v10h5v-6h4v6h5V10", InTabBar: true),
        new("statistiques", "Statistiques", "M3 3v18h18M7 16v-4M12 16V8M17 16V5"),
        new("factures", "Factures", "M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8zM14 2v6h6M8 13h8M8 17h8", InTabBar: true),
        new("patients", "Patients", "M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8zM22 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75", InTabBar: true),
        new("medicaments", "Médicaments", "M10.5 20.5 3.5 13.5a5 5 0 0 1 7-7l7 7a5 5 0 0 1-7 7zM8.5 8.5l7 7", InTabBar: true, Short: "Médic."),
        new("bordereaux", "Bordereaux", "M3 7h18M3 12h18M3 17h18"),
        new("rejets", "Rejets et paiements", "M12 9v4M12 17h.01M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z", InTabBar: true, Short: "Rejets"),
        new("psychotropes", "Psychotropes", "M9 3h6M10 3v6l-5 9a2 2 0 0 0 1.8 3h10.4a2 2 0 0 0 1.8-3l-5-9V3M7 15h10"),
        new("notifications", "Notifications", "M18 8a6 6 0 0 0-12 0c0 7-3 9-3 9h18s-3-2-3-9M13.7 21a2 2 0 0 1-3.4 0")
    ];
}
