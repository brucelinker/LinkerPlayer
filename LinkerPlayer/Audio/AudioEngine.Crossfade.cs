using ManagedBass;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;

namespace LinkerPlayer.Audio;

public partial class AudioEngine
{
    // Crossfade
    private int _crossfadeOldDecodeStream;
    private int _crossfadeNewDecodeStream;
    private Timer? _crossfadeTimer;
    private long _crossfadeVersion;
    private string _crossfadePendingPath = string.Empty;

    private void StopCrossfadeTimer(string reason)
    {
        Timer? timerToDispose = _crossfadeTimer;
        if (timerToDispose != null)
        {
            try
            {
                _logger.LogDebug("Crossfade: disposing timer (Reason={Reason})", reason);
                timerToDispose.Dispose();
            }
            catch
            {
            }
            _crossfadeTimer = null;
        }
    }

    private static double EvaluateFade(FadeCurveShape shape, double t)
    {
        double clamped = Math.Clamp(t, 0d, 1d);

        if (shape == FadeCurveShape.Linear)
        {
            return clamped;
        }

        if (shape == FadeCurveShape.Cosine)
        {
            return 0.5d - (0.5d * Math.Cos(Math.PI * clamped));
        }

        if (shape == FadeCurveShape.Sine)
        {
            return Math.Sin((Math.PI / 2d) * clamped);
        }

        if (shape == FadeCurveShape.Logarithmic)
        {
            const double k = 9d;
            return Math.Log10(1d + (k * clamped)) / Math.Log10(1d + k);
        }

        return clamped;
    }

    public bool TryBeginCrossfade(string nextTrackPath, double nextTrackStartSeconds, int fadeOutMs, int fadeInMs, FadeCurveShape curveShape)
    {
        lock (_engineSync)
        {
            if (string.IsNullOrWhiteSpace(nextTrackPath))
            {
                return false;
            }

            if (_mixerStream == 0 || _decodeStream == 0)
            {
                return false;
            }

            if (fadeOutMs <= 0 && fadeInMs <= 0)
            {
                return false;
            }

            StopCrossfadeTimer("begin");

            long version = Interlocked.Increment(ref _crossfadeVersion);

            _crossfadeOldDecodeStream = _decodeStream;
            _crossfadeNewDecodeStream = Bass.CreateStream(nextTrackPath, 0, 0, Flags: BassFlags.Decode);
            if (_crossfadeNewDecodeStream == 0)
            {
                _logger.LogWarning("Crossfade: failed to create decode stream for '{Path}' ({Error})", nextTrackPath, Bass.LastError);
                _crossfadeOldDecodeStream = 0;
                return false;
            }

            if (nextTrackStartSeconds > 0)
            {
                long bytePosition = Bass.ChannelSeconds2Bytes(_crossfadeNewDecodeStream, nextTrackStartSeconds);
                if (bytePosition > 0)
                {
                    Bass.ChannelSetPosition(_crossfadeNewDecodeStream, bytePosition);
                }
            }

            Bass.ChannelGetInfo(_crossfadeNewDecodeStream, out ChannelInfo newDecodeInfo);
            Bass.ChannelGetInfo(_crossfadeOldDecodeStream, out ChannelInfo oldDecodeInfo);

            BassFlags addFlags = BassFlags.Default;
            if (newDecodeInfo.Channels != oldDecodeInfo.Channels)
            {
                addFlags |= BassFlags.MixerChanDownMix;
            }

            if (!ManagedBass.Mix.BassMix.MixerAddChannel(_mixerStream, _crossfadeNewDecodeStream, addFlags))
            {
                _logger.LogWarning("Crossfade: failed to add new channel to mixer ({Error})", Bass.LastError);
                Bass.StreamFree(_crossfadeNewDecodeStream);
                _crossfadeNewDecodeStream = 0;
                _crossfadeOldDecodeStream = 0;
                return false;
            }

            // Switch engine "now playing" identity immediately so UI (seekbar/length) matches what becomes audible.
            _decodeStream = _crossfadeNewDecodeStream;
            _crossfadePendingPath = nextTrackPath;
            LoadedTrackPath = nextTrackPath;

            try
            {
                long lenBytes = Bass.ChannelGetLength(_decodeStream);
                if (lenBytes > 0)
                {
                    CurrentTrackLength = Bass.ChannelBytes2Seconds(_decodeStream, lenBytes);
                }
            }
            catch
            {
            }

            // Ensure the new track fades in from 0 and the old fades out from 1.
            Bass.ChannelSetAttribute(_crossfadeOldDecodeStream, ChannelAttribute.Volume, 1f);
            Bass.ChannelSetAttribute(_decodeStream, ChannelAttribute.Volume, 0f);

            int tickMs = 15;
            DateTimeOffset startedUtc = DateTimeOffset.UtcNow;

            _crossfadeTimer = new System.Threading.Timer(_ =>
            {
                try
                {
                    lock (_engineSync)
                    {
                        if (Interlocked.Read(ref _crossfadeVersion) != version)
                        {
                            return;
                        }

                        double elapsedMs = (DateTimeOffset.UtcNow - startedUtc).TotalMilliseconds;
                        double outT = fadeOutMs <= 0 ? 1d : (elapsedMs / fadeOutMs);
                        double inT = fadeInMs <= 0 ? 1d : (elapsedMs / fadeInMs);

                        double outGain = 1d - EvaluateFade(curveShape, outT);
                        double inGain = EvaluateFade(curveShape, inT);

                        float outVol = (float)Math.Clamp(outGain, 0d, 1d);
                        float inVol = (float)Math.Clamp(inGain, 0d, 1d);

                        if (_crossfadeOldDecodeStream != 0)
                        {
                            Bass.ChannelSetAttribute(_crossfadeOldDecodeStream, ChannelAttribute.Volume, outVol);
                        }
                        if (_decodeStream != 0)
                        {
                            Bass.ChannelSetAttribute(_decodeStream, ChannelAttribute.Volume, inVol);
                        }

                        bool done = (elapsedMs >= Math.Max(fadeOutMs, fadeInMs));
                        if (!done)
                        {
                            return;
                        }

                        StopCrossfadeTimer("commit");

                        if (_crossfadeOldDecodeStream != 0)
                        {
                            try
                            {
                                ManagedBass.Mix.BassMix.MixerRemoveChannel(_crossfadeOldDecodeStream);
                            }
                            catch
                            {
                            }
                            Bass.StreamFree(_crossfadeOldDecodeStream);
                            _crossfadeOldDecodeStream = 0;
                        }

                        // At this point _decodeStream is already the new stream; just clear crossfade handle.
                        _crossfadeNewDecodeStream = 0;

                        string committedPath = _crossfadePendingPath;
                        _crossfadePendingPath = string.Empty;

                        _ = Task.Run(() =>
                        {
                            try
                            {
                                OnCrossfadeCommitted?.Invoke(committedPath);
                            }
                            catch
                            {
                            }
                        });
                    }
                }
                catch
                {
                }
            }, null, 0, tickMs);

            return true;
        }
    }

    public bool TryFadeOutAndStop(int fadeOutMs, FadeCurveShape curveShape)
    {
        lock (_engineSync)
        {
            if (fadeOutMs <= 0)
            {
                return false;
            }

            if (_decodeStream == 0)
            {
                return false;
            }

            StopCrossfadeTimer("fadeout-stop");

            long version = Interlocked.Increment(ref _crossfadeVersion);
            int tickMs = 15;
            DateTimeOffset startedUtc = DateTimeOffset.UtcNow;

            int targetStream = _decodeStream;

            _crossfadeTimer = new System.Threading.Timer(_ =>
            {
                try
                {
                    lock (_engineSync)
                    {
                        if (Interlocked.Read(ref _crossfadeVersion) != version)
                        {
                            return;
                        }

                        double elapsedMs = (DateTimeOffset.UtcNow - startedUtc).TotalMilliseconds;
                        double t = elapsedMs / fadeOutMs;
                        double gain = 1d - EvaluateFade(curveShape, t);
                        float vol = (float)Math.Clamp(gain, 0d, 1d);

                        Bass.ChannelSetAttribute(targetStream, ChannelAttribute.Volume, vol);

                        if (elapsedMs < fadeOutMs)
                        {
                            return;
                        }

                        StopCrossfadeTimer("fadeout-stop-complete");
                        _ = Task.Run(() =>
                        {
                            try
                            {
                                Stop();
                            }
                            catch
                            {
                            }
                        });
                    }
                }
                catch
                {
                }
            }, null, 0, tickMs);

            return true;
        }
    }
}
