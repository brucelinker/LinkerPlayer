using LinkerPlayer.Core;
using LinkerPlayer.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;

namespace LinkerPlayer.Services.Metadata;

public static class BackgroundMetadataRefresher
{
    private static readonly ConcurrentQueue<MediaFile> _queue = new();
    private static readonly SemaphoreSlim _workerLock = new(1, 1);
    private static readonly int _degreeOfParallelism = 4;
    private static readonly int _batchSize = 100;

    public static void Enqueue(IEnumerable<MediaFile> files)
    {
        if (files == null)
            return;
        foreach (MediaFile f in files)
        {
            if (f == null)
                continue;
            _queue.Enqueue(f);
        }

        _ = StartWorkerAsync();
    }

    private static async Task StartWorkerAsync()
    {
        if (!await _workerLock.WaitAsync(0).ConfigureAwait(false))
        {
            return; // already running
        }

        try
        {
            ILogger? logger = null;
            try
            {
                ILoggerFactory? lf = App.AppHost?.Services?.GetService(typeof(ILoggerFactory)) as ILoggerFactory;
                if (lf != null)
                {
                    logger = lf.CreateLogger("BackgroundMetadataRefresher");
                }
            }
            catch { }

            IMusicLibrary? library = App.AppHost?.Services?.GetService(typeof(IMusicLibrary)) as IMusicLibrary;
            if (library == null)
            {
                logger?.LogWarning("No IMusicLibrary available for background metadata refresher");
                return;
            }

            List<MediaFile> toProcess = new();
            while (_queue.TryDequeue(out MediaFile? item))
            {
                if (item != null)
                    toProcess.Add(item);
                if (toProcess.Count >= _batchSize)
                {
                    await ProcessBatchAsync(toProcess, library, logger).ConfigureAwait(false);
                    toProcess.Clear();
                }
            }

            if (toProcess.Count > 0)
            {
                await ProcessBatchAsync(toProcess, library, logger).ConfigureAwait(false);
            }
        }
        finally
        {
            _workerLock.Release();
        }
    }

    private static async Task ProcessBatchAsync(List<MediaFile> batch, IMusicLibrary library, ILogger? logger)
    {
        if (batch == null || batch.Count == 0)
            return;

        SemaphoreSlim sem = new SemaphoreSlim(_degreeOfParallelism);
        List<Task<MediaFile?>> tasks = new();

        foreach (MediaFile mf in batch)
        {
            await sem.WaitAsync().ConfigureAwait(false);
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    MediaFile work = new MediaFile { Path = mf.Path };
                    work.UpdateFromFileMetadata();
                    return work;
                }
                catch (Exception ex)
                {
                    logger?.LogDebug(ex, "Background metadata refresh failed for {Path}", mf.Path);
                    return null;
                }
                finally
                {
                    sem.Release();
                }
            }));
        }

        MediaFile?[] results = await Task.WhenAll(tasks).ConfigureAwait(false);
        List<MediaFile> updates = results.Where(r => r != null).Select(r => r!).ToList();

        if (updates.Count == 0)
            return;

        // Build a single O(1) path lookup from the live in-memory objects.
        // Snapshot once — the same object references remain valid for the whole batch.
        Dictionary<string, MediaFile> libraryIndex = library.MainLibrary
            .ToList()
            .ToDictionary(t => t.Path, t => t, StringComparer.OrdinalIgnoreCase);

        // Mark tracks as refreshing before the DB write
        foreach (MediaFile mf in updates)
        {
            if (libraryIndex.TryGetValue(mf.Path, out MediaFile? inMemory))
            {
                try { inMemory.IsRefreshing = true; } catch { }
            }
        }

        try
        {
            await library.UpdateTracksAsync(updates, updateMetadata: true, updateAnalysis: false).ConfigureAwait(false);

            // Clear flags on the same in-memory instances — no need to re-snapshot
            foreach (MediaFile mf in updates)
            {
                if (libraryIndex.TryGetValue(mf.Path, out MediaFile? inMemory))
                {
                    try
                    {
                        inMemory.NeedsMetadataRefresh = false;
                        inMemory.LastMetadataRefreshUtc = DateTime.UtcNow;
                        inMemory.IsRefreshing = false;
                    }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error saving refreshed metadata batch");
        }
    }
}
