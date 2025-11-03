using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Windows;

namespace LinkerPlayer.ViewModels;

/// <summary>
/// BPM Detection and ReplayGain Calculation commands (partial class)
/// </summary>
public partial class PropertiesViewModel
{
    [RelayCommand(CanExecute = nameof(CanDetectBpm))]
    private async Task DetectBpmAsync()
    {
        if (_bpmDetector == null)
        {
            MessageBox.Show("BPM detection is not available. The BASS audio library may not be properly initialized.",
           "BPM Detection", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_sharedDataModel.SelectedTrack == null)
        {
            return;
        }

        string filePath = _sharedDataModel.SelectedTrack.Path;

        try
        {
            IsBpmDetecting = true;
            BpmDetectionProgress = 0;
            BpmDetectionStatus = "Analyzing audio file...";

            _bpmDetectionCts = new CancellationTokenSource();

            var progress = new Progress<double>(value =>
         {
             BpmDetectionProgress = value * 100;
             BpmDetectionStatus = $"Detecting BPM... {BpmDetectionProgress:F0}%";
         });

            double? detectedBpm = await _bpmDetector.DetectBpmAsync(filePath, progress, _bpmDetectionCts.Token);

            if (_bpmDetectionCts.Token.IsCancellationRequested)
            {
                BpmDetectionStatus = "Detection cancelled";
                return;
            }

            if (detectedBpm.HasValue)
            {
                var bpmItem = MetadataItems.FirstOrDefault(item => item.Name == "Beats Per Minute");
                if (bpmItem != null)
                {
                    bpmItem.Value = ((uint)detectedBpm.Value).ToString();
                    BpmDetectionStatus = $"BPM detected: {detectedBpm.Value:F0}";
                }
                else
                {
                    BpmDetectionStatus = $"BPM detected: {detectedBpm.Value:F0} (unable to update field)";
                }

                _logger.LogInformation("BPM detection completed: {BPM}", detectedBpm.Value);
            }
            else
            {
                BpmDetectionStatus = "Could not detect BPM";
                MessageBox.Show("Unable to detect BPM for this track. The file may not have a clear rhythmic pattern.",
                             "BPM Detection", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during BPM detection");
            BpmDetectionStatus = "Detection failed";
            MessageBox.Show($"An error occurred during BPM detection:\n{ex.Message}",
                         "BPM Detection Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBpmDetecting = false;
            _bpmDetectionCts?.Dispose();
            _bpmDetectionCts = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelBpmDetection))]
    private void CancelBpmDetection()
    {
        _bpmDetectionCts?.Cancel();
        BpmDetectionStatus = "Cancelling...";
    }

    private bool CanDetectBpm() => !IsBpmDetecting && _sharedDataModel.SelectedTrack != null && _bpmDetector != null;
    private bool CanCancelBpmDetection() => IsBpmDetecting;

    [RelayCommand(CanExecute = nameof(CanCalculateReplayGain))]
    private async Task CalculateReplayGainAsync()
    {
        if (_replayGainCalculator == null)
        {
            MessageBox.Show("ReplayGain calculation is not available. The BASS audio library may not be properly initialized.",
           "ReplayGain Calculation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_sharedDataModel.SelectedTrack == null)
        {
            return;
        }

        string filePath = _sharedDataModel.SelectedTrack.Path;

        try
        {
            IsReplayGainCalculating = true;
            ReplayGainCalculationProgress = 0;
            ReplayGainCalculationStatus = "Analyzing audio file...";

            _replayGainCalculationCts = new CancellationTokenSource();

            var progress = new Progress<double>(value =>
          {
              ReplayGainCalculationProgress = value * 100;
              ReplayGainCalculationStatus = $"Calculating ReplayGain... {ReplayGainCalculationProgress:F0}%";
          });

            var result = await _replayGainCalculator.CalculateReplayGainAsync(filePath, progress, _replayGainCalculationCts.Token);

            if (_replayGainCalculationCts.Token.IsCancellationRequested)
            {
                ReplayGainCalculationStatus = "Calculation cancelled";
                return;
            }

            if (result.Success)
            {
                var trackGainItem = ReplayGainItems.FirstOrDefault(item =>
                  item.Name == "ReplayGain Track Gain" || item.Name == "Track Gain");
                var trackPeakItem = ReplayGainItems.FirstOrDefault(item =>
             item.Name == "ReplayGain Track Peak" || item.Name == "Track Peak");

                if (trackGainItem != null && trackPeakItem != null)
                {
                    string gainStr = result.TrackGain >= 0
                 ? $"+{result.TrackGain:F2} dB"
                     : $"{result.TrackGain:F2} dB";

                    trackGainItem.Value = gainStr;
                    trackPeakItem.Value = result.TrackPeak.ToString("F6");

                    ReplayGainCalculationStatus = $"ReplayGain calculated: {gainStr}, Peak: {result.TrackPeak:F6}";

                    _logger.LogInformation(
          "ReplayGain calculation completed: Gain={Gain:F2} dB, Peak={Peak:F6}, Loudness={Loudness:F2} LUFS",
             result.TrackGain, result.TrackPeak, result.IntegratedLoudness);
                }
                else
                {
                    ReplayGainCalculationStatus = $"ReplayGain calculated but unable to update fields";
                    _logger.LogWarning("ReplayGain items not found in collection");
                }
            }
            else
            {
                ReplayGainCalculationStatus = "Calculation failed";
                MessageBox.Show($"Unable to calculate ReplayGain:\n{result.ErrorMessage}",
             "ReplayGain Calculation", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during ReplayGain calculation");
            ReplayGainCalculationStatus = "Calculation failed";
            MessageBox.Show($"An error occurred during ReplayGain calculation:\n{ex.Message}",
              "ReplayGain Calculation Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsReplayGainCalculating = false;
            _replayGainCalculationCts?.Dispose();
            _replayGainCalculationCts = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelReplayGainCalculation))]
    private void CancelReplayGainCalculation()
    {
        _replayGainCalculationCts?.Cancel();
        ReplayGainCalculationStatus = "Cancelling...";
    }

    private bool CanCalculateReplayGain() => !IsReplayGainCalculating && _sharedDataModel.SelectedTrack != null && _replayGainCalculator != null;
    private bool CanCancelReplayGainCalculation() => IsReplayGainCalculating;
}
