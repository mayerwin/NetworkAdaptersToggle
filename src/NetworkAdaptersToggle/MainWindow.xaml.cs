using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using NetworkAdaptersToggle.Models;
using NetworkAdaptersToggle.Services;

namespace NetworkAdaptersToggle;

public partial class MainWindow : Window
{
    private readonly AdapterService _adapterService = new();
    private readonly IniSettingsService _settingsService = new();
    private readonly ObservableCollection<NetworkAdapter> _adapters = [];
    private bool _suppressSelectionSave;

    public MainWindow()
    {
        InitializeComponent();
        AdapterGrid.ItemsSource = _adapters;
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshAdaptersAsync();
    }

    private async Task RefreshAdaptersAsync()
    {
        SetBusy(true, "Loading adapters...");

        try
        {
            var adapters = await _adapterService.GetAllAdaptersAsync();
            var savedSelections = _settingsService.LoadSelectedIndexes();

            _suppressSelectionSave = true;
            _adapters.Clear();

            foreach (var adapter in adapters.OrderBy(a => a.Name))
            {
                // Restore selection from INI (default: unchecked)
                adapter.IsSelected = savedSelections.Contains(adapter.InterfaceIndex);
                _adapters.Add(adapter);
            }

            _suppressSelectionSave = false;

            if (_adapters.Count == 0)
                SetStatus("No network adapters found.");
            else
                SetStatus($"{_adapters.Count} adapter(s) found. {_adapters.Count(a => a.IsUp)} up, {_adapters.Count(a => a.IsDisabled)} disabled.");
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void BtnEnable_Click(object sender, RoutedEventArgs e)
    {
        var selected = _adapters.Where(a => a.IsSelected && !a.IsUp).ToList();

        if (selected.Count == 0)
        {
            SetStatus("No disabled adapters selected to enable.");
            return;
        }

        SetBusy(true, $"Enabling {selected.Count} adapter(s)...");

        var results = new List<ToggleResult>();
        foreach (var adapter in selected)
        {
            SetStatus($"Enabling {adapter.Name}...");
            var result = await _adapterService.EnableAndVerifyAsync(adapter.InterfaceIndex, adapter.Name);
            results.Add(result);
        }

        await RefreshAdaptersAsync();
        ReportResults(results, "enabled");
    }

    private async void BtnDisable_Click(object sender, RoutedEventArgs e)
    {
        var selected = _adapters.Where(a => a.IsSelected && !a.IsDisabled).ToList();

        if (selected.Count == 0)
        {
            SetStatus("No active adapters selected to disable.");
            return;
        }

        SetBusy(true, $"Disabling {selected.Count} adapter(s)...");

        var results = new List<ToggleResult>();
        foreach (var adapter in selected)
        {
            SetStatus($"Disabling {adapter.Name}...");
            var result = await _adapterService.DisableAndVerifyAsync(adapter.InterfaceIndex, adapter.Name);
            results.Add(result);
        }

        await RefreshAdaptersAsync();
        ReportResults(results, "disabled");
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAdaptersAsync();
    }

    private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
    {
        _suppressSelectionSave = true;
        foreach (var adapter in _adapters)
            adapter.IsSelected = true;
        _suppressSelectionSave = false;
        SaveSelections();
    }

    private void BtnSelectNone_Click(object sender, RoutedEventArgs e)
    {
        _suppressSelectionSave = true;
        foreach (var adapter in _adapters)
            adapter.IsSelected = false;
        _suppressSelectionSave = false;
        SaveSelections();
    }

    private void OnAdapterSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (!_suppressSelectionSave)
            SaveSelections();
    }

    private void SaveSelections()
    {
        try
        {
            var selectedIndexes = _adapters
                .Where(a => a.IsSelected)
                .Select(a => a.InterfaceIndex);
            _settingsService.SaveSelectedIndexes(selectedIndexes);
        }
        catch
        {
            // Non-critical — don't disrupt the user
        }
    }

    private void ReportResults(List<ToggleResult> results, string action)
    {
        var succeeded = results.Count(r => r.Success);
        var failed = results.Count(r => !r.Success);

        if (failed == 0)
        {
            SetStatus($"{succeeded}/{results.Count} adapter(s) {action} successfully.");
        }
        else
        {
            var failedNames = string.Join(", ", results.Where(r => !r.Success).Select(r => r.AdapterName));
            SetStatus($"{succeeded}/{results.Count} {action}. Failed: {failedNames}");
        }
    }

    private void SetBusy(bool busy, string? message = null)
    {
        BtnEnable.IsEnabled = !busy;
        BtnDisable.IsEnabled = !busy;
        BtnRefresh.IsEnabled = !busy;
        BtnSelectAll.IsEnabled = !busy;
        BtnSelectNone.IsEnabled = !busy;

        if (message is not null)
            StatusText.Text = message;
    }

    private void SetStatus(string message)
    {
        StatusText.Text = message;
    }
}
