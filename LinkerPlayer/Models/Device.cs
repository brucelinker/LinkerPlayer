namespace LinkerPlayer.Models;

public enum OutputDeviceType { DirectSound, Wasapi }

public record Device
{
    public string Name { get; init; }
    public OutputDeviceType Type { get; init; }
    public int Index { get; init; }
    public bool IsDefault { get; init; }
    public Device(string name, OutputDeviceType type, int index, bool isDefault = false)
    {
        Name = name;
        Type = type;
        Index = index;
        IsDefault = isDefault;
    }
}
