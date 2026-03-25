using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using NetworkAdaptersToggle.Models;
using NetworkAdaptersToggle.Services;

namespace NetworkAdaptersToggle;

public partial class MainWindow : Window
{
    private readonly AdapterService _adapterService = new();
    private readonly IniSettingsService _settingsService = new();
    private readonly ObservableCollection<NetworkAdapter> _adapters = [];
    private bool _suppressSelectionSave;

    private static readonly SolidColorBrush SuccessFgBrush = new(Color.FromRgb(0x50, 0xfa, 0x7b));
    private static readonly SolidColorBrush ErrorFgBrush = new(Color.FromRgb(0xff, 0x55, 0x55));
    private static readonly SolidColorBrush NeutralFgBrush = new(Color.FromRgb(0x7f, 0x84, 0x9c));

    private static readonly SolidColorBrush SuccessBgBrush = new(Color.FromRgb(0x1e, 0x3a, 0x2e));
    private static readonly SolidColorBrush ErrorBgBrush = new(Color.FromRgb(0x3a, 0x1e, 0x1e));
    private static readonly SolidColorBrush NeutralBgBrush = new(Color.FromRgb(0x28, 0x2a, 0x3a));
    private static readonly SolidColorBrush SuccessBorderBrush = new(Color.FromRgb(0x2a, 0x5a, 0x3a));
    private static readonly SolidColorBrush ErrorBorderBrush = new(Color.FromRgb(0x5a, 0x2a, 0x2a));
    private static readonly SolidColorBrush NeutralBorderBrush = new(Color.FromRgb(0x3b, 0x3d, 0x52));

    public MainWindow()
    {
        InitializeComponent();
        AdapterGrid.ItemsSource = _adapters;
        Loaded += MainWindow_Loaded;
        StateChanged += MainWindow_StateChanged;
    }

    // ═══ Window chrome handlers ═══

    private void BtnMinimize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void BtnMaximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        // Add padding when maximized to prevent content from going under taskbar edges
        RootBorder.Padding = WindowState == WindowState.Maximized
            ? new Thickness(7)
            : new Thickness(0);

        // Update maximize/restore icon
        MaxRestoreIcon.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
    }

    // ═══ Rounded corner clipping for DataGrid border ═══

    private void GridBorder_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var border = (Border)sender;
        border.Clip = new RectangleGeometry(
            new Rect(0, 0, border.ActualWidth, border.ActualHeight), 6, 6);
    }

    // ═══ Lifecycle ═══

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
                adapter.IsSelected = savedSelections.Contains(adapter.InterfaceIndex);
                _adapters.Add(adapter);
            }

            _suppressSelectionSave = false;
            UpdateHeaderCheckBox();

            if (_adapters.Count == 0)
            {
                SetStatusNeutral("No network adapters found.");
            }
            else
            {
                var enabled = _adapters.Count(a => !a.IsDisabled);
                var disabled = _adapters.Count(a => a.IsDisabled);
                SetStatusSuccess($"{_adapters.Count} adapter(s) found.  {enabled} enabled, {disabled} disabled.");
            }
        }
        catch (Exception ex)
        {
            SetStatusError($"Error loading adapters: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ═══ Action handlers ═══

    private async void BtnEnable_Click(object sender, RoutedEventArgs e)
    {
        var selected = _adapters.Where(a => a.IsSelected && a.IsDisabled).ToList();

        if (selected.Count == 0)
        {
            SetStatusNeutral("No disabled adapters selected to enable.");
            return;
        }

        SetBusy(true, $"Enabling {selected.Count} adapter(s)...");

        var results = new List<ToggleResult>();
        foreach (var adapter in selected)
        {
            SetStatusNeutral($"Enabling {adapter.Name}...");
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
            SetStatusNeutral("No enabled adapters selected to disable.");
            return;
        }

        SetBusy(true, $"Disabling {selected.Count} adapter(s)...");

        var results = new List<ToggleResult>();
        foreach (var adapter in selected)
        {
            SetStatusNeutral($"Disabling {adapter.Name}...");
            var result = await _adapterService.DisableAndVerifyAsync(adapter.InterfaceIndex, adapter.Name);
            results.Add(result);
        }

        await RefreshAdaptersAsync();
        ReportResults(results, "disabled");
    }

    private async void BtnUninstall_Click(object sender, RoutedEventArgs e)
    {
        var selected = _adapters.Where(a => a.IsSelected).ToList();

        if (selected.Count == 0)
        {
            SetStatusNeutral("No adapters selected to uninstall.");
            return;
        }

        var names = string.Join("\n", selected.Select(a => $"  \u2022 {a.Name}"));
        var confirm = MessageBox.Show(
            $"Are you sure you want to uninstall {selected.Count} adapter(s)?\n\n{names}\n\nThis action may require a reboot to fully take effect.",
            "Confirm Uninstall",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return;

        SetBusy(true, $"Uninstalling {selected.Count} adapter(s)...");

        var results = new List<ToggleResult>();
        foreach (var adapter in selected)
        {
            SetStatusNeutral($"Uninstalling {adapter.Name}...");
            var result = await _adapterService.UninstallAdapterAsync(adapter.InterfaceIndex, adapter.Name);
            results.Add(result);
        }

        await RefreshAdaptersAsync();
        ReportResults(results, "uninstalled");
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAdaptersAsync();
    }

    // ═══ Row click → toggle checkbox ═══

    private void AdapterGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject dep) return;

        // Don't interfere with header clicks (sorting, header checkbox)
        if (FindParent<DataGridColumnHeader>(dep) is not null) return;

        // Find which row was clicked
        var row = FindParent<DataGridRow>(dep);
        if (row?.DataContext is NetworkAdapter adapter)
        {
            adapter.IsSelected = !adapter.IsSelected;
            if (!_suppressSelectionSave)
            {
                SaveSelections();
                UpdateHeaderCheckBox();
            }
            e.Handled = true; // Prevent DataGrid from also processing (avoids double-toggle on checkbox)
        }
    }

    private void OnAdapterSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (!_suppressSelectionSave)
        {
            SaveSelections();
            UpdateHeaderCheckBox();
        }
    }

    // ═══ Header checkbox (select all / none) ═══

    private void HeaderCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox checkBox) return;

        var newState = checkBox.IsChecked ?? true;

        _suppressSelectionSave = true;
        foreach (var adapter in _adapters)
            adapter.IsSelected = newState;
        _suppressSelectionSave = false;
        SaveSelections();
    }

    private void UpdateHeaderCheckBox()
    {
        var headerCheckBox = FindHeaderCheckBox();
        if (headerCheckBox is null) return;

        var selectedCount = _adapters.Count(a => a.IsSelected);
        if (selectedCount == 0)
            headerCheckBox.IsChecked = false;
        else if (selectedCount == _adapters.Count)
            headerCheckBox.IsChecked = true;
        else
            headerCheckBox.IsChecked = null;
    }

    private CheckBox? FindHeaderCheckBox()
    {
        if (AdapterGrid.Columns.Count == 0) return null;
        var headerPresenter = FindVisualChild<DataGridColumnHeadersPresenter>(AdapterGrid);
        if (headerPresenter is null) return null;
        return FindVisualChild<CheckBox>(headerPresenter);
    }

    // ═══ GitHub link ═══

    private void BtnGitHub_Click(object sender, RoutedEventArgs e)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "https://github.com/mayerwin/NetworkAdaptersToggle",
            UseShellExecute = true
        };
        System.Diagnostics.Process.Start(psi);
    }

    // ═══ Persistence ═══

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

    // ═══ Status bar ═══

    private void ReportResults(List<ToggleResult> results, string action)
    {
        var succeeded = results.Count(r => r.Success);
        var failed = results.Count(r => !r.Success);

        if (failed == 0)
        {
            SetStatusSuccess($"\u2714  {succeeded}/{results.Count} adapter(s) {action} successfully.");
        }
        else
        {
            var failMessages = string.Join("  |  ",
                results.Where(r => !r.Success).Select(r => $"{r.AdapterName}: {r.Message}"));
            SetStatusError($"\u2716  {succeeded}/{results.Count} {action}.  Failed: {failMessages}");
        }
    }

    private void SetBusy(bool busy, string? message = null)
    {
        BtnEnable.IsEnabled = !busy;
        BtnDisable.IsEnabled = !busy;
        BtnUninstall.IsEnabled = !busy;
        BtnRefresh.IsEnabled = !busy;

        if (message is not null)
            SetStatusNeutral(message);
    }

    private void SetStatusSuccess(string message)
    {
        StatusText.Text = message;
        StatusText.Foreground = SuccessFgBrush;
        StatusBorder.Background = SuccessBgBrush;
        StatusBorder.BorderBrush = SuccessBorderBrush;
    }

    private void SetStatusError(string message)
    {
        StatusText.Text = message;
        StatusText.Foreground = ErrorFgBrush;
        StatusBorder.Background = ErrorBgBrush;
        StatusBorder.BorderBrush = ErrorBorderBrush;
    }

    private void SetStatusNeutral(string message)
    {
        StatusText.Text = message;
        StatusText.Foreground = NeutralFgBrush;
        StatusBorder.Background = NeutralBgBrush;
        StatusBorder.BorderBrush = NeutralBorderBrush;
    }

    // ═══ Visual tree helpers ═══

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        var current = VisualTreeHelper.GetParent(child);
        while (current is not null)
        {
            if (current is T found) return found;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) return found;
            var result = FindVisualChild<T>(child);
            if (result is not null) return result;
        }
        return null;
    }
}
