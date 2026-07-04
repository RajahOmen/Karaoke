using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Sound;
using Lumina.Data.Files;
using Lumina.Data.Parsing.Scd;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Lumina.Extensions;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Action = System.Action;

namespace Karaoke.Services;

public class BGMService(
    IFramework framework,
    IPluginLog pluginLog,
    IDataManager dataManager,
    SongLoaderService songLoaderService,
    IHostApplicationLifetime lifetime,
    Configuration configuration
    ) : IHostedService
{
    private readonly IDataManager dataManager = dataManager;
    private readonly SongLoaderService songLoaderService = songLoaderService;
    private readonly IHostApplicationLifetime lifetime = lifetime;
    private readonly Configuration configuration = configuration;
    private readonly IFramework framework = framework;
    private readonly IPluginLog pluginLog = pluginLog;
    private readonly ExcelSheet<BGM> bgmSheet = dataManager.GetExcelSheet<BGM>();
    private readonly Dictionary<string, BGM?> bgmCache = [];

    public event Action? OnSongChange;
    public uint? LoadingSongId = null;
    public bool LoadingSong => LoadingSongId is not null;
    public Song? CurrentSong = null;
    public bool CurrentSongHasLooped => TotalElapsedTime > CurrentSong?.Duration;
    /// <summary>
    /// Restarted during overflowed duration, don't know the time!
    /// </summary>
    public bool CurrentSongUnknownTime = false;
    public SongLoopData? CurrentSongLoopData { get; private set; } = null;
    public float CurrentElapsedTime { get; private set; } = 0.0f;
    /// <summary>
    /// The last elapsed time as reported directly from the SoundData struct.
    /// No adjustments for loops or overflow.
    /// </summary>
    public float RawElapsedTime { get; private set; } = 0.0f;

    /// <summary>
    /// Total elapsed time from song start, accounting for overflow offset
    /// and config-defined time rate multiplier
    /// </summary>
    public float TotalElapsedTime { get; private set; } = 0.0f;
    /// <summary>
    /// Handles when the game overflows elapsed time at ~7158s or so
    /// </summary>
    public float ElapsedTimeRawOverflowOffset { get; private set; } = 0.0f;
    public uint CurrentSongId => CurrentSong?.Id ?? 0;

    private double deltaTimeSum = 0;
    private DateTime startDateTime = DateTime.MinValue;

    public bool ReloadingCurrentSongLyrics => songLoaderService.ReloadingLyrics;

    private const int LogElapsedInterval = 800;
    private int logUpdateCount = 0;

    private bool listeningToFramework = false;

    private const float ELAPSED_OVERFLOW_THRESHOLD = 7158.277f;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        this.lifetime.ApplicationStarted.Register(() =>
        {
            framework.Update += OnUpdate;
            listeningToFramework = true;
        });
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (listeningToFramework)
            framework.Update -= OnUpdate;
        listeningToFramework = false;
        return Task.CompletedTask;
    }

    public void OnUpdate(IFramework framework)
    {
        ThreadSafety.AssertMainThread();

        var (newSongId, newSongSoundData) = getCurrentBGM();
        var newRawElapsedTime = newSongSoundData?.GetElapsedTime() ?? 0.0f;
        deltaTimeSum += framework.UpdateDelta.TotalSeconds;

        if (newSongId != (CurrentSong?.Id ?? 0) && LoadingSongId != newSongId)
        {
            pluginLog.Verbose($"SONG START: {(DateTime.Now.TimeOfDay - TimeSpan.FromMicroseconds(newRawElapsedTime * 1000000d)).TotalMicroseconds / 1000000d:F10} ({DateTime.Now.TimeOfDay.TotalMicroseconds / 1000000d:F10} - {newRawElapsedTime:F10} [{TimeSpan.FromMicroseconds(newRawElapsedTime * 1000000d)}])");

            pluginLog.Verbose($"SONG START FRAMEWORK: {(framework.LastUpdate.TimeOfDay - TimeSpan.FromMicroseconds(newRawElapsedTime * 1000000d)).TotalMicroseconds / 1000000d:F10} ({framework.LastUpdate.TimeOfDay.TotalMicroseconds / 1000000d:F10} - {newRawElapsedTime:F10} [{TimeSpan.FromMicroseconds(newRawElapsedTime * 1000000d)}])");

            deltaTimeSum = newRawElapsedTime;
            LoadingSongId = null;
            CurrentSong = null;
            ElapsedTimeRawOverflowOffset = 0.0f;
            RawElapsedTime = 0.0f;
            startDateTime = framework.LastUpdate - TimeSpan.FromMicroseconds(newRawElapsedTime * 1000000d);
            if (newSongSoundData is not null)
            {
                LoadingSongId = newSongId;
                pluginLog.Debug($"BGM changed ({CurrentSongId} -> {newSongId}), updating song...");
                try
                {
                    var newSongLoopData = getSongLoopData(newSongSoundData.Value);
                    var song = songLoaderService.GetSongById(
                        newSongId,
                        newSongLoopData
                    );
                    RawElapsedTime = newRawElapsedTime;
                    CurrentSong = song;
                    CurrentSongLoopData = newSongLoopData;
                    if (CurrentSongUnknownTime)
                    {
                        pluginLog.Warning("Unknown time status reset");
                    }
                    CurrentSongUnknownTime = false;
                    pluginLog.Debug($"Song updated: {CurrentSong?.Name ?? "Unknown"}, Duration: {song.Duration}, loopStart: {song.LoopStart}");
                }
                finally
                {
                    LoadingSongId = null;
                }
            }
            else
            {
                pluginLog.Debug($"BGM stopped");
            }
            OnSongChange?.Invoke();
        }

        // update this (adjusting the raw to account overflows and loops)

        TotalElapsedTime = getAdjustedElapsedTime(
            newRawElapsedTime,
            framework.UpdateDelta,
            framework
        );

        RawElapsedTime = newRawElapsedTime;

        CurrentElapsedTime = CurrentSong?.LoopElapsedTime(
            TotalElapsedTime
        ) ?? 0.0f;

        if (configuration.DebugMode)
        {
            if (logUpdateCount >= LogElapsedInterval)
            {
                LogElapsedTime(framework);
                logUpdateCount = 0;
            }
            logUpdateCount++;
        }
    }

    public void ReloadCurrentSongLyrics()
    {
        songLoaderService.ReloadLyrics(CurrentSong);
    }

    private SongLoopData getSongLoopData(SoundData songSoundData)
    {
        var fileName = songSoundData.GetFileName().ToString();
        var scdFile = dataManager.GameData.GetFile<ScdFile>(fileName);
        if (scdFile is not ScdFile musicFile)
        {
            pluginLog.Warning($"No BGM data found for file name {fileName}");
            return new SongLoopData(0, 0, 0);
        }

        var soundNumber = (int)songSoundData.GetSoundNumber();
        // TODO: Verify this is actually how to use this SoundNumber value
        var sound = musicFile.GetSound(soundNumber);
        var intervals = musicFile
            .GetTrack(soundNumber)
            .Where(track => track.cmd == TrackCmd.Interval)
            .Select(track => (uint)(track.data ?? 0u))
            .ToList();

        var lastIntervalLength = intervals.Last();

        var totalLength = sound.SoundExtraDesc?.PlayTimeLength ?? (uint)intervals.Sum(i => i);
        uint? loopStart = null;
        if (sound.SoundBasicDesc.Attribute.HasFlag(SoundAttribute.Loop) && intervals.Count > 1)
            loopStart = intervals[^2];

        return new SongLoopData(
            totalDurationMillis: totalLength,
            loopStartMillis: loopStart,
            loopDurationMillis: lastIntervalLength
        );
    }

    private float getAdjustedElapsedTime(float newRawElapsedTime, TimeSpan deltaTime, IFramework framework)
    {
        // the time has overflowed, adjust accordingly
        if (newRawElapsedTime < RawElapsedTime)
        {
            LogElapsedTime(framework);
            var actualNewRawElapsed = RawElapsedTime + (float)deltaTime.TotalSeconds;
            var wrap = actualNewRawElapsed - newRawElapsedTime;
            var newCurElapsedTimeLogged = CurrentSong?.LoopElapsedTime(
                newRawElapsedTime,
                ElapsedTimeRawOverflowOffset + wrap
            ) ?? 0.0f;
            pluginLog.Warning(
                $"ELAPSED TIME OVERFLOW:\n" +
                $"{newRawElapsedTime:F5} < {RawElapsedTime:F5}\n" +
                $"deltaTime: {deltaTime.TotalSeconds:F5}, wrap: {wrap:F5}. New total offset: {ElapsedTimeRawOverflowOffset + wrap}\n" +
                $"new cur elapsed time: {newCurElapsedTimeLogged:F5}"
            );
            ElapsedTimeRawOverflowOffset += wrap;

        }
        else if (newRawElapsedTime < 0 && ElapsedTimeRawOverflowOffset < 0.1)
        {
            if (!CurrentSongUnknownTime)
            {
                pluginLog.Warning(
                    "NEW RAW ELAPSED NEGATIVE, NO OVERFLOW DETECTED\n" +
                    $"{newRawElapsedTime}, {CurrentElapsedTime} - {ElapsedTimeRawOverflowOffset} ({CurrentElapsedTime - ElapsedTimeRawOverflowOffset})"
                );
            }
            CurrentSongUnknownTime = true;
        }

        return Math.Max(0f, (float)((newRawElapsedTime + ElapsedTimeRawOverflowOffset) * configuration.TimeRateMultiplier));
    }

    private BGM? getBgmRowSoundData(SoundData soundData)
    {
        ThreadSafety.AssertMainThread();
        var fileName = soundData.GetFileName().ToString();
        if (bgmCache.TryGetValue(fileName, out var cached))
            return cached;
        var bgmRow = bgmSheet.FirstOrNull(r => r.File == fileName);
        bgmCache[fileName] = bgmRow;
        return bgmRow;

    }

    private unsafe (uint SongId, SoundData? SongSoundData) getCurrentBGM()
    {
        ThreadSafety.AssertMainThread();
        var soundManager = SoundManager.Instance();
        if (soundManager == null)
        {
            pluginLog.Warning($"Sound manager instance null");
            return default;
        }

        SoundData? soundData = null;
        var smallestDuration = float.MinValue;
        foreach (var entry in soundManager->SoundDataPool->Entries)
        {
            if (!entry.IsActive || !entry.IsPlaying())
                continue;

            var fileName = entry.GetFileName().ToString();
            if (!fileName.StartsWith("music/"))
                continue;

            var elapsed = entry.GetElapsedTime();

            // Note: this will only delay the problem that occurs here.
            // Delays from ~2hrs to ~4hrs. The issue: when two
            // tracks play simultaneously but slightly delayed, track 1
            // will be played for "shorter" than track 2 for the time
            // when track 1 has overflowed and track 2 hasn't. After
            // track 2 overflows, it will revert to track 2 but the 
            // usual overflow compensation will be broken. This happened
            // in testing on the character login screen with a custom
            // track being played overtop the standard login bgm.
            if (elapsed < 0)
            {
                elapsed += ELAPSED_OVERFLOW_THRESHOLD * 2;
            }

            if (soundData is null || elapsed < smallestDuration)
            {
                soundData = entry;
                smallestDuration = elapsed;
            }
        }

        if (soundData is not SoundData playingMusicData)
            return default;

        if (getBgmRowSoundData(playingMusicData) is not BGM bgmRow)
            return default;

        return (bgmRow.RowId, playingMusicData);
    }

    private unsafe SoundData? getPlayingBGMSound()
    {
        var soundManager = SoundManager.Instance();
        if (soundManager == null)
            throw new Exception("Sound manager null");

        SoundData? soundData = null;
        var smallestDuration = float.MinValue;
        foreach (var entry in soundManager->SoundDataPool->Entries)
        {
            if (!entry.IsActive || !entry.IsPlaying())
                continue;

            var fileName = entry.GetFileName().ToString();
            if (!fileName.StartsWith("music/"))
                continue;

            var elapsed = entry.GetElapsedTime();
            if (soundData is null || elapsed < smallestDuration)
            {
                soundData = entry;
                smallestDuration = elapsed;
            }
        }

        return soundData;
    }

    public void LogElapsedTime(IFramework framework)
    {
        var rateTime = (RawElapsedTime + ElapsedTimeRawOverflowOffset) * configuration.TimeRateMultiplier;
        pluginLog.Debug(
            $"{framework.LastUpdate.TimeOfDay.TotalMicroseconds / 1000000d:F10}, " +
            $"{(DateTime.Now - startDateTime).TotalMicroseconds / 1000000d:F10}, " +
            $"{CurrentElapsedTime:F10}, " +
            $"{RawElapsedTime:F10}, " +
            $"{rateTime:F10}, " +
            $"{RawElapsedTime - rateTime:F10}, " +
            $"{DateTime.Now.TimeOfDay.TotalMicroseconds / 1000000d:F10}, " +
            $"{framework.UpdateDelta.TotalMilliseconds / 1000000d:F10}, " +
            $"{ElapsedTimeRawOverflowOffset:F10}, " +
            $"{configuration.TimeRateMultiplier:F16}, " +
            $"{(CurrentSongLoopData?.TotalDurationMillis ?? 0) / 1000f:F10}, " +
            $"{(CurrentSongLoopData?.LoopStartMillis ?? 0) / 1000f:F10}, " +
            $"{(CurrentSongLoopData?.LoopDurationMillis ?? 0) / 1000f:F10}"
        );
    }

    private static string dumpStruct<T>(T? obj, string separator = ", ") where T : struct
    {
        if (obj is null)
            return " null";
        return $"\n{string.Join(separator,
            typeof(T)
                .GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Select(f => $"{f.Name}: {f.GetValue(obj)}"))}";
    }

    private static string dumpStructArr<T>(T[]? arr) where T : struct
    {
        if (arr is null)
            return " null";
        return $"\n{string.Join("\n", arr.Select((e, i) => $"[{i}] {dumpStruct<T>(e)}"))}";
    }

    public unsafe void LogData()
    {
        var bgmData = getPlayingBGMSound();
        if (bgmData is not SoundData soundData)
        {
            pluginLog.Warning($"sound data not found");
            return;
        }
        var fileName = soundData.GetFileName().ToString();
        var file = dataManager.GameData.GetFile<ScdFile>(fileName);
        if (file is not ScdFile scdFile)
        {
            pluginLog.Warning($"SCD file not found for name {fileName}");
            return;
        }

        var srh = soundData.GetSoundResourceHandle();
        var soundNumber = (int)soundData.GetSoundNumber();
        var sound = scdFile.GetSound(soundNumber);
        var tracks = scdFile.GetTrack(soundNumber);

        pluginLog.Debug($"==============================================================");

        if (srh is null)
            pluginLog.Debug($"Sound resource handle null");
        else
            pluginLog.Debug($"SoundResourceHandle: {srh->FileName}, {srh->FileType}, {srh->Id}");
        pluginLog.Debug($"File Name: {fileName}");
        pluginLog.Debug($"sound number: {soundNumber}");
        pluginLog.Debug($"AudioDataCount: {scdFile.AudioDataCount}");
        pluginLog.Debug($"SoundDataCount: {scdFile.SoundDataCount}");
        pluginLog.Debug($"TrackDataCount: {scdFile.TrackDataCount}");
        pluginLog.Debug($"Layout: {dumpStruct(scdFile.GetLayout())}");
        pluginLog.Debug($"AttributeData: {dumpStruct(scdFile.GetAttributeData())}");
        pluginLog.Debug("------SOUND-----");
        pluginLog.Debug($"SoundBasicDesc:{dumpStruct<SoundBasicDesc>(sound.SoundBasicDesc)}");
        pluginLog.Debug($"speed: {soundData.Speed}");
        pluginLog.Debug($"getspeed: {soundData.GetSpeed()}");
        pluginLog.Debug($"SendInfos:{dumpStructArr(sound.SendInfos)}");
        pluginLog.Debug($"SoundEffectParam:{dumpStruct(sound.SoundEffectParam)}");
        pluginLog.Debug($"AtomosgearInfo:{dumpStruct(sound.AtomosgearInfo)}");
        pluginLog.Debug($"AccelerationInfo:{dumpStruct(sound.AccelerationInfo)}");
        pluginLog.Debug($"BusDuckingInfo:{dumpStruct(sound.BusDuckingInfo)}");
        pluginLog.Debug($"RoutingInfo:{dumpStruct(sound.RoutingInfo)}");
        pluginLog.Debug($"SoundExtraDesc:{dumpStruct(sound.SoundExtraDesc)}");
        pluginLog.Debug("------TRACK-----");
        foreach (var (cmd, data) in tracks)
        {
            pluginLog.Debug($"cmd: {Enum.GetName(cmd)}, data [{data?.GetType()}]: {data}");
            if (data?.GetType() == typeof(TrackCmdParam))
            {
                pluginLog.Debug($"{dumpStruct<TrackCmdParam>((TrackCmdParam)data)}");
            }
            if (cmd == TrackCmd.LoopStart && data is not null)
            {
                var loopStart = (UInt32[])data;
                for (var i = 0; i < loopStart.Length; i++)
                {
                    pluginLog.Debug($"ACTUAL VALUE: {loopStart[i]}");
                }
            }
        }
        pluginLog.Debug($"-----AUDIO[{soundNumber}]-----");
        var maybeAudio = scdFile.GetAudio(soundNumber);
        if (maybeAudio is not ScdFile.Audio audio)
        {
            pluginLog.Debug($"audio data is null for sound number: {soundNumber}");
        }
        else
        {
            pluginLog.Debug($"AudioBasicDesc:{dumpStruct<AudioBasicDesc>(audio.AudioBasicDesc)}");
        }
        pluginLog.Debug($"==============================================================");
    }
}
