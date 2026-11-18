using System.Net.Http.Json;
using System.Windows;

namespace GamingClub.Desktop;

public partial class MainWindow
{
    private sealed record AdminBookingRow(int BookingId, int UserId, string Login,
        int SeatId, int SeatNumber, int TariffId, DateTime StartUtc,
        DateTime EndUtc, string Status);

    private async void RefreshBookings_Click(object sender, RoutedEventArgs e) =>
        await RefreshBookingsAsync();

    private async Task RefreshBookingsAsync()
    {
        if (!isAdmin) { BookingsStatus.Text = "Требуется вход администратора."; return; }
        try
        {
            using var request = Authorized(HttpMethod.Get, "/api/admin/bookings");
            using var response = await Client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                BookingsStatus.Text = $"Не удалось получить брони: HTTP {(int)response.StatusCode}.";
                return;
            }
            var rows = await response.Content.ReadFromJsonAsync<List<AdminBookingRow>>();
            BookingsTable.ItemsSource = rows ?? [];
            BookingsStatus.Text = $"Загружено бронирований: {rows?.Count ?? 0}; максимум 100.";
        }
        catch (Exception ex) { BookingsStatus.Text = $"Ошибка API ({ex.GetType().Name})."; }
    }
}
