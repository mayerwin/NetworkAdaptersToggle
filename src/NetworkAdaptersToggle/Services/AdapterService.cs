using System.Diagnostics;
using System.Text.Json;
using NetworkAdaptersToggle.Models;

namespace NetworkAdaptersToggle.Services;

public sealed class AdapterService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private const int ProcessTimeoutMs = 30_000;
    private const int VerifyDelayMs = 1500;

    public async Task<List<NetworkAdapter>> GetAllAdaptersAsync()
    {
        var json = await RunPowerShellAsync(
            "Get-NetAdapter | Select-Object Name, Status, InterfaceIndex, InterfaceDescription, MacAddress, LinkSpeed | ConvertTo-Json -Compress");

        if (string.IsNullOrWhiteSpace(json))
            return [];

        json = json.Trim();

        try
        {
            // PowerShell returns a single object (not array) when there's only one adapter
            if (json.StartsWith('['))
                return JsonSerializer.Deserialize<List<NetworkAdapter>>(json, JsonOptions) ?? [];

            var single = JsonSerializer.Deserialize<NetworkAdapter>(json, JsonOptions);
            return single is null ? [] : [single];
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to parse adapter data: {ex.Message}", ex);
        }
    }

    public async Task<ToggleResult> EnableAndVerifyAsync(int interfaceIndex, string adapterName)
    {
        var (success, error) = await ToggleAdapterAsync(interfaceIndex, enable: true);
        if (!success)
            return new ToggleResult(interfaceIndex, adapterName, false, error);

        await Task.Delay(VerifyDelayMs);
        return await VerifyStateAsync(interfaceIndex, adapterName, expectedDisabled: false);
    }

    public async Task<ToggleResult> DisableAndVerifyAsync(int interfaceIndex, string adapterName)
    {
        var (success, error) = await ToggleAdapterAsync(interfaceIndex, enable: false);
        if (!success)
            return new ToggleResult(interfaceIndex, adapterName, false, error);

        await Task.Delay(VerifyDelayMs);
        return await VerifyStateAsync(interfaceIndex, adapterName, expectedDisabled: true);
    }

    public async Task<ToggleResult> UninstallAdapterAsync(int interfaceIndex, string adapterName)
    {
        // First try: get PnP device ID from the adapter (works for physical adapters)
        var (exitCode, stdout, _) = await RunPowerShellFullAsync(
            $"(Get-NetAdapter -InterfaceIndex {interfaceIndex} -IncludeHidden -ErrorAction Stop).PnpDeviceID");

        var pnpId = stdout?.Trim();

        if (exitCode == 0 && !string.IsNullOrWhiteSpace(pnpId))
        {
            // Physical adapter — use pnputil to remove the device
            var (uninstExitCode, uninstOut, uninstStderr) = await RunPowerShellFullAsync(
                $"pnputil /remove-device '{pnpId}'");

            if (uninstExitCode != 0)
            {
                var errorMsg = string.IsNullOrWhiteSpace(uninstStderr)
                    ? $"pnputil exited with code {uninstExitCode}"
                    : uninstStderr.Trim();
                return new ToggleResult(interfaceIndex, adapterName, false, errorMsg);
            }

            return new ToggleResult(interfaceIndex, adapterName, true, "Uninstalled successfully");
        }

        // Fallback: try finding a matching PnP device by description (some virtual adapters)
        var (pnpExit, pnpOut, pnpErr) = await RunPowerShellFullAsync(
            $"$desc = (Get-NetAdapter -InterfaceIndex {interfaceIndex} -IncludeHidden).InterfaceDescription; " +
            $"$dev = Get-PnpDevice -FriendlyName $desc -ErrorAction SilentlyContinue | Select-Object -First 1; " +
            $"if ($dev) {{ pnputil /remove-device $dev.InstanceId }} else {{ exit 99 }}");

        if (pnpExit == 0)
            return new ToggleResult(interfaceIndex, adapterName, true, "Uninstalled successfully");

        if (pnpExit == 99)
            return new ToggleResult(interfaceIndex, adapterName, false,
                "This is a virtual adapter with no removable device. Remove it through Hyper-V Manager or its managing service.");

        var fallbackErr = string.IsNullOrWhiteSpace(pnpErr) ? $"Uninstall failed (exit code {pnpExit})" : pnpErr.Trim();
        return new ToggleResult(interfaceIndex, adapterName, false, fallbackErr);
    }

    private async Task<ToggleResult> VerifyStateAsync(int interfaceIndex, string adapterName, bool expectedDisabled)
    {
        List<NetworkAdapter> adapters;
        try
        {
            adapters = await GetAllAdaptersAsync();
        }
        catch (Exception ex)
        {
            return new ToggleResult(interfaceIndex, adapterName, false, $"Verification failed: {ex.Message}");
        }

        var adapter = adapters.FirstOrDefault(a => a.InterfaceIndex == interfaceIndex);

        if (adapter is null)
        {
            if (expectedDisabled)
            {
                // Some adapters vanish when disabled — try with -IncludeHidden
                try
                {
                    var hiddenJson = await RunPowerShellAsync(
                        $"Get-NetAdapter -IncludeHidden | Where-Object {{ $_.InterfaceIndex -eq {interfaceIndex} }} | Select-Object Name, Status, InterfaceIndex | ConvertTo-Json -Compress");

                    if (!string.IsNullOrWhiteSpace(hiddenJson))
                    {
                        var hidden = JsonSerializer.Deserialize<NetworkAdapter>(hiddenJson.Trim(), JsonOptions);
                        if (hidden?.IsDisabled == true)
                            return new ToggleResult(interfaceIndex, adapterName, true, "Disabled successfully");
                    }
                }
                catch
                {
                    // Fall through
                }

                // Adapter truly gone after disable — consider it successful
                return new ToggleResult(interfaceIndex, adapterName, true, "Disabled (adapter hidden)");
            }

            return new ToggleResult(interfaceIndex, adapterName, false, "Adapter not found after operation");
        }

        if (expectedDisabled && adapter.IsDisabled)
            return new ToggleResult(interfaceIndex, adapterName, true, "Disabled successfully");

        // After enabling, adapter may be "Disconnected" (no network) rather than "Up".
        // Any non-Disabled state means the enable succeeded.
        if (!expectedDisabled && !adapter.IsDisabled)
            return new ToggleResult(interfaceIndex, adapterName, true, $"Enabled successfully (status: {adapter.Status})");

        return new ToggleResult(interfaceIndex, adapterName, false,
            $"Expected {(expectedDisabled ? "Disabled" : "enabled (non-Disabled)")} but got {adapter.Status}");
    }

    private async Task<(bool Success, string Error)> ToggleAdapterAsync(int interfaceIndex, bool enable)
    {
        // Enable-NetAdapter and Disable-NetAdapter do NOT accept -InterfaceIndex directly.
        // We must pipe from Get-NetAdapter.
        var cmdlet = enable ? "Enable-NetAdapter" : "Disable-NetAdapter";
        var command = $"Get-NetAdapter -InterfaceIndex {interfaceIndex} | {cmdlet} -Confirm:$false";

        var (exitCode, _, stderr) = await RunPowerShellFullAsync(command);

        if (exitCode != 0)
            return (false, string.IsNullOrWhiteSpace(stderr)
                ? $"PowerShell exited with code {exitCode}"
                : stderr.Trim());

        return (true, "");
    }

    private static async Task<string> RunPowerShellAsync(string command)
    {
        var (_, stdout, _) = await RunPowerShellFullAsync(command);
        return stdout;
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunPowerShellFullAsync(string command)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NoLogo -Command \"{command}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(ProcessTimeoutMs);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return (-1, "", "PowerShell process timed out after 30 seconds");
        }

        return (process.ExitCode, stdout, stderr);
    }
}

public record ToggleResult(int InterfaceIndex, string AdapterName, bool Success, string Message);
