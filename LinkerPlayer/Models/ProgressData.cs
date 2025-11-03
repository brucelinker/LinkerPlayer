using CommunityToolkit.Mvvm.ComponentModel;

namespace LinkerPlayer.Models;

public partial class ProgressData : ObservableObject
{
    [ObservableProperty] private bool _isProcessing;
    [ObservableProperty] private int _processedTracks;
    [ObservableProperty] private int _totalTracks;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private string _phase = ""; // Adding, Metadata, Saving
}
