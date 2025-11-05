using ManagedBass;
using ManagedBass.Fx;
using Microsoft.Extensions.Logging;

namespace LinkerPlayer.BassLibs;

/// <summary>
/// Service for detecting BPM (Beats Per Minute) of audio files using BASS audio library
/// </summary>
public interface IBpmDetector
{
    /// <summary>
    /// Detects the BPM of an audio file
    /// </summary>
    /// <param name="filePath">Path to the audio file</param>
    /// <param name="progress">Progress callback (0.0 to 1.0)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Detected BPM value, or null if detection failed</returns>
    Task<double?> DetectBpmAsync(string filePath, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}

public class BpmDetector : IBpmDetector
{
    private readonly ILogger<BpmDetector> _logger;

    public BpmDetector(ILogger<BpmDetector> logger)
    {
        _logger = logger;
    }

    public async Task<double?> DetectBpmAsync(string filePath, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
   {
       try
       {
           if (!File.Exists(filePath))
           {
               _logger.LogError("File not found: {FilePath}", filePath);
               return (double?)null;
           }

           _logger.LogInformation("Starting BPM detection for: {FilePath}", filePath);
           progress?.Report(0.1);

           // Create a decode stream (no playback, just for analysis)
           int stream = Bass.CreateStream(filePath, 0, 0, BassFlags.Decode | BassFlags.Float);

           if (stream == 0)
           {
               Errors error = Bass.LastError;
               _logger.LogError("Failed to create stream for BPM detection. Error: {Error}", error);
               return (double?)null;
           }

           try
           {
               progress?.Report(0.2);

               // Get stream length and info
               long length = Bass.ChannelGetLength(stream);
               double seconds = Bass.ChannelBytes2Seconds(stream, length);

               // Get channel info for debugging
               Bass.ChannelGetInfo(stream, out ChannelInfo channelInfo);
               _logger.LogInformation("Stream Info - Length: {Seconds:F2}s, Frequency: {Freq}Hz, Channels: {Channels}, Flags: {Flags}",
                seconds, channelInfo.Frequency, channelInfo.Channels, channelInfo.Flags);

               // Use BASS_FX to detect BPM
               // The BPMDecodeGet API signature in ManagedBass.Fx is:
               // double BPMDecodeGet(int channel, double startSec, double endSec, int minMaxBPM, BassFlags flags, BPMProgressProcedure? proc)
               // where minMaxBPM combines min and max as MAKELONG(min, max) = (max << 16) | (min & 0xFFFF)

               const int minBpm = 60;
               const int maxBpm = 200;

               // Combine min/max BPM into single integer (LOWORD=min, HIWORD=max)
               int minMaxBpm = (maxBpm << 16) | (minBpm & 0xFFFF);

               _logger.LogInformation("Calling BassFx.BPMDecodeGet with range {MinBpm}-{MaxBpm} BPM, minMaxBpm=0x{MinMaxBpm:X8}, analyzing {Seconds:F1}s", minBpm, maxBpm, minMaxBpm, seconds);

               // Try analyzing first 20 seconds only for faster results
               // BASS_FX BPM detection works better on shorter segments with clear beats
               double analyzeLength = Math.Min(20.0, seconds);

               double bpm = BassFx.BPMDecodeGet(
               stream,
                     0.0,   // Start from beginning
                   analyzeLength,    // Analyze first 20 seconds (or full length if shorter)
                    minMaxBpm,   // Combined min/max BPM  
                 BassFlags.FxBpmBackground,  // Use background flag - seems to work better than 0
                 null     // No progress callback
               );

               _logger.LogInformation("BassFx.BPMDecodeGet returned: {BPM}, Bass.LastError: {Error}", bpm, Bass.LastError);

               // Simulate progress since we can't get real-time updates
               for (int i = 30; i <= 90 && !cancellationToken.IsCancellationRequested; i += 10)
               {
                   progress?.Report(i / 100.0);
                   Thread.Sleep(100); // Small delay to show progress
               }

               if (cancellationToken.IsCancellationRequested)
               {
                   _logger.LogInformation("BPM detection cancelled");
                   return (double?)null;
               }

               progress?.Report(1.0);

               if (bpm > 0)
               {
                   // Round to nearest integer for cleaner display
                   double roundedBpm = Math.Round(bpm);
                   _logger.LogInformation("BPM detection successful: {BPM:F2} (rounded: {RoundedBPM})", bpm, roundedBpm);
                   return (double?)roundedBpm;
               }
               else
               {
                   Errors lastError = Bass.LastError;
                   _logger.LogError("BPM detection failed - Returned: {BPM}, Bass.LastError: {Error}, File: {FilePath}", bpm, lastError, filePath);

                   // Try to get more info about why it failed
                   if (seconds < 10)
                   {
                       _logger.LogWarning("File may be too short for BPM detection: {Seconds:F2} seconds", seconds);
                   }

                   return (double?)null;
               }
           }
           finally
           {
               // Clean up the stream
               Bass.StreamFree(stream);
           }
       }
       catch (Exception ex)
       {
           _logger.LogError(ex, "Exception during BPM detection: {Message}", ex.Message);
           return (double?)null;
       }
   }, cancellationToken);
    }
}
