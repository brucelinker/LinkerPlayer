using CommunityToolkit.Mvvm.ComponentModel;
using LinkerPlayer.Models;
using ManagedBass;
using ManagedBass.Fx;
using Microsoft.Extensions.Logging;

namespace LinkerPlayer.Audio;

public partial class AudioEngine
{
    [ObservableProperty] private bool _eqEnabled = true;

    private readonly List<EqualizerBandSettings> _equalizerBands =
    [
        new(32.0f,0f,1.0f),
        new(64.0f,0f,1.0f),
        new(125.0f,0f,1.0f),
        new(250.0f,0f,1.0f),
        new(500.0f,0f,1.0f),
        new(1000.0f,0f,1.0f),
        new(2000.0f,0f,1.0f),
        new(4000.0f,0f,1.0f),
        new(8000.0f,0f,1.0f),
        new(16000.0f,0f,1.0f)
    ];

    private bool _eqInitialized;
    private int[] _eqFxHandles = [];

    public bool IsEqualizerInitialized => _eqInitialized;

    public bool InitializeEqualizer()
    {
        CleanupEqualizer();

        if (CurrentStream == 0)
        {
            _logger.LogWarning("Cannot initialize equalizer: No stream loaded");
            return false;
        }

        _eqFxHandles = new int[_equalizerBands.Count];
        int successCount = 0;

        for (int i = 0; i < _equalizerBands.Count; i++)
        {
            float freq = _equalizerBands[i].Frequency;
            int fxHandle = Bass.ChannelSetFX(CurrentStream, EffectType.PeakEQ, 0);

            if (fxHandle == 0)
            {
                _logger.LogError($"Failed to set FX for {freq}Hz: {Bass.LastError}");
                continue;
            }

            PeakEQParameters eqParams = new()
            {
                fCenter = freq,
                fGain = _equalizerBands[i].Gain,
                fBandwidth = _equalizerBands[i].Bandwidth,
                lChannel = FXChannelFlags.All
            };

            if (!Bass.FXSetParameters(fxHandle, eqParams))
            {
                _logger.LogError($"Failed to set EQ params for {freq}Hz: {Bass.LastError}");
                Bass.ChannelRemoveFX(CurrentStream, fxHandle);
                _eqFxHandles[i] = 0;
            }
            else
            {
                _eqFxHandles[i] = fxHandle;
                successCount++;
            }
        }

        _eqInitialized = successCount > 0;
        return _eqInitialized;
    }

    public List<EqualizerBandSettings> GetBandsList()
    {
        // Return a COPY of the bands, not the original reference
        return _equalizerBands.Select(band => new EqualizerBandSettings(
        band.Frequency,
        band.Gain,
        band.Bandwidth
        )).ToList();
    }

    public void SetBandsList(List<EqualizerBandSettings> bands)
    {
        _equalizerBands.Clear();

        // Create NEW band objects from the input, don't store references
        foreach (EqualizerBandSettings band in bands)
        {
            _equalizerBands.Add(new EqualizerBandSettings(
            band.Frequency,
            band.Gain,
            band.Bandwidth
           ));
        }

        // Re-initialize EQ if it's already set up
        if (EqEnabled && CurrentStream != 0)
        {
            InitializeEqualizer();
        }
    }

    public float GetBandGain(int index)
    {
        if (index >= 0 && index < _equalizerBands.Count)
        {
            return _equalizerBands[index].Gain;
        }
        return 0f;
    }

    public void SetBandGainByIndex(int index, float gain)
    {
        if (index >= 0 && index < _equalizerBands.Count)
        {
            SetBandGain(_equalizerBands[index].Frequency, gain);
        }
    }

    public void SetBandGain(float frequency, float gain)
    {
        // Store the gain setting regardless of EQ state
        int bandIndex = _equalizerBands.FindIndex(b => Math.Abs(b.Frequency - frequency) < 0.1f);
        if (bandIndex != -1)
        {
            _equalizerBands[bandIndex].Gain = AudioMath.ClampGain(gain);
        }

        // Only apply to actual EQ if it's enabled and initialized
        if (!EqEnabled || !_eqInitialized || _eqFxHandles.Length == 0 || CurrentStream == 0)
        {
            return;
        }

        int bandIdx = _equalizerBands.FindIndex(b => Math.Abs(b.Frequency - frequency) < 0.1f);
        if (bandIdx == -1 || bandIdx >= _eqFxHandles.Length || _eqFxHandles[bandIdx] == 0)
        {
            return;
        }

        float clampedGain = AudioMath.ClampGain(gain);

        PeakEQParameters eqParams = new()
        {
            fCenter = frequency,
            fBandwidth = _equalizerBands[bandIdx].Bandwidth,
            fGain = clampedGain,
            lChannel = FXChannelFlags.All
        };

        if (Bass.FXSetParameters(_eqFxHandles[bandIdx], eqParams))
        {
            _equalizerBands[bandIdx].Gain = clampedGain;
        }
        else
        {
            _logger.LogError($"Failed to update EQ gain for {frequency} Hz: {Bass.LastError}");
        }
    }

    private void CleanupEqualizer()
    {
        if (_eqFxHandles.Length > 0 && CurrentStream != 0)
        {
            foreach (int fxHandle in _eqFxHandles)
            {
                if (fxHandle != 0)
                {
                    Bass.ChannelRemoveFX(CurrentStream, fxHandle);
                }
            }
        }
        _eqFxHandles = [];
        _eqInitialized = false;
    }
}
