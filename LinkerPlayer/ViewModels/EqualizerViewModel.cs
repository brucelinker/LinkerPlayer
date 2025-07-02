using CommunityToolkit.Mvvm.ComponentModel;
using LinkerPlayer.Audio;
using LinkerPlayer.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.ObjectModel;
using System.IO;

namespace LinkerPlayer.ViewModels;

public partial class EqualizerViewModel : ObservableObject
{
    private readonly AudioEngine _audioEngine;
    private readonly ILogger<EqualizerViewModel> _logger;
    private readonly string _jsonFilePath;

    [ObservableProperty] private static ObservableCollection<Preset> _eqPresets = [];

    public EqualizerViewModel(AudioEngine audioEngine, ILogger<EqualizerViewModel> logger)
    {
        _audioEngine = audioEngine;
        _logger = logger;

        try
        {
            _logger.Log(LogLevel.Information, "Initializing EqualizerViewModel");

            _jsonFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LinkerPlayer", "eqPresets.json");

            LoadFromJson();

            _logger.Log(LogLevel.Information, "EqualizerViewModel initialized successfully");
        }
        catch (IOException ex)
        {
            _logger.Log(LogLevel.Error, ex, "IO error in EqualizerViewModel constructor: {Message}\n{StackTrace}", ex.Message, ex.StackTrace);
            throw;
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Error, ex, "Unexpected error in EqualizerViewModel constructor: {Message}\n{StackTrace}", ex.Message, ex.StackTrace);
            throw;
        }
    }

    [ObservableProperty] private float _band0;
    [ObservableProperty] private float _band1;
    [ObservableProperty] private float _band2;
    [ObservableProperty] private float _band3;
    [ObservableProperty] private float _band4;
    [ObservableProperty] private float _band5;
    [ObservableProperty] private float _band6;
    [ObservableProperty] private float _band7;
    [ObservableProperty] private float _band8;
    [ObservableProperty] private float _band9;

    partial void OnEqPresetsChanged(ObservableCollection<Preset> value)
    {
        _logger.LogInformation($"OnEqPresetChanged: {value}");
    }

    partial void OnBand0Changed(float value) { _audioEngine.SetBandGain(32.0f, value); }
    partial void OnBand1Changed(float value) { _audioEngine.SetBandGain(64.0f, value); }
    partial void OnBand2Changed(float value) { _audioEngine.SetBandGain(125.0f, value); }
    partial void OnBand3Changed(float value) { _audioEngine.SetBandGain(250.0f, value); }
    partial void OnBand4Changed(float value) { _audioEngine.SetBandGain(500.0f, value); }
    partial void OnBand5Changed(float value) { _audioEngine.SetBandGain(1000.0f, value); }
    partial void OnBand6Changed(float value) { _audioEngine.SetBandGain(2000.0f, value); }
    partial void OnBand7Changed(float value) { _audioEngine.SetBandGain(4000.0f, value); }
    partial void OnBand8Changed(float value) { _audioEngine.SetBandGain(8000.0f, value); }
    partial void OnBand9Changed(float value) { _audioEngine.SetBandGain(16000.0f, value); }

    public void SaveEqPresets()
    {
        JsonSerializerSettings settings = new() { TypeNameHandling = TypeNameHandling.Auto };
        string json = JsonConvert.SerializeObject(EqPresets, Formatting.Indented, settings);
        File.WriteAllText(_jsonFilePath, json);

        _logger.LogInformation("Saved EqPresets to json");
    }

    public void LoadFromJson()
    {
        if (File.Exists(_jsonFilePath))
        {
            string jsonString = File.ReadAllText(_jsonFilePath);

            EqPresets = JsonConvert.DeserializeObject<ObservableCollection<Preset>>(jsonString)!;

            if (EqPresets == null)
            {
                _logger.LogWarning("eqPresets.json is empty");
                return;
            }

            _logger.LogInformation("Loaded EqPresets from json");
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_jsonFilePath)!);
            File.Create(_jsonFilePath).Close();
        }
    }
}