using LinkerPlayer.Core;
using LinkerPlayer.Models;
using ManagedBass;
using Microsoft.Extensions.Logging;
using System.IO;

namespace LinkerPlayer.Services.Analysis;

public sealed class TrackSilenceAnalyzer : ITrackSilenceAnalyzer
{
    private readonly ISettingsManager _settingsManager;
    private readonly ILogger<TrackSilenceAnalyzer> _logger;

    public TrackSilenceAnalyzer(ISettingsManager settingsManager, ILogger<TrackSilenceAnalyzer> logger)
    {
        _settingsManager = settingsManager;
        _logger = logger;
    }

    public async Task<TrackSilenceAnalysisResult> AnalyzeAsync(MediaFile track, CancellationToken cancellationToken = default)
    {
        if (track == null)
        {
            throw new ArgumentNullException(nameof(track));
        }

        string filePath = track.Path;
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            throw new FileNotFoundException("Track file not found", filePath);
        }

        AppSettings settings = _settingsManager.Settings;
        int thresholdDb = settings.SkipSilenceThresholdDb;
        int minDurationMs = settings.SkipSilenceMinimumDurationMs;
        int leaveInitialMs = settings.SkipSilenceLeaveInitialMs;

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            int stream = Bass.CreateStream(filePath, 0, 0, BassFlags.Decode | BassFlags.Float);
            if (stream == 0)
            {
                throw new InvalidOperationException($"Failed to create decode stream: {Bass.LastError}");
            }

            try
            {
                double secondsPerSample = 1.0 / Bass.ChannelGetInfo(stream).Frequency;
                int channels = Bass.ChannelGetInfo(stream).Channels;

                // Threshold in linear scale. Using simple amplitude threshold.
                // dB here is relative to full-scale, so -60dB => 0.001.
                double thresholdAmp = Math.Pow(10.0, thresholdDb / 20.0);

                int leadingMs = ScanLeadingSilenceMs(stream, channels, secondsPerSample, thresholdAmp, minDurationMs, leaveInitialMs, cancellationToken);
                int trailingMs = ScanTrailingSilenceMs(stream, channels, secondsPerSample, thresholdAmp, minDurationMs, cancellationToken);

                return new TrackSilenceAnalysisResult
                {
                    LeadingSilenceMs = leadingMs,
                    TrailingSilenceMs = trailingMs
                };
            }
            finally
            {
                Bass.StreamFree(stream);
            }
        }, cancellationToken);
    }

    private int ScanLeadingSilenceMs(int stream, int channels, double secondsPerSample, double thresholdAmp, int minDurationMs, int leaveInitialMs, CancellationToken cancellationToken)
    {
        Bass.ChannelSetPosition(stream, 0);

        int blockSamplesPerChannel = 2048;
        int blockTotalSamples = blockSamplesPerChannel * channels;
        float[] buffer = new float[blockTotalSamples];

        long silentSamples = 0;
        long totalSamples = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int bytes = Bass.ChannelGetData(stream, buffer, blockTotalSamples * sizeof(float));
            if (bytes <= 0)
            {
                break;
            }

            int samplesRead = bytes / sizeof(float);
            bool foundNonSilent = false;

            for (int i = 0; i < samplesRead; i += channels)
            {
                totalSamples++;

                double peak = 0;
                for (int c = 0; c < channels; c++)
                {
                    double sample = Math.Abs(buffer[i + c]);
                    if (sample > peak)
                    {
                        peak = sample;
                    }
                }

                if (peak >= thresholdAmp)
                {
                    foundNonSilent = true;
                    break;
                }

                silentSamples++;
            }

            if (foundNonSilent)
            {
                break;
            }
        }

        double silentSeconds = silentSamples * secondsPerSample;
        int silentMs = (int)Math.Round(silentSeconds * 1000.0);

        if (silentMs < minDurationMs)
        {
            return 0;
        }

        if (leaveInitialMs > 0)
        {
            silentMs = Math.Max(0, silentMs - leaveInitialMs);
        }

        return silentMs;
    }

    private int ScanTrailingSilenceMs(int stream, int channels, double secondsPerSample, double thresholdAmp, int minDurationMs, CancellationToken cancellationToken)
    {
        // Seek near end and scan backwards in chunks using positions.
        // For a first implementation, do a forward scan to find last non-silent sample.
        Bass.ChannelSetPosition(stream, 0);

        int blockSamplesPerChannel = 2048;
        int blockTotalSamples = blockSamplesPerChannel * channels;
        float[] buffer = new float[blockTotalSamples];

        long lastNonSilentSample = 0;
        long totalSamples = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int bytes = Bass.ChannelGetData(stream, buffer, blockTotalSamples * sizeof(float));
            if (bytes <= 0)
            {
                break;
            }

            int samplesRead = bytes / sizeof(float);
            for (int i = 0; i < samplesRead; i += channels)
            {
                double peak = 0;
                for (int c = 0; c < channels; c++)
                {
                    double sample = Math.Abs(buffer[i + c]);
                    if (sample > peak)
                    {
                        peak = sample;
                    }
                }

                if (peak >= thresholdAmp)
                {
                    lastNonSilentSample = totalSamples;
                }

                totalSamples++;
            }
        }

        if (totalSamples == 0)
        {
            return 0;
        }

        long trailingSilentSamples = Math.Max(0, totalSamples - 1 - lastNonSilentSample);
        double trailingSeconds = trailingSilentSamples * secondsPerSample;
        int trailingMs = (int)Math.Round(trailingSeconds * 1000.0);

        if (trailingMs < minDurationMs)
        {
            return 0;
        }

        return trailingMs;
    }
}
