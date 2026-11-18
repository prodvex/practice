using System.Globalization;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace GamingClub.Desktop;

public partial class MainWindow
{
    private async void ExportReport_Click(object sender, RoutedEventArgs e)
    {
        if (!isAdmin) { ReportStatus.Text = "Для экспорта войдите как ADMIN."; return; }
        if (!DateTimeOffset.TryParse(ReportFrom.Text, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var from) ||
            !DateTimeOffset.TryParse(ReportTo.Text, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var to) ||
            !ReportFrom.Text.Trim().EndsWith("Z", StringComparison.OrdinalIgnoreCase) ||
            !ReportTo.Text.Trim().EndsWith("Z", StringComparison.OrdinalIgnoreCase) ||
            to <= from || to - from > TimeSpan.FromDays(31))
        {
            ReportStatus.Text = "Укажите корректный UTC-интервал до 31 дня.";
            return;
        }
        var picker = new SaveFileDialog
        {
            Title = "Сохранить отчёт GamingClub",
            Filter = "CSV файл (*.csv)|*.csv",
            FileName = "gamingclub-occupancy.csv",
            DefaultExt = "csv",
            AddExtension = true
        };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            var query = $"/api/admin/reports/occupancy.csv?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}";
            using var request = Authorized(HttpMethod.Get, query);
            using var response = await Client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                ReportStatus.Text = $"Экспорт отклонён: HTTP {(int)response.StatusCode}.";
                return;
            }
            var bytes = await response.Content.ReadAsByteArrayAsync();
            await File.WriteAllBytesAsync(picker.FileName, bytes);
            ReportStatus.Text = "Отчёт CSV сохранён.";
        }
        catch (Exception ex) { ReportStatus.Text = $"Ошибка экспорта ({ex.GetType().Name})."; }
    }
}
