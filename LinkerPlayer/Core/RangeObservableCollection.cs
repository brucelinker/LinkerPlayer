using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace LinkerPlayer.Core;

/// <summary>
/// ObservableCollection that supports adding multiple items with a single CollectionChanged notification.
/// </summary>
public class RangeObservableCollection<T> : ObservableCollection<T>
{
    private bool _suppressNotification = false;

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (!_suppressNotification)
            base.OnCollectionChanged(e);
    }

    public void AddRange(IEnumerable<T> items)
    {
        if (items == null)
            throw new ArgumentNullException(nameof(items));

        _suppressNotification = true;
        try
        {
            foreach (T item in items)
            {
                // Use Items.Add() instead of Add() to bypass reentrancy checks
                Items.Add(item);
            }
        }
        finally
        {
            _suppressNotification = false;
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        }
    }
}
