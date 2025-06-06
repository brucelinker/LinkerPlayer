using CommunityToolkit.Mvvm.ComponentModel;
using LinkerPlayer.Audio;
using LinkerPlayer.Models;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.ObjectModel;
using System.IO;

namespace LinkerPlayer.ViewModels;

public partial class EqualizerViewModel : ObservableObject
{
    private readonly AudioEngine _audioEngine;
    private readonly string _jsonFilePath;

    [ObservableProperty] private static ObservableCollection<Preset> _eqPresets = [];

    public EqualizerViewModel(AudioEngine audioEngine)
    {
        _audioEngine = audioEngine;

        _jsonFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LinkerPlayer", "eqPresets.json");

        LoadFromJson();
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
        Log.Information($"OnEqPresetChanged: {value}");
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

        Log.Information("Saved EqPresets to json");
    }

    public void LoadFromJson()
    {
        if (File.Exists(_jsonFilePath))
        {
            string jsonString = File.ReadAllText(_jsonFilePath);

            EqPresets = JsonConvert.DeserializeObject<ObservableCollection<Preset>>(jsonString)!;

            if (EqPresets == null)
            {
                Log.Warning("eqPresets.json is empty");
                return;
            }

            Log.Information("Loaded EqPresets from json");
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_jsonFilePath)!);
            File.Create(_jsonFilePath).Close();
        }
    }
}