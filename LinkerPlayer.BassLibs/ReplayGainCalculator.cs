using ManagedBass;
using Microsoft.Extensions.Logging;

namespace LinkerPlayer.BassLibs;

/// <summary>
/// Result of ReplayGain calculation
/// </summary>
public class ReplayGainResult
{
    /// <summary>
    /// Track gain in dB (adjustment needed to reach -18 LUFS)
    /// </summary>
    public double TrackGain
    {
        get; set;
    }

    /// <summary>
    /// Track peak sample value (0.0 to 1.0+)
    /// </summary>
    public double TrackPeak
    {
        get; set;
    }

    /// <summary>
    /// Integrated loudness in LUFS (for reference)
    /// </summary>
    public double IntegratedLoudness
    {
        get; set;
    }

    /// <summary>
    /// Loudness range in LU (for reference)
    /// </summary>
    public double LoudnessRange
    {
        get; set;
    }

    /// <summary>
    /// Whether the measurement was successful
    /// </summary>
    public bool Success
    {
        get; set;
    }

    /// <summary>
    /// Error message if measurement failed
    /// </summary>
    public string? ErrorMessage
    {
        get; set;
    }
}

/// <summary>
/// Service for calculating ReplayGain values using BassLoud
/// </summary>
public interface IReplayGainCalculator
{
    /// <summary>
    /// Calculate ReplayGain for an audio file
    /// </summary>
    /// <param name="filePath">Path to the audio file</param>
    /// <param name="progress">Progress callback (0.0 to 1.0)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>ReplayGain calculation result</returns>
    Task<ReplayGainResult> CalculateReplayGainAsync(
        string filePath,
        IProgress<double>? progress = null,
 CancellationToken cancellationToken = default);
}

public class ReplayGainCalculator : IReplayGainCalculator
{
    private readonly ILogger<ReplayGainCalculator> _logger;

    public ReplayGainCalculator(ILogger<ReplayGainCalculator> logger)
    {
        _logger = logger;
    }

    public async Task<ReplayGainResult> CalculateReplayGainAsync(
      string filePath, IProgress<double>? progress = null,
      CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            ReplayGainResult result = new ReplayGainResult { Success = false };

            try
            {
                if (!File.Exists(filePath))
                {
                    result.ErrorMessage = $"File not found: {filePath}";
                    _logger.LogError(result.ErrorMessage);
                    return result;
                }

                _logger.LogInformation("Starting ReplayGain calculation for: {FilePath}", filePath);
                progress?.Report(0.05);

                // Create a decode stream (no playback, just for analysis)
                int stream = Bass.CreateStream(filePath, 0, 0, BassFlags.Decode | BassFlags.Float);

                if (stream == 0)
                {
                    Errors error = Bass.LastError;
                    result.ErrorMessage = $"Failed to create stream: {error}";
                    _logger.LogError(result.ErrorMessage);
                    return result;
                }

                try
                {
                    progress?.Report(0.1);

                    // Get stream info
                    long lengthBytes = Bass.ChannelGetLength(stream);
                    double seconds = Bass.ChannelBytes2Seconds(stream, lengthBytes);

                    Bass.ChannelGetInfo(stream, out ChannelInfo channelInfo);
                    _logger.LogInformation("Stream Info - Length: {Seconds:F2}s, Frequency: {Freq}Hz, Channels: {Channels}",
                        seconds, channelInfo.Frequency, channelInfo.Channels);

                    // Start loudness scanning (returns a handle, not bool)
                    int loudnessHandle = BassLoud.Start(stream,
                     (int)(BassLoud.LoudnessFlags.Integrated | BassLoud.LoudnessFlags.Range | BassLoud.LoudnessFlags.TruePeak),
                          -1000); // DSP priority

                    if (loudnessHandle == 0)
                    {
                        result.ErrorMessage = $"Failed to start loudness scanner: {Bass.LastError}";
                        _logger.LogError(result.ErrorMessage);
                        return result;
                    }

                    try
                    {
                        // Process the entire file
                        const int bufferSize = 20000; // Process in chunks
                        byte[] buffer = new byte[bufferSize];
                        long totalBytes = lengthBytes;
                        long processedBytes = 0;
                        int bytesRead;

                        _logger.LogInformation("Processing {Seconds:F1} seconds of audio...", seconds);

                        while ((bytesRead = Bass.ChannelGetData(stream, buffer, bufferSize)) > 0)
                        {
                            if (cancellationToken.IsCancellationRequested)
                            {
                                result.ErrorMessage = "Calculation cancelled";
                                _logger.LogInformation(result.ErrorMessage);
                                return result;
                            }

                            processedBytes += bytesRead;

                            // Report progress (10% to 90%)
                            double fileProgress = (double)processedBytes / totalBytes;
                            progress?.Report(0.1 + (fileProgress * 0.8));
                        }

                        progress?.Report(0.95);

                        // Get the integrated loudness
                        if (!BassLoud.GetLevel(loudnessHandle, (int)BassLoud.LoudnessMode.Integrated, out float integrated))
                        {
                            result.ErrorMessage = $"Failed to get integrated loudness: {Bass.LastError}";
                            _logger.LogError(result.ErrorMessage);
                            return result;
                        }

                        // Get the loudness range
                        if (!BassLoud.GetLevel(loudnessHandle, (int)BassLoud.LoudnessMode.Range, out float range))
                        {
                            result.ErrorMessage = $"Failed to get loudness range: {Bass.LastError}";
                            _logger.LogError(result.ErrorMessage);
                            return result;
                        }

                        // Get the true peak
                        if (!BassLoud.GetLevel(loudnessHandle, (int)BassLoud.LoudnessMode.TruePeak, out float truePeak))
                        {
                            result.ErrorMessage = $"Failed to get true peak: {Bass.LastError}";
                            _logger.LogError(result.ErrorMessage);
                            return result;
                        }

                        // Calculate ReplayGain from integrated loudness
                        double trackGain = BassLoud.LufsToReplayGain(integrated);

                        result.Success = true;
                        result.TrackGain = trackGain;
                        result.TrackPeak = truePeak;
                        result.IntegratedLoudness = integrated;
                        result.LoudnessRange = range;

                        progress?.Report(1.0);

                        _logger.LogInformation(
                            "ReplayGain calculation successful: Gain={Gain:F2} dB, Peak={Peak:F6}, Loudness={Loudness:F2} LUFS, Range={Range:F2} LU",
                            result.TrackGain, result.TrackPeak, result.IntegratedLoudness, result.LoudnessRange);

                        return result;
                    }
                    finally
                    {
                        // Stop the loudness measurement
                        BassLoud.Stop(loudnessHandle);
                    }
                }
                finally
                {
                    // Free the stream
                    Bass.StreamFree(stream);
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Exception during ReplayGain calculation: {ex.Message}";
                _logger.LogError(ex, result.ErrorMessage);
                return result;
            }
        }, cancellationToken); // End of Task.Run
    }
}
