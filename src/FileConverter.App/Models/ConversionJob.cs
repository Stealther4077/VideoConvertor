using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FileConverter.App.Models;

public sealed class ConversionJob : INotifyPropertyChanged
{
    private JobStatus _status = JobStatus.Pending;
    private double _progressPercent;
    private string _statusDetail = "Waiting";
    private string? _errorMessage;
    private string? _outputPath;
    private TimeSpan? _duration;
    private string? _speed;
    private bool _countsTowardEncodeProgress;

    public required string SourcePath { get; init; }

    public string FileName => Path.GetFileName(SourcePath);

    /// <summary>
    /// True when this job needs (or needed) a real encode in the current run.
    /// Used so overall progress ignores quick skips after the pre-scan.
    /// </summary>
    public bool CountsTowardEncodeProgress
    {
        get => _countsTowardEncodeProgress;
        set
        {
            if (_countsTowardEncodeProgress == value) return;
            _countsTowardEncodeProgress = value;
            OnPropertyChanged();
        }
    }

    public JobStatus Status
    {
        get => _status;
        set
        {
            if (_status == value) return;
            _status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public double ProgressPercent
    {
        get => _progressPercent;
        set
        {
            var clamped = Math.Clamp(value, 0, 100);
            if (Math.Abs(_progressPercent - clamped) < 0.01) return;
            _progressPercent = clamped;
            OnPropertyChanged();
        }
    }

    public string StatusDetail
    {
        get => _statusDetail;
        set
        {
            if (_statusDetail == value) return;
            _statusDetail = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (_errorMessage == value) return;
            _errorMessage = value;
            OnPropertyChanged();
        }
    }

    public string? OutputPath
    {
        get => _outputPath;
        set
        {
            if (_outputPath == value) return;
            _outputPath = value;
            OnPropertyChanged();
        }
    }

    public TimeSpan? Duration
    {
        get => _duration;
        set
        {
            if (_duration == value) return;
            _duration = value;
            OnPropertyChanged();
        }
    }

    public string? Speed
    {
        get => _speed;
        set
        {
            if (_speed == value) return;
            _speed = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public string StatusText => Status switch
    {
        JobStatus.Pending => "Pending",
        JobStatus.Running => string.IsNullOrWhiteSpace(Speed)
            ? $"{ProgressPercent:0}% — {StatusDetail}"
            : $"{ProgressPercent:0}% — {StatusDetail} @ {Speed}",
        JobStatus.Done => "Done",
        JobStatus.Failed => string.IsNullOrWhiteSpace(ErrorMessage) ? "Failed" : $"Failed — {ErrorMessage}",
        JobStatus.Cancelled => "Cancelled",
        JobStatus.Skipped => string.IsNullOrWhiteSpace(StatusDetail) ? "Skipped" : $"Skipped — {StatusDetail}",
        _ => Status.ToString()
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
