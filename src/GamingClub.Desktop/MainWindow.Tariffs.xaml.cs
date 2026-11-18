using System.Globalization;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;

namespace GamingClub.Desktop;

public partial class MainWindow
{
    private sealed record AdminTariffRow(int TariffId, string Name, string TariffType,
        decimal PriceRub, int? PackageMinutes, TimeSpan? StartLocalTime,
        TimeSpan? EndLocalTime, bool IsActive);

    private async void RefreshTariffs_Click(object sender, RoutedEventArgs e) =>
        await RefreshTariffsAsync();

    private async Task RefreshTariffsAsync()
    {
        if (!isAdmin) { TariffStatus.Text = "Требуется вход администратора."; return; }
        try
        {
            using var request = Authorized(HttpMethod.Get, "/api/admin/tariffs");
            using var response = await Client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                TariffStatus.Text = $"Ошибка тарифов: HTTP {(int)response.StatusCode}.";
                return;
            }
            var rows = await response.Content.ReadFromJsonAsync<List<AdminTariffRow>>();
            TariffsTable.ItemsSource = rows ?? [];
            TariffStatus.Text = $"Тарифов: {rows?.Count ?? 0}, включая отключённые.";
        }
        catch (Exception ex) { TariffStatus.Text = $"Ошибка API ({ex.GetType().Name})."; }
    }

    private async void CreateTariff_Click(object sender, RoutedEventArgs e)
    {
        if (!isAdmin) { TariffStatus.Text = "Войдите как ADMIN."; return; }
        if (!decimal.TryParse(TariffPriceInput.Text, NumberStyles.Number,
                CultureInfo.InvariantCulture, out var price) || price < 0m ||
            (TariffMinutesInput.Text.Length > 0 &&
             !int.TryParse(TariffMinutesInput.Text, out _)))
        {
            TariffStatus.Text = "Некорректная цена или длительность. Цена: 120.00";
            return;
        }
        int? minutes = string.IsNullOrWhiteSpace(TariffMinutesInput.Text)
            ? null : int.Parse(TariffMinutesInput.Text, CultureInfo.InvariantCulture);
        var kind = (TariffTypeInput.SelectedItem as ComboBoxItem)?.Content?.ToString();
        try
        {
            using var request = Authorized(HttpMethod.Post, "/api/admin/tariffs");
            request.Content = JsonContent.Create(new
            {
                name = TariffNameInput.Text.Trim(),
                tariffType = kind,
                priceRub = price,
                packageMinutes = minutes,
                startLocalTime = string.IsNullOrWhiteSpace(TariffFromInput.Text) ? null :
                    TariffFromInput.Text.Trim(),
                endLocalTime = string.IsNullOrWhiteSpace(TariffToInput.Text) ? null :
                    TariffToInput.Text.Trim()
            });
            using var response = await Client.SendAsync(request);
            TariffStatus.Text = response.IsSuccessStatusCode
                ? "Новый тариф создан." : $"Тариф не создан: HTTP {(int)response.StatusCode}. Проверьте ограничения.";
            if (response.IsSuccessStatusCode) await RefreshTariffsAsync();
        }
        catch (Exception ex) { TariffStatus.Text = $"Ошибка API ({ex.GetType().Name})."; }
    }

    private async void DeactivateTariff_Click(object sender, RoutedEventArgs e)
    {
        if (!isAdmin) { TariffStatus.Text = "Войдите как ADMIN."; return; }
        if (!int.TryParse(DeactivateTariffIdInput.Text, out var id) || id <= 0)
        {
            TariffStatus.Text = "Укажите существующий ID тарифа.";
            return;
        }
        var confirmation = MessageBox.Show($"Отключить тариф №{id}? Существующие брони сохранятся.",
            "Подтверждение отключения", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes) return;
        try
        {
            using var request = Authorized(HttpMethod.Post, $"/api/admin/tariffs/{id}/deactivate");
            using var response = await Client.SendAsync(request);
            TariffStatus.Text = response.IsSuccessStatusCode
                ? $"Тариф №{id} отключён." : $"Операция отклонена: HTTP {(int)response.StatusCode}.";
            if (response.IsSuccessStatusCode) await RefreshTariffsAsync();
        }
        catch (Exception ex) { TariffStatus.Text = $"Ошибка API ({ex.GetType().Name})."; }
    }
}
