using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace LinkerPlayer.BassLibs
{
    /// <summary>
    /// Manages BASS native DLL extraction and loading
    /// </summary>
    public static class BassNativeLibraryManager
    {
        private static readonly Dictionary<string, string> _extractedDlls = new();
        private static bool _isInitialized = false;
        private static ILogger? _logger;

        /// <summary>
        /// Initialize the BASS native library manager
        /// </summary>
        /// <param name="logger">Optional logger for diagnostic information</param>
        public static void Initialize(ILogger? logger = null)
        {
            if (_isInitialized) return;

            _logger = logger;
            _logger?.LogInformation("Initializing BASS Native Library Manager");

            try
            {
                ExtractNativeDlls();
                _isInitialized = true;
                _logger?.LogInformation($"BASS Native Library Manager initialized successfully - {_extractedDlls.Count} DLLs available");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to initialize BASS Native Library Manager");
                throw;
            }
        }

        /// <summary>
        /// Gets the path where BASS DLLs have been extracted
        /// </summary>
        public static string GetNativeLibraryPath()
        {
            if (!_isInitialized)
                throw new InvalidOperationException("BassNativeLibraryManager must be initialized first");

            // Return the directory where DLLs were extracted
            return Path.GetDirectoryName(_extractedDlls.Values.FirstOrDefault())
                   ?? throw new InvalidOperationException("No DLLs have been extracted");
        }

        /// <summary>
        /// Gets the path to a specific BASS DLL
        /// </summary>
        /// <param name="dllName">Name of the DLL (e.g., "bass.dll")</param>
        /// <returns>Full path to the extracted DLL</returns>
        public static string GetDllPath(string dllName)
        {
            if (!_isInitialized)
                throw new InvalidOperationException("BassNativeLibraryManager must be initialized first");

            if (_extractedDlls.TryGetValue(dllName.ToLowerInvariant(), out string? path))
                return path;

            throw new FileNotFoundException($"BASS DLL '{dllName}' not found in extracted libraries");
        }

        /// <summary>
        /// Checks if a specific BASS DLL is available
        /// </summary>
        /// <param name="dllName">Name of the DLL to check</param>
        /// <returns>True if the DLL is available</returns>
        public static bool IsDllAvailable(string dllName)
        {
            return _isInitialized && _extractedDlls.ContainsKey(dllName.ToLowerInvariant());
        }

        /// <summary>
        /// Gets a list of all available BASS DLLs
        /// </summary>
        /// <returns>List of available DLL names</returns>
        public static IReadOnlyList<string> GetAvailableDlls()
        {
            if (!_isInitialized)
                return Array.Empty<string>();

            return _extractedDlls.Keys.ToList().AsReadOnly();
        }

        private static void ExtractNativeDlls()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            string tempPath = Path.Combine(Path.GetTempPath(), "LinkerPlayer", "BassLibs", Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0");

            Directory.CreateDirectory(tempPath);

            string[] dllNames = new[]
            {
                "bass.dll",
                "bassalac.dll",
                "bassape.dll",
                "bassflac.dll",
                "bassmix.dll",
                "basswasapi.dll",
                "basswma.dll",
                "basswv.dll",
                "bass_aac.dll",
                "bass_ac3.dll",
                "bass_fx.dll"
            };

            foreach (string dllName in dllNames)
            {
                string resourceName = $"LinkerPlayer.BassLibs.Native.{dllName}";
                string extractedPath = Path.Combine(tempPath, dllName);

                try
                {
                    using Stream? resourceStream = assembly.GetManifestResourceStream(resourceName);
                    if (resourceStream == null)
                    {
                        _logger?.LogWarning($"BASS DLL resource not found: {dllName}");
                        continue;
                    }

                    // Only extract if file doesn't exist or is different
                    bool shouldExtract = !File.Exists(extractedPath);
                    if (!shouldExtract)
                    {
                        long existingLength = new FileInfo(extractedPath).Length;
                        shouldExtract = existingLength != resourceStream.Length;
                    }

                    if (shouldExtract)
                    {
                        using FileStream fileStream = File.Create(extractedPath);
                        resourceStream.CopyTo(fileStream);
                    }

                    _extractedDlls[dllName.ToLowerInvariant()] = extractedPath;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, $"Failed to extract BASS DLL: {dllName}");
                }
            }

            _logger?.LogInformation($"BASS Native Library Manager ready - {_extractedDlls.Count} DLLs available");
        }

        /// <summary>
        /// Cleanup extracted DLLs on application shutdown
        /// </summary>
        public static void Cleanup()
        {
            if (!_isInitialized) return;

            _logger?.LogInformation("Cleaning up BASS Native Library Manager");

            // Note: We don't delete the DLLs since they might still be in use
            // The temp directory cleanup will be handled by the OS
            _extractedDlls.Clear();
            _isInitialized = false;
        }
    }
}