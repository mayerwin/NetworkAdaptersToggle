using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace NetworkAdaptersToggle.Models;

public sealed class NetworkAdapter : INotifyPropertyChanged
{
    private bool _isSelected;

    [JsonPropertyName("Name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("Status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("InterfaceIndex")]
    public int InterfaceIndex { get; set; }

    [JsonPropertyName("InterfaceDescription")]
    public string InterfaceDescription { get; set; } = "";

    [JsonPropertyName("MacAddress")]
    public string MacAddress { get; set; } = "";

    [JsonPropertyName("LinkSpeed")]
    public string LinkSpeed { get; set; } = "";

    [JsonIgnore]
    public bool IsUp => Status == "Up";

    [JsonIgnore]
    public bool IsDisabled => Status == "Disabled";

    [JsonIgnore]
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
