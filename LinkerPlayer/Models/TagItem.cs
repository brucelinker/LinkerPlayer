using System;
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

    public bool IsEditable { get; set; }
    public Action<string>? UpdateAction { get; set; }

    // Album cover property for PictureInfoItems
    public BitmapImage? AlbumCoverSource { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}