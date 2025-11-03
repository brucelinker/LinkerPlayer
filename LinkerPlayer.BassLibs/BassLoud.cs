using System.Runtime.InteropServices;

namespace LinkerPlayer.BassLibs;

/// <summary>
/// BassLoud - EBU R 128 loudness measurement for BASS
/// P/Invoke wrapper for bassloud.dll
/// </summary>
public static class BassLoud
{
    private const string DllName = "bassloud.dll";

    /// <summary>
    /// Loudness scanning modes/flags
    /// </summary>
    [Flags]
    public enum LoudnessFlags
    {
        /// <summary>
        /// Enable integrated loudness measurement
        /// </summary>
        Integrated = 0x1,

        /// <summary>
        /// Enable loudness range measurement
        /// </summary>
        Range = 0x2,

        /// <summary>
        /// Enable peak level measurement
        /// </summary>
        Peak = 0x4,

        /// <summary>
        /// Enable true peak level measurement
        /// </summary>
        TruePeak = 0x8,

        /// <summary>
        /// Automatically free the measurement when the channel is freed
        /// </summary>
        AutoFree = 0x10000,

        /// <summary>
        /// Enable loudness measurement of the last 400ms (or specified duration)
        /// </summary>
        Current = 0x100
    }

    /// <summary>
    /// Loudness measurement modes for GetLevel
    /// </summary>
    public enum LoudnessMode
    {
        /// <summary>
        /// Integrated loudness in LUFS (average since measurement started)
        /// </summary>
        Integrated = 0x1,

        /// <summary>
        /// Loudness range in LU
        /// </summary>
        Range = 0x2,

        /// <summary>
        /// Peak level in linear scale
        /// </summary>
        Peak = 0x4,

        /// <summary>
        /// True peak level in linear scale
        /// </summary>
        TruePeak = 0x8,

        /// <summary>
        /// Loudness in LUFS of the last 400ms (or specified duration)
        /// </summary>
        Current = 0x100
    }

    /// <summary>
    /// Start loudness measurement on a channel
    /// </summary>
    /// <param name="handle">The channel handle</param>
    /// <param name="flags">Measurement flags</param>
    /// <param name="priority">DSP priority</param>
    /// <returns>Loudness measurement handle, or 0 on error</returns>
    [DllImport(DllName, EntryPoint = "BASS_Loudness_Start", CallingConvention = CallingConvention.Winapi)]
    public static extern int Start(int handle, int flags, int priority);

    /// <summary>
    /// Get a loudness measurement level
    /// </summary>
    /// <param name="handle">The loudness measurement handle</param>
    /// <param name="mode">The measurement type to retrieve</param>
    /// <param name="level">Pointer to receive the level</param>
    /// <returns>True if successful</returns>
    [DllImport(DllName, EntryPoint = "BASS_Loudness_GetLevel", CallingConvention = CallingConvention.Winapi)]
    public static extern bool GetLevel(int handle, int mode, out float level);

    /// <summary>
    /// Stop loudness measurement
    /// </summary>
    /// <param name="handle">The loudness measurement handle</param>
    /// <returns>True if successful</returns>
    [DllImport(DllName, EntryPoint = "BASS_Loudness_Stop", CallingConvention = CallingConvention.Winapi)]
    public static extern bool Stop(int handle);

    /// <summary>
    /// Get BassLoud version
    /// </summary>
    /// <returns>Version number</returns>
    [DllImport(DllName, EntryPoint = "BASS_Loudness_GetVersion", CallingConvention = CallingConvention.Winapi)]
    public static extern int GetVersion();

    /// <summary>
    /// Convert LUFS to ReplayGain dB value
    /// ReplayGain reference level is -18 LUFS
    /// </summary>
    /// <param name="lufs">Integrated loudness in LUFS</param>
    /// <returns>ReplayGain adjustment in dB</returns>
    public static double LufsToReplayGain(float lufs)
    {
        const double ReferenceLevel = -18.0;
        return ReferenceLevel - lufs;
    }

    /// <summary>
    /// Convert ReplayGain dB to LUFS value
    /// </summary>
    /// <param name="replayGainDb">ReplayGain adjustment in dB</param>
    /// <returns>Integrated loudness in LUFS</returns>
    public static double ReplayGainToLufs(double replayGainDb)
    {
        const double ReferenceLevel = -18.0;
        return ReferenceLevel - replayGainDb;
    }
}
