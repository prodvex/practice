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


    private async void CancelSelectedBooking_Click(object sender, RoutedEventArgs e)
    {
        if (!isAdmin)
        {
            BookingsStatus.Text = "Для отмены войдите как ADMIN.";
            return;
        }
        if (BookingsTable.SelectedItem is not AdminBookingRow booking)
        {
            BookingsStatus.Text = "Выберите бронь в таблице.";
            return;
        }
        if (booking.Status != "ACTIVE" || booking.StartUtc <= DateTime.UtcNow)
        {
            BookingsStatus.Text = "Можно отменять только будущую активную бронь.";
            return;
        }

        var choice = MessageBox.Show(
            $"Отменить бронь №{booking.BookingId} (ПК №{booking.SeatNumber})?\n" +
            "Неоплаченные начисления будут аннулированы. Для оплаченных бронирований операция не допускается.",
            "Подтвердите отмену", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (choice != MessageBoxResult.Yes) return;

        try
        {
            using var request = Authorized(HttpMethod.Post,
                $"/api/admin/bookings/{booking.BookingId}/cancel");
            using var response = await Client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                BookingsStatus.Text = $"Отмена отклонена: HTTP {(int)response.StatusCode}.";
                return;
            }
            await RefreshBookingsAsync();
            BookingsStatus.Text = $"Бронь №{booking.BookingId} отменена; неоплаченные счета аннулированы.";
        }
        catch (Exception ex)
        {
            BookingsStatus.Text = $"Ошибка отмены ({ex.GetType().Name}).";
        }
    }

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
