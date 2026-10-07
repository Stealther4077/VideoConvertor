using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using FileConverter.App.Models;
using FileConverter.App.Services;
using Microsoft.Win32;

namespace FileConverter.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly FfmpegLocator _locator = new();
    private readonly ConversionQueueService _queue;
    private readonly StringBuilder _logBuilder = new();
    private string _logText = string.Empty;
    private string _ffmpegStatus = "Checking ffmpeg…";
    private string _overallStatus = "Idle";
    private double _overallProgress;
    private string _estimatedTimeRemaining = "—";
    private double? _lastSpeedFactor;
    private OutputFormat _selectedFormat = OutputFormat.Avi;
    private bool _useSameFolder = true;
    private string? _customOutputFolder;
    private bool _overwriteExisting;
    private bool _includeSubfolders;
    private bool _deleteOriginalAfterSuccess;
    private bool _skipCompatibleCodec = true;
    private bool _isBusy;

    public MainViewModel()
    {
        var probe = new MediaProbeService(_locator);
        var conversion = new ConversionService(_locator, probe);
        _queue = new ConversionQueueService(conversion);
        _queue.Jobs.CollectionChanged += OnJobsChanged;
        _queue.Message += AppendLog;
        _queue.QueueStateChanged += OnQueueStateChanged;

        Jobs = _queue.Jobs;
        FormatOptions = Enum.GetValues<OutputFormat>().ToList();

        AddFilesCommand = new RelayCommand(_ => AddFiles(), _ => !IsBusy);
        AddFolderCommand = new RelayCommand(_ => AddFolder(), _ => !IsBusy);
        StartCommand = new RelayCommand(async _ => await StartAsync(), _ => !IsBusy && Jobs.Count > 0);
        CancelCommand = new RelayCommand(_ => _queue.Cancel(), _ => IsBusy);
        ClearFinishedCommand = new RelayCommand(_ => _queue.ClearFinished(), _ => !IsBusy);
        ClearAllCommand = new RelayCommand(_ => _queue.ClearAll(), _ => !IsBusy);
        BrowseOutputFolderCommand = new RelayCommand(_ => BrowseOutputFolder(), _ => !UseSameFolderAsSource && !IsBusy);
        BrowseFfmpegCommand = new RelayCommand(_ => BrowseFfmpegFolder());
        RemoveSelectedCommand = new RelayCommand(p => RemoveSelected(p), _ => !IsBusy);

        RefreshFfmpeg();
        AppendLog("Ready. Add movie files or a folder to begin.");
    }

    public ObservableCollection<ConversionJob> Jobs { get; }
    public IReadOnlyList<OutputFormat> FormatOptions { get; }

    public ICommand AddFilesCommand { get; }
    public ICommand AddFolderCommand { get; }
    public ICommand StartCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand ClearFinishedCommand { get; }
    public ICommand ClearAllCommand { get; }
    public ICommand BrowseOutputFolderCommand { get; }
    public ICommand BrowseFfmpegCommand { get; }
    public ICommand RemoveSelectedCommand { get; }

    public string LogText
    {
        get => _logText;
        private set
        {
            if (_logText == value) return;
            _logText = value;
            OnPropertyChanged();
        }
    }

    public string FfmpegStatus
    {
        get => _ffmpegStatus;
        private set
        {
            if (_ffmpegStatus == value) return;
            _ffmpegStatus = value;
            OnPropertyChanged();
        }
    }

    public string OverallStatus
    {
        get => _overallStatus;
        private set
        {
            if (_overallStatus == value) return;
            _overallStatus = value;
            OnPropertyChanged();
        }
    }

    public double OverallProgress
    {
        get => _overallProgress;
        private set
        {
            if (Math.Abs(_overallProgress - value) < 0.01) return;
            _overallProgress = value;
            OnPropertyChanged();
        }
    }

    public string EstimatedTimeRemaining
    {
        get => _estimatedTimeRemaining;
        private set
        {
            if (_estimatedTimeRemaining == value) return;
            _estimatedTimeRemaining = value;
            OnPropertyChanged();
        }
    }

    public OutputFormat SelectedFormat
    {
        get => _selectedFormat;
        set
        {
            if (_selectedFormat == value) return;
            _selectedFormat = value;
            OnPropertyChanged();
        }
    }

    public bool UseSameFolderAsSource
    {
        get => _useSameFolder;
        set
        {
            if (_useSameFolder == value) return;
            _useSameFolder = value;
            OnPropertyChanged();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string? CustomOutputFolder
    {
        get => _customOutputFolder;
        set
        {
            if (_customOutputFolder == value) return;
            _customOutputFolder = value;
            OnPropertyChanged();
        }
    }

    public bool OverwriteExisting
    {
        get => _overwriteExisting;
        set
        {
            if (_overwriteExisting == value) return;
            _overwriteExisting = value;
            OnPropertyChanged();
        }
    }

    public bool IncludeSubfolders
    {
        get => _includeSubfolders;
        set
        {
            if (_includeSubfolders == value) return;
            _includeSubfolders = value;
            OnPropertyChanged();
        }
    }

    public bool DeleteOriginalAfterSuccess
    {
        get => _deleteOriginalAfterSuccess;
        set
        {
            if (_deleteOriginalAfterSuccess == value) return;
            _deleteOriginalAfterSuccess = value;
            OnPropertyChanged();
        }
    }

    public bool SkipCompatibleCodec
    {
        get => _skipCompatibleCodec;
        set
        {
            if (_skipCompatibleCodec == value) return;
            _skipCompatibleCodec = value;
            OnPropertyChanged();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value) return;
            _isBusy = value;
            OnPropertyChanged();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public void AddDroppedPaths(IEnumerable<string> paths)
    {
        var count = _queue.AddFiles(paths, IncludeSubfolders);
        if (count == 0)
            AppendLog("No supported video files found in the drop.");
        RecalculateOverall();
    }

    private void AddFiles()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select movie files",
            Multiselect = true,
            Filter = "Video files|*.mkv;*.mp4;*.avi;*.mov;*.m4v;*.wmv;*.webm;*.ts;*.m2ts;*.mpg;*.mpeg|All files|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            _queue.AddFiles(dialog.FileNames, includeSubfolders: false);
            RecalculateOverall();
        }
    }

    private void AddFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select a folder with movies"
        };

        if (dialog.ShowDialog() == true)
        {
            var count = _queue.AddFiles(new[] { dialog.FolderName }, IncludeSubfolders);
            if (count == 0)
                AppendLog("No supported video files found in that folder.");
            RecalculateOverall();
        }
    }

    private void BrowseOutputFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Choose output folder" };
        if (dialog.ShowDialog() == true)
            CustomOutputFolder = dialog.FolderName;
    }

    private void BrowseFfmpegFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Select folder containing ffmpeg.exe" };
        if (dialog.ShowDialog() != true)
            return;

        if (_locator.TryLocate(dialog.FolderName))
        {
            FfmpegStatus = _locator.DiscoveryMessage ?? "ffmpeg ready";
            AppendLog(FfmpegStatus);
        }
        else
        {
            FfmpegStatus = _locator.DiscoveryMessage ?? "ffmpeg not found";
            AppendLog(FfmpegStatus);
            MessageBox.Show(
                "That folder does not contain both ffmpeg.exe and ffprobe.exe.",
                "FileConverter",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void RemoveSelected(object? parameter)
    {
        if (parameter is System.Collections.IEnumerable selected)
        {
            var jobs = selected.OfType<ConversionJob>().ToList();
            _queue.RemoveSelected(jobs);
            RecalculateOverall();
        }
    }

    private async Task StartAsync()
    {
        if (!_locator.TryLocate())
        {
            FfmpegStatus = _locator.DiscoveryMessage ?? "ffmpeg not found";
            AppendLog(FfmpegStatus);
            MessageBox.Show(
                "ffmpeg was not found. Install it (e.g. winget install Gyan.FFmpeg) or browse to its bin folder.",
                "FileConverter",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        FfmpegStatus = _locator.DiscoveryMessage ?? "ffmpeg ready";

        if (!UseSameFolderAsSource && string.IsNullOrWhiteSpace(CustomOutputFolder))
        {
            MessageBox.Show("Choose an output folder, or use the same folder as each source file.",
                "FileConverter", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var settings = new AppSettings
        {
            OutputFormat = SelectedFormat,
            UseSameFolderAsSource = UseSameFolderAsSource,
            CustomOutputFolder = CustomOutputFolder,
            OverwriteExisting = OverwriteExisting,
            IncludeSubfolders = IncludeSubfolders,
            DeleteOriginalAfterSuccess = DeleteOriginalAfterSuccess,
            SkipCompatibleCodec = SkipCompatibleCodec
        };

        IsBusy = true;
        try
        {
            await _queue.RunAsync(settings);
        }
        finally
        {
            IsBusy = false;
            RecalculateOverall();
        }
    }

    private void RefreshFfmpeg()
    {
        if (_locator.TryLocate())
            FfmpegStatus = _locator.DiscoveryMessage ?? "ffmpeg ready";
        else
            FfmpegStatus = _locator.DiscoveryMessage ?? "ffmpeg not found";
    }

    private void OnJobsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (ConversionJob job in e.NewItems)
                job.PropertyChanged += OnJobPropertyChanged;
        }

        if (e.OldItems is not null)
        {
            foreach (ConversionJob job in e.OldItems)
                job.PropertyChanged -= OnJobPropertyChanged;
        }

        RecalculateOverall();
        CommandManager.InvalidateRequerySuggested();
    }

    private void OnJobPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConversionJob.ProgressPercent)
            or nameof(ConversionJob.Status)
            or nameof(ConversionJob.Speed)
            or nameof(ConversionJob.Duration)
            or nameof(ConversionJob.CountsTowardEncodeProgress))
        {
            RecalculateOverall();
        }
    }

    private void OnQueueStateChanged()
    {
        IsBusy = _queue.IsRunning;
        RecalculateOverall();
    }

    private void RecalculateOverall()
    {
        if (Jobs.Count == 0)
        {
            OverallProgress = 0;
            OverallStatus = "Idle";
            EstimatedTimeRemaining = "—";
            _lastSpeedFactor = null;
            return;
        }

        if (_queue.IsScanning)
        {
            OverallProgress = _queue.ScanTotal == 0
                ? 0
                : 100.0 * _queue.ScanCurrent / _queue.ScanTotal;
            OverallStatus = $"Scanning {_queue.ScanCurrent} / {_queue.ScanTotal}";
            EstimatedTimeRemaining = "Scanning…";
            return;
        }

        var work = Jobs.Where(j => j.CountsTowardEncodeProgress).ToList();
        var skipped = Jobs.Count(j => j.Status == JobStatus.Skipped);
        var running = work.FirstOrDefault(j => j.Status == JobStatus.Running);

        if (work.Count == 0)
        {
            OverallProgress = Jobs.All(j => j.Status is JobStatus.Skipped or JobStatus.Done or JobStatus.Failed or JobStatus.Cancelled)
                ? 100
                : 0;
            OverallStatus = skipped > 0
                ? $"No encodes needed — {skipped} skipped"
                : "Idle";
            EstimatedTimeRemaining = "—";
            return;
        }

        double totalWeight = 0;
        double doneWeight = 0;
        double remainingMediaSeconds = 0;

        foreach (var job in work)
        {
            var weight = Math.Max(job.Duration?.TotalSeconds ?? 60, 1);
            totalWeight += weight;

            switch (job.Status)
            {
                case JobStatus.Done:
                case JobStatus.Failed:
                case JobStatus.Cancelled:
                    doneWeight += weight;
                    break;
                case JobStatus.Running:
                    doneWeight += weight * (job.ProgressPercent / 100.0);
                    remainingMediaSeconds += weight * (1.0 - job.ProgressPercent / 100.0);
                    break;
                default:
                    remainingMediaSeconds += weight;
                    break;
            }
        }

        OverallProgress = totalWeight <= 0 ? 0 : doneWeight / totalWeight * 100.0;

        var finishedWork = work.Count(j => j.Status is JobStatus.Done or JobStatus.Failed or JobStatus.Cancelled);
        OverallStatus = running is null
            ? $"Encode {finishedWork} / {work.Count}  ·  {skipped} skipped"
            : $"{running.FileName}  ·  encode {finishedWork} / {work.Count}  ·  {skipped} skipped";

        var speed = ConversionService.ParseSpeedFactor(running?.Speed) ?? _lastSpeedFactor;
        if (speed is > 0)
            _lastSpeedFactor = speed;

        if (!_queue.IsRunning || remainingMediaSeconds <= 0.5)
        {
            EstimatedTimeRemaining = _queue.IsRunning && finishedWork >= work.Count ? "Finishing…" : "—";
            return;
        }

        if (speed is null or <= 0)
        {
            EstimatedTimeRemaining = "Estimating…";
            return;
        }

        var eta = TimeSpan.FromSeconds(remainingMediaSeconds / speed.Value);
        EstimatedTimeRemaining = FormatEta(eta);
    }

    private static string FormatEta(TimeSpan eta)
    {
        if (eta.TotalHours >= 1)
            return $"{(int)eta.TotalHours}h {eta.Minutes:D2}m";
        if (eta.TotalMinutes >= 1)
            return $"{eta.Minutes}m {eta.Seconds:D2}s";
        return $"{Math.Max(1, (int)eta.TotalSeconds)}s";
    }

    private void AppendLog(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss}  {message}";
        _logBuilder.AppendLine(line);
        LogText = _logBuilder.ToString();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
