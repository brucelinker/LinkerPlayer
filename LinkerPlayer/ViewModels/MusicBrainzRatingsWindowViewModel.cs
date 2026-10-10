using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinkerPlayer.Models;

namespace LinkerPlayer.ViewModels;

public sealed class MusicBrainzRatingsWindowViewModel : ObservableObject
{
    private CancellationTokenSource? _cancellation;
    private int _total;
    private int _processed;
    private int _rated;
    private int _noRating;
    private int _errors;
    private int _skipped;
    private int _noMusicBrainzId;
    private string _currentTrack = string.Empty;
    private string _status = "Starting…";
    private bool _isRunning = true;
    private bool _cancelRequested;

    public MusicBrainzRatingsWindowViewModel()
    {
        PrimaryActionCommand = new RelayCommand(PrimaryAction, CanPrimaryAction);
    }

    public int Total
    {
        get => _total;
        private set
        {
            if (SetProperty(ref _total, value))
                OnPropertyChanged(nameof(ProgressText));
        }
    }

    public int Processed
    {
        get => _processed;
        private set
        {
            if (SetProperty(ref _processed, value))
                OnPropertyChanged(nameof(ProgressText));
        }
    }

    public int Rated
    {
        get => _rated;
        private set => SetProperty(ref _rated, value);
    }

    public int NoRating
    {
        get => _noRating;
        private set => SetProperty(ref _noRating, value);
    }

    public int Errors
    {
        get => _errors;
        private set => SetProperty(ref _errors, value);
    }

    public int Skipped
    {
        get => _skipped;
        private set => SetProperty(ref _skipped, value);
    }

    public int NoMusicBrainzId
    {
        get => _noMusicBrainzId;
        private set => SetProperty(ref _noMusicBrainzId, value);
    }

    public string CurrentTrack
    {
        get => _currentTrack;
        private set => SetProperty(ref _currentTrack, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(ActionText));
                PrimaryActionCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool CancelRequested
    {
        get => _cancelRequested;
        private set
        {
            if (SetProperty(ref _cancelRequested, value))
            {
                OnPropertyChanged(nameof(ActionText));
                PrimaryActionCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string ProgressText => $"{Processed} of {Total}";

    public string ActionText => !IsRunning ? "Close" : CancelRequested ? "Cancelling…" : "Cancel";

    public IRelayCommand PrimaryActionCommand { get; }

    public event EventHandler? CloseRequested;

    public void BindCancellation(CancellationTokenSource cancellation)
    {
        _cancellation = cancellation;
    }

    public void Apply(MusicBrainzRatingProgressUpdate update)
    {
        Total = update.Total;
        Processed = update.Processed;
        Rated = update.Rated;
        NoRating = update.NoRating;
        Errors = update.Errors;
        Skipped = update.Skipped;
        NoMusicBrainzId = update.NoMusicBrainzId;
        CurrentTrack = update.CurrentTrack;
        if (!string.IsNullOrWhiteSpace(update.Status))
            Status = update.Status;
        if (update.IsCancelled)
            CancelRequested = true;
        IsRunning = update.IsRunning;
    }

    public void Fail(string message)
    {
        Status = string.IsNullOrWhiteSpace(message) ? "Failed" : message;
        IsRunning = false;
    }

    public void RequestCancel()
    {
        if (!IsRunning || CancelRequested)
            return;

        CancelRequested = true;
        Status = "Cancelling…";
        _cancellation?.Cancel();
    }

    private bool CanPrimaryAction() => !IsRunning || !CancelRequested;

    private void PrimaryAction()
    {
        if (IsRunning)
        {
            RequestCancel();
            return;
        }

        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
