using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using TransfertPhotos.Services;

namespace TransfertPhotos;

public partial class MainWindow : Window
{
    private readonly MtpService _mtp = new();
    private CancellationTokenSource? _cts;

    public ObservableCollection<string> Log { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        if (DestBox.Text.Length == 0)
        {
            string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            DestBox.Text = Path.Combine(pictures, "Smartphone");
        }

        SetBusy(true, "Recherche d'appareils connectés...");
        try
        {
            var devices = await Task.Run(_mtp.GetDevices);
            DeviceBox.ItemsSource = devices;
            if (devices.Count == 0)
                Status("Aucun appareil détecté. Déverrouillez le téléphone, choisissez « Transfert de fichiers USB (MTP) » puis cliquez sur Actualiser.");
            else
            {
                DeviceBox.SelectedIndex = 0;
                Status(devices.Count + " appareil(s) détecté(s).");
            }
        }
        catch (Exception ex)
        {
            AddLog("Erreur lors de la détection : " + ex.Message);
            Status("La détection des appareils a échoué.");
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceBox.SelectedItem is not PhoneInfo device)
        {
            Status("Sélectionnez d'abord un appareil.");
            return;
        }
        var log = new Progress<string>(AddLog);
        var cts = new CancellationTokenSource();
        _cts = cts;
        SetBusy(true, "Analyse du téléphone en cours...");
        try
        {
            var result = await Task.Run(() => _mtp.Scan(device, cts.Token, log));
            Status($"Analyse terminée : {result.PhotoCount:N0} photos et {result.VideoCount:N0} vidéos ({FormatSize(result.TotalBytes)}). Cliquez sur Transférer.");
            AddLog($"Analyse terminée : {result.PhotoCount:N0} photos, {result.VideoCount:N0} vidéos, {FormatSize(result.TotalBytes)}.");
        }
        catch (OperationCanceledException)
        {
            Status("Analyse annulée.");
        }
        catch (Exception ex)
        {
            AddLog("Erreur pendant l'analyse : " + ex.Message);
            Status("L'analyse a échoué — consultez le journal.");
        }
        finally
        {
            SetBusy(false, null);
            _cts = null;
        }
    }

    private async void Transfer_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceBox.SelectedItem is not PhoneInfo device)
        {
            Status("Sélectionnez d'abord un appareil.");
            return;
        }
        string dest = DestBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(dest))
        {
            Status("Choisissez un dossier de destination.");
            return;
        }

        var options = new TransferOptions(DateCheck.IsChecked == true, SkipDupCheck.IsChecked == true);
        var cts = new CancellationTokenSource();
        _cts = cts;
        SetBusy(true, "Transfert en cours...");
        var log = new Progress<string>(AddLog);
        try
        {
            var result = await Task.Run(() => _mtp.Transfer(device, dest, options, cts.Token, log));
            Status($"Terminé : {result.Copied:N0} fichier(s) copié(s), {result.Skipped:N0} doublon(s) ignoré(s), {FormatSize(result.Bytes)} transférés.");
            AddLog("Transfert terminé.");
        }
        catch (OperationCanceledException)
        {
            Status("Transfert annulé.");
            AddLog("Transfert annulé par l'utilisateur.");
        }
        catch (Exception ex)
        {
            AddLog("Erreur pendant le transfert : " + ex.Message);
            Status("Le transfert a échoué — consultez le journal.");
        }
        finally
        {
            SetBusy(false, null);
            _cts = null;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choisir le dossier de destination"
        };
        if (Directory.Exists(DestBox.Text)) dlg.InitialDirectory = DestBox.Text;
        if (dlg.ShowDialog(this) == true) DestBox.Text = dlg.FolderName;
    }

    private void OpenDest_Click(object sender, RoutedEventArgs e)
    {
        string path = DestBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            Status("Le dossier de destination n'existe pas encore.");
            return;
        }
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    private void SetBusy(bool busy, string? message)
    {
        ScanBtn.IsEnabled = !busy;
        TransferBtn.IsEnabled = !busy;
        CancelBtn.IsEnabled = busy;
        DeviceBox.IsEnabled = !busy;
        RefreshBtn.IsEnabled = !busy;
        Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (message != null) StatusText.Text = message;
    }

    private void Status(string text) => StatusText.Text = text;

    private void AddLog(string line)
    {
        Log.Add($"[{DateTime.Now:HH:mm:ss}] {line}");
        while (Log.Count > 400) Log.RemoveAt(0);
        LogList.ScrollIntoView(Log[^1]);
    }

    private static string FormatSize(long bytes)
    {
        string[] units = { "o", "Ko", "Mo", "Go", "To" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.#} {units[unit]}";
    }
}
