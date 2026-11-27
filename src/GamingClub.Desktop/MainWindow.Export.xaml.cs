using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace GamingClub.Desktop;

public partial class MainWindow
{
    private async void ExportReport_Click(object sender, RoutedEventArgs e)
    {
        if (!isAdmin) { ReportStatus.Text = "Для экспорта войдите как ADMIN."; return; }
        if (!TryGetReportPeriod(out var period)) return;
        var kind = ReportKind.SelectedIndex == 1 ? "revenue" : "occupancy";
        var picker = new SaveFileDialog
        {
            Title = "Сохранить отчёт GamingClub",
            Filter = "CSV файл (*.csv)|*.csv",
            FileName = $"gamingclub-{kind}.csv",
            DefaultExt = "csv",
            AddExtension = true
        };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            var query = $"/api/admin/reports/{kind}.csv{period}";
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
