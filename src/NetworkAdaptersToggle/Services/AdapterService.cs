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

    public async Task<List<NetworkAdapter>> GetAllAdaptersAsync()
    {
        var json = await RunPowerShellAsync(
            "Get-NetAdapter | Select-Object Name, Status, InterfaceIndex, InterfaceDescription, MacAddress, LinkSpeed | ConvertTo-Json -Compress");

        if (string.IsNullOrWhiteSpace(json))
            return [];

        json = json.Trim();

        // PowerShell returns a single object (not array) when there's only one adapter
        if (json.StartsWith('['))
            return JsonSerializer.Deserialize<List<NetworkAdapter>>(json, JsonOptions) ?? [];

        var single = JsonSerializer.Deserialize<NetworkAdapter>(json, JsonOptions);
        return single is null ? [] : [single];
    }

    public async Task<(bool Success, string Error)> EnableAdapterAsync(int interfaceIndex)
    {
        return await ToggleAdapterAsync(interfaceIndex, enable: true);
    }

    public async Task<(bool Success, string Error)> DisableAdapterAsync(int interfaceIndex)
    {
        return await ToggleAdapterAsync(interfaceIndex, enable: false);
    }

    public async Task<ToggleResult> EnableAndVerifyAsync(int interfaceIndex, string adapterName)
    {
        var (success, error) = await EnableAdapterAsync(interfaceIndex);
        if (!success)
            return new ToggleResult(interfaceIndex, adapterName, false, error);

        await Task.Delay(1500);
        return await VerifyStateAsync(interfaceIndex, adapterName, expectedDisabled: false);
    }

    public async Task<ToggleResult> DisableAndVerifyAsync(int interfaceIndex, string adapterName)
    {
        var (success, error) = await DisableAdapterAsync(interfaceIndex);
        if (!success)
            return new ToggleResult(interfaceIndex, adapterName, false, error);

        await Task.Delay(1500);
        return await VerifyStateAsync(interfaceIndex, adapterName, expectedDisabled: true);
    }

    private async Task<ToggleResult> VerifyStateAsync(int interfaceIndex, string adapterName, bool expectedDisabled)
    {
        var adapters = await GetAllAdaptersAsync();
        var adapter = adapters.FirstOrDefault(a => a.InterfaceIndex == interfaceIndex);

        if (adapter is null)
        {
            // Some adapters vanish when disabled — try with -IncludeHidden
            if (expectedDisabled)
            {
                var hiddenJson = await RunPowerShellAsync(
                    $"Get-NetAdapter -IncludeHidden | Where-Object {{ $_.InterfaceIndex -eq {interfaceIndex} }} | Select-Object Name, Status, InterfaceIndex | ConvertTo-Json -Compress");

                if (!string.IsNullOrWhiteSpace(hiddenJson))
                {
                    var hidden = JsonSerializer.Deserialize<NetworkAdapter>(hiddenJson.Trim(), JsonOptions);
                    if (hidden?.IsDisabled == true)
                        return new ToggleResult(interfaceIndex, adapterName, true, "Disabled successfully");
                }

                // Adapter truly gone after disable — consider it successful
                return new ToggleResult(interfaceIndex, adapterName, true, "Disabled (adapter hidden)");
            }

            return new ToggleResult(interfaceIndex, adapterName, false, "Adapter not found after operation");
        }

        if (expectedDisabled && adapter.IsDisabled)
            return new ToggleResult(interfaceIndex, adapterName, true, "Disabled successfully");

        if (!expectedDisabled && adapter.IsUp)
            return new ToggleResult(interfaceIndex, adapterName, true, "Enabled successfully");

        return new ToggleResult(interfaceIndex, adapterName, false,
            $"Expected {(expectedDisabled ? "Disabled" : "Up")} but got {adapter.Status}");
    }

    private async Task<(bool Success, string Error)> ToggleAdapterAsync(int interfaceIndex, bool enable)
    {
        var cmdlet = enable ? "Enable-NetAdapter" : "Disable-NetAdapter";
        var command = $"{cmdlet} -InterfaceIndex {interfaceIndex} -Confirm:$false";

        var (exitCode, _, stderr) = await RunPowerShellFullAsync(command);

        if (exitCode != 0 && !string.IsNullOrWhiteSpace(stderr))
            return (false, stderr.Trim());

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
        await process.WaitForExitAsync();

        return (process.ExitCode, stdout, stderr);
    }
}

public record ToggleResult(int InterfaceIndex, string AdapterName, bool Success, string Message);
