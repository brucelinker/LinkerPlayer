using System.ComponentModel;
using System.Windows.Media.Imaging;

namespace LinkerPlayer.Models;

public class TagItem : INotifyPropertyChanged
{
    private string _value = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Value
    {
        get => _value;
        set
        {
            if (_value != value)
            {
                _value = value;
                OnPropertyChanged(nameof(Value));
            }
        }
    }

    public bool IsEditable
    {
        get; set;
    }

    /// <summary>
    /// Indicates this item had multiple distinct values across selected files.
    /// </summary>
    public bool HasMultipleValues { get; set; }

    /// <summary>
    /// The original display value when the item was loaded (used to detect edits on multi-value fields).
    /// </summary>
    public string OriginalValue { get; set; } = string.Empty;
    public Action<string>? UpdateAction
    {
        get; set;
    }
    public BitmapImage? AlbumCoverSource
    {
        get; set;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
