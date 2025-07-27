using System;
using System.ComponentModel;

namespace LinkerPlayer.Models;

public class TagItem : INotifyPropertyChanged
{
    private string _value;

    public string Name { get; set; }

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
    public Action<string> UpdateAction { get; set; }

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}