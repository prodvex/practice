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

    private async void Extend_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(BookingIdInput.Text, out int bookingId) || bookingId <= 0 ||
            !DateTimeOffset.TryParse(ExtensionTimeInput.Text,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var end))
        {
            ExtensionStatus.Text = "Укажите корректный ID брони и время UTC.";
            return;
        }
        // Explicit timezone offset avoids silently interpreting dates as local time.
        if (!ExtensionTimeInput.Text.Trim().EndsWith("Z", StringComparison.OrdinalIgnoreCase)
            && !System.Text.RegularExpressions.Regex.IsMatch(ExtensionTimeInput.Text, @"[+-]\\d{2}:\\d{2}$"))
        {
            ExtensionStatus.Text = "Дата должна содержать UTC Z или смещение +03:00.";
            return;
        }
        try
        {
            ExtensionStatus.Text = "Продление...";
            var url = Environment.GetEnvironmentVariable("GAMINGCLUB_API_URL") ?? "http://localhost:5080";
            var response = await Client.PostAsJsonAsync(
                $"{url.TrimEnd('/')}/api/dev/sessions/{bookingId}/extend",
                new { newEndUtc = end });
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<ExtensionResponse>();
                ExtensionStatus.Text = result is null ? "Продление выполнено." :
                    $"Продлено до {result.NewEndUtc:yyyy-MM-dd HH:mm} UTC; доплата {result.ExtraChargeRub:0.00} руб.";
            }
            else
            {
                ExtensionStatus.Text = $"Отклонено: HTTP {(int)response.StatusCode}. Проверьте бронь и конфликт времени.";
            }
        }
        catch (Exception ex)
        {
            ExtensionStatus.Text = $"Сервер недоступен ({ex.GetType().Name}).";
        }
    }

    public sealed record ExtensionResponse(bool Success, string Message, DateTime NewEndUtc, decimal ExtraChargeRub);

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
