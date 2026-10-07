using System.Collections.ObjectModel;
using FileConverter.App.Models;

namespace FileConverter.App.Services;

public sealed class ConversionQueueService
{
    private readonly ConversionService _conversionService;
    private CancellationTokenSource? _cts;
    private readonly object _gate = new();

    public ConversionQueueService(ConversionService conversionService)
        => _conversionService = conversionService;

    public ObservableCollection<ConversionJob> Jobs { get; } = new();

    public bool IsRunning { get; private set; }
    public bool IsScanning { get; private set; }
    public int ScanCurrent { get; private set; }
    public int ScanTotal { get; private set; }

    public event Action? QueueStateChanged;
    public event Action<string>? Message;

    public int AddFiles(IEnumerable<string> paths, bool includeSubfolders)
    {
        var added = 0;
        var existing = new HashSet<string>(Jobs.Select(j => j.SourcePath), StringComparer.OrdinalIgnoreCase);

        foreach (var path in MediaFileEnumerator.FromPaths(paths, includeSubfolders))
        {
            if (!existing.Add(path))
                continue;

            Jobs.Add(new ConversionJob { SourcePath = path });
            added++;
        }

        if (added > 0)
            Message?.Invoke($"Added {added} file(s) to the queue.");

        return added;
    }

    public void RemoveSelected(IEnumerable<ConversionJob> jobs)
    {
        foreach (var job in jobs.ToList())
        {
            if (job.Status == JobStatus.Running)
                continue;
            Jobs.Remove(job);
        }
    }

    public void ClearFinished()
    {
        var removable = Jobs
            .Where(j => j.Status is JobStatus.Done or JobStatus.Failed or JobStatus.Cancelled or JobStatus.Skipped)
            .ToList();
        foreach (var job in removable)
            Jobs.Remove(job);

        if (removable.Count > 0)
            Message?.Invoke($"Cleared {removable.Count} finished job(s).");
    }

    public void ClearAll()
    {
        if (IsRunning)
        {
            Message?.Invoke("Cannot clear the queue while conversion is running.");
            return;
        }

        var count = Jobs.Count;
        Jobs.Clear();
        Message?.Invoke(count == 0 ? "Queue is already empty." : $"Cleared {count} job(s).");
    }

    public async Task RunAsync(AppSettings settings)
    {
        lock (_gate)
        {
            if (IsRunning)
            {
                Message?.Invoke("Conversion is already running.");
                return;
            }

            IsRunning = true;
            IsScanning = false;
            ScanCurrent = 0;
            ScanTotal = 0;
            _cts = new CancellationTokenSource();
        }

        QueueStateChanged?.Invoke();
        var token = _cts!.Token;

        try
        {
            var pending = Jobs.Where(j => j.Status is JobStatus.Pending or JobStatus.Failed or JobStatus.Cancelled).ToList();
            if (pending.Count == 0)
            {
                Message?.Invoke("No pending jobs to convert.");
                return;
            }

            foreach (var job in pending)
            {
                job.CountsTowardEncodeProgress = false;
                job.ErrorMessage = null;
                job.Speed = null;
                job.ProgressPercent = 0;
                job.Status = JobStatus.Pending;
                job.StatusDetail = "Waiting for scan";
            }

            Message?.Invoke($"Scanning {pending.Count} file(s) for skips before encode…");
            IsScanning = true;
            ScanTotal = pending.Count;
            ScanCurrent = 0;
            QueueStateChanged?.Invoke();

            var skipped = 0;
            for (var i = 0; i < pending.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var job = pending[i];
                ScanCurrent = i + 1;
                job.StatusDetail = $"Scanning {ScanCurrent}/{ScanTotal}";
                QueueStateChanged?.Invoke();

                try
                {
                    var wasSkipped = await _conversionService.PrepareAsync(job, settings, token);
                    if (wasSkipped)
                    {
                        skipped++;
                        Message?.Invoke($"Skipped ({job.StatusDetail}): {job.FileName}");
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    job.Status = JobStatus.Failed;
                    job.ErrorMessage = TrimError(ex.Message);
                    job.StatusDetail = "Scan failed";
                    job.CountsTowardEncodeProgress = false;
                    Message?.Invoke($"Scan failed: {job.FileName} — {job.ErrorMessage}");
                }
            }

            IsScanning = false;
            QueueStateChanged?.Invoke();

            var toEncode = pending.Where(j => j.Status == JobStatus.Pending && j.CountsTowardEncodeProgress).ToList();
            Message?.Invoke(
                $"Scan done: {skipped} skipped, {toEncode.Count} to encode → {settings.OutputFormat.ToDisplayName()}.");

            if (toEncode.Count == 0)
            {
                Message?.Invoke("Nothing left to encode.");
                return;
            }

            var completed = 0;
            foreach (var job in toEncode)
            {
                token.ThrowIfCancellationRequested();

                job.Status = JobStatus.Running;
                job.ProgressPercent = 0;
                job.ErrorMessage = null;
                job.StatusDetail = "Starting";
                job.Speed = null;
                Message?.Invoke($"Converting: {job.FileName}");

                var progress = new Progress<ConversionProgress>(p =>
                {
                    job.ProgressPercent = p.Percent;
                    job.StatusDetail = p.Detail;
                    job.Speed = p.Speed;
                });

                try
                {
                    await _conversionService.ConvertAsync(job, settings, progress, token);
                    if (job.Status == JobStatus.Skipped)
                    {
                        Message?.Invoke($"Skipped ({job.StatusDetail}): {job.FileName}");
                    }
                    else
                    {
                        Message?.Invoke($"Done: {Path.GetFileName(job.OutputPath)}");
                        TryDeleteOriginal(job, settings);
                    }

                    completed++;
                }
                catch (OperationCanceledException)
                {
                    job.Status = JobStatus.Cancelled;
                    job.StatusDetail = "Cancelled";
                    Message?.Invoke($"Cancelled: {job.FileName}");
                    foreach (var remaining in toEncode.Where(j => j.Status == JobStatus.Pending))
                    {
                        remaining.Status = JobStatus.Cancelled;
                        remaining.StatusDetail = "Cancelled";
                    }

                    throw;
                }
                catch (Exception ex)
                {
                    job.Status = JobStatus.Failed;
                    job.ErrorMessage = TrimError(ex.Message);
                    job.StatusDetail = "Failed";
                    Message?.Invoke($"Failed: {job.FileName} — {job.ErrorMessage}");
                }
            }

            Message?.Invoke($"Queue finished. Encoded {completed} of {toEncode.Count} file(s) ({skipped} skipped in scan).");
        }
        catch (OperationCanceledException)
        {
            Message?.Invoke("Conversion cancelled.");
            foreach (var job in Jobs.Where(j => j.Status == JobStatus.Pending))
            {
                job.Status = JobStatus.Cancelled;
                job.StatusDetail = "Cancelled";
            }
        }
        finally
        {
            lock (_gate)
            {
                IsRunning = false;
                IsScanning = false;
                _cts?.Dispose();
                _cts = null;
            }

            QueueStateChanged?.Invoke();
        }
    }

    public void Cancel()
    {
        lock (_gate)
        {
            if (!IsRunning || _cts is null)
            {
                Message?.Invoke("Nothing to cancel.");
                return;
            }

            Message?.Invoke("Cancelling…");
            _cts.Cancel();
        }
    }

    private void TryDeleteOriginal(ConversionJob job, AppSettings settings)
    {
        if (!settings.DeleteOriginalAfterSuccess)
            return;

        if (job.Status != JobStatus.Done)
            return;

        if (string.IsNullOrWhiteSpace(job.OutputPath) || !File.Exists(job.OutputPath))
        {
            Message?.Invoke($"Kept original (output missing): {job.FileName}");
            return;
        }

        if (string.Equals(
                Path.GetFullPath(job.SourcePath),
                Path.GetFullPath(job.OutputPath),
                StringComparison.OrdinalIgnoreCase))
        {
            Message?.Invoke($"Kept original (same as output): {job.FileName}");
            return;
        }

        try
        {
            File.Delete(job.SourcePath);
            Message?.Invoke($"Deleted original: {job.FileName}");
        }
        catch (Exception ex)
        {
            Message?.Invoke($"Could not delete original {job.FileName}: {TrimError(ex.Message)}");
        }
    }

    private static string TrimError(string message)
    {
        var oneLine = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return oneLine.Length <= 180 ? oneLine : oneLine[..177] + "…";
    }
}
