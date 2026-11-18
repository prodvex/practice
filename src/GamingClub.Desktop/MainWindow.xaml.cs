using System.Collections.ObjectModel;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Windows;

namespace GamingClub.Desktop;

public partial class MainWindow : Window
{
    // For Android emulator localhost is different. Desktop defaults to local API.
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(10) };
    public ObservableCollection<SeatView> Seats { get; } = new();
    private string? token;
    private bool isAdmin;
    private static string ApiBase =>
        (Environment.GetEnvironmentVariable("GAMINGCLUB_API_URL") ?? "http://localhost:5080").TrimEnd('/');

    private HttpRequestMessage Authorized(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, ApiBase + path);
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AuthStatus.Text = "Проверка учётных данных...";
            using var response = await Client.PostAsJsonAsync(
                ApiBase + "/api/auth/login",
                new { login = LoginInput.Text.Trim(), password = PasswordInput.Password });
            if (!response.IsSuccessStatusCode)
            {
                Logout();
                AuthStatus.Text = "Ошибка входа или неверный пароль.";
                return;
            }
            var account = await response.Content.ReadFromJsonAsync<LoginResponse>();
            if (account is null || account.Role != "ADMIN" || string.IsNullOrEmpty(account.AccessToken))
            {
                Logout();
                AuthStatus.Text = "Требуется роль ADMIN.";
                return;
            }
            token = account.AccessToken; // Memory only; do not write token to disk or logs.
            isAdmin = true;
            PasswordInput.Clear();
            AuthStatus.Text = $"Администратор: {account.Login}";
        }
        catch (Exception ex)
        {
            Logout();
            AuthStatus.Text = $"Сервер недоступен ({ex.GetType().Name}).";
        }
    }

    private void Logout_Click(object sender, RoutedEventArgs e)
    {
        Logout();
        AuthStatus.Text = "Выход выполнен.";
    }

    private void Logout()
    {
        token = null;
        isAdmin = false;
        PasswordInput.Clear();
    }

    private async void Report_Click(object sender, RoutedEventArgs e)
    {
        if (!isAdmin) { ReportStatus.Text = "Сначала войдите как ADMIN."; return; }
        if (!DateTimeOffset.TryParse(ReportFrom.Text, out var from) ||
            !DateTimeOffset.TryParse(ReportTo.Text, out var to) ||
            !ReportFrom.Text.Trim().EndsWith("Z", StringComparison.OrdinalIgnoreCase) ||
            !ReportTo.Text.Trim().EndsWith("Z", StringComparison.OrdinalIgnoreCase) ||
            to <= from || to - from > TimeSpan.FromDays(31))
        {
            ReportStatus.Text = "Введите корректный UTC-период до 31 дня.";
            return;
        }
        try
        {
            var query = $"/api/admin/reports/occupancy?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}";
            using var request = Authorized(HttpMethod.Get, query);
            using var response = await Client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                ReportStatus.Text = $"Отчёт недоступен: HTTP {(int)response.StatusCode}.";
                return;
            }
            var report = await response.Content.ReadFromJsonAsync<OccupancyResponse>();
            ReportStatus.Text = report is null ? "Пустой ответ." :
                $"Мест: {report.SeatCount}, занято {report.ReservedMinutes:0.##} минут, загрузка {report.OccupancyPercent:0.##}%";
        }
        catch (Exception ex)
        {
            ReportStatus.Text = $"Ошибка API ({ex.GetType().Name}).";
        }
    }

    public sealed record LoginResponse(string AccessToken, string TokenType,
        int ExpiresIn, int UserId, string Login, string Role, string DisplayName);
    public sealed record OccupancyResponse(DateTime FromUtc, DateTime ToUtc,
        int SeatCount, decimal ReservedMinutes, decimal CapacityMinutes, decimal OccupancyPercent);

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
            && !System.Text.RegularExpressions.Regex.IsMatch(ExtensionTimeInput.Text, @"[+-]\d{2}:\d{2}$"))
        {
            ExtensionStatus.Text = "Дата должна содержать UTC Z или смещение +03:00.";
            return;
        }
        if (!isAdmin || string.IsNullOrWhiteSpace(token))
        {
            ExtensionStatus.Text = "Для продления войдите как ADMIN.";
            return;
        }
        try
        {
            ExtensionStatus.Text = "Продление...";
            using var request = Authorized(HttpMethod.Post,
                $"/api/sessions/{bookingId}/extend");
            request.Content = JsonContent.Create(new { newEndUtc = end });
            using var response = await Client.SendAsync(request);
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
