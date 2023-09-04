using System;
using System.Collections.Generic;
using System.IO;
using LinkerPlayer.Models;
using Newtonsoft.Json;
using Serilog;

namespace LinkerPlayer.Core;

public class EqualizerLibrary
{
    public static List<BandsSettings>? BandsSettings = new();
    private static readonly string JsonFilePath;

    static EqualizerLibrary()
    {
        JsonFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LinkerPlayer", "bandsSettings.json");
    }

    public static void LoadFromJson()
    {
        if (File.Exists(JsonFilePath))
        {
            string jsonString = File.ReadAllText(JsonFilePath);

            List<BandsSettings>? tempBands = JsonConvert.DeserializeObject<List<BandsSettings>>(jsonString);
            if (tempBands != null)
            {
                BandsSettings = JsonConvert.DeserializeObject<List<BandsSettings>>(jsonString);
            }
            else
            {
                Log.Warning("Json is empty");
            }

            Log.Information("Load from json");
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(JsonFilePath)!);
            File.Create(JsonFilePath).Close();
        }
    }

    public static void SaveToJson()
    {
        JsonSerializerSettings settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto };
        string json = JsonConvert.SerializeObject(BandsSettings, Formatting.Indented, settings);
        File.WriteAllText(JsonFilePath, json);

        Log.Information("Save to json");
    }
}