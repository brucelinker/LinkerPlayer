using CommunityToolkit.Mvvm.ComponentModel;
using LinkerPlayer.Models;
using ManagedBass;
using ManagedBass.Fx;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Collections.Generic;

namespace LinkerPlayer.Audio;

public partial class AudioEngine
{
    [ObservableProperty] private bool _eqEnabled = true;

    private readonly List<EqualizerBandSettings> _equalizerBands = new List<EqualizerBandSettings>
    {
        new EqualizerBandSettings(32.0f, 0f, 1.0f),
        new EqualizerBandSettings(64.0f, 0f, 1.0f),
        new EqualizerBandSettings(125.0f, 0f, 1.0f),
        new EqualizerBandSettings(250.0f, 0f, 1.0f),
        new EqualizerBandSettings(500.0f, 0f, 1.0f),
        new EqualizerBandSettings(1000.0f, 0f, 1.0f),
        new EqualizerBandSettings(2000.0f, 0f, 1.0f),
        new EqualizerBandSettings(4000.0f, 0f, 1.0f),
        new EqualizerBandSettings(8000.0f, 0f, 1.0f),
        new EqualizerBandSettings(16000.0f, 0f, 1.0f)
    };

    private bool _eqInitializedLocal;
    private int[] _eqFxHandlesLocal = Array.Empty<int>();

    public bool IsEqualizerInitialized => _eqInitializedLocal;

    public bool InitializeEqualizer()
    {
        CleanupEqualizer();

        if (CurrentStream == 0)
        {
            _logger.LogWarning("Cannot initialize equalizer: No stream loaded");
            return false;
        }

        _eqFxHandlesLocal = new int[_equalizerBands.Count];
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

            PeakEQParameters eqParams = new PeakEQParameters()
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
                _eqFxHandlesLocal[i] = 0;
            }
            else
            {
                _eqFxHandlesLocal[i] = fxHandle;
                successCount++;
            }
        }

        _eqInitializedLocal = successCount > 0;
        return _eqInitializedLocal;
    }

    public List<EqualizerBandSettings> GetBandsList()
    {
        return _equalizerBands.Select(band => new EqualizerBandSettings(band.Frequency, band.Gain, band.Bandwidth)).ToList();
    }

    public void SetBandsList(List<EqualizerBandSettings> bands)
    {
        _equalizerBands.Clear();

        foreach (EqualizerBandSettings band in bands)
        {
            _equalizerBands.Add(new EqualizerBandSettings(band.Frequency, band.Gain, band.Bandwidth));
        }

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
        int bandIndex = _equalizerBands.FindIndex(b => Math.Abs(b.Frequency - frequency) < 0.1f);
        if (bandIndex != -1)
        {
            _equalizerBands[bandIndex].Gain = AudioMath.ClampGain(gain);
        }

        if (!EqEnabled || !_eqInitializedLocal || _eqFxHandlesLocal.Length == 0 || CurrentStream == 0)
        {
            return;
        }

        int bandIdx = _equalizerBands.FindIndex(b => Math.Abs(b.Frequency - frequency) < 0.1f);
        if (bandIdx == -1 || bandIdx >= _eqFxHandlesLocal.Length || _eqFxHandlesLocal[bandIdx] == 0)
        {
            return;
        }

        float clampedGain = AudioMath.ClampGain(gain);

        PeakEQParameters eqParams = new PeakEQParameters()
        {
            fCenter = frequency,
            fBandwidth = _equalizerBands[bandIdx].Bandwidth,
            fGain = clampedGain,
            lChannel = FXChannelFlags.All
        };

        if (Bass.FXSetParameters(_eqFxHandlesLocal[bandIdx], eqParams))
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
        if (_eqFxHandlesLocal.Length > 0 && CurrentStream != 0)
        {
            foreach (int fxHandle in _eqFxHandlesLocal)
            {
                if (fxHandle != 0)
                {
                    Bass.ChannelRemoveFX(CurrentStream, fxHandle);
                }
            }
        }
        _eqFxHandlesLocal = Array.Empty<int>();
        _eqInitializedLocal = false;
    }
}
