using System.Collections.ObjectModel;
using System.Net.Http.Json;
using System.Windows;

namespace GamingClub.Desktop;

public partial class MainWindow : Window
{
    // For Android emulator localhost is different. Desktop defaults to local API.
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(10) };
    public ObservableCollection<SeatView> Seats { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        try
        {
            StatusText.Text = "Загрузка...";
            var url = Environment.GetEnvironmentVariable("GAMINGCLUB_API_URL") ?? "http://localhost:5080";
            var seats = await Client.GetFromJsonAsync<List<SeatDto>>($"{url.TrimEnd('/')}/api/seats");
            Seats.Clear();
            foreach (var seat in seats ?? [])
                Seats.Add(new SeatView(seat.SeatNumber, seat.IsActive ? "Доступен" : "Отключён"));
            StatusText.Text = $"Мест: {Seats.Count}";
        }
        catch (Exception ex)
        {
            // Never show secrets or a raw connection string in the UI.
            StatusText.Text = $"API недоступен ({ex.GetType().Name}). Проверьте запуск сервера.";
        }
    }

    public sealed record SeatDto(int SeatId, int HallId, int SeatNumber, int X, int Y, bool IsActive);
    public sealed record SeatView(int SeatNumber, string Status);
}
