using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CUE4Parse_Conversion;
using CUE4Parse_Conversion.Options;
using CUE4Parse.Utils;
using FModel.Extensions;
using FModel.Framework;
using FModel.Settings;
using FModel.Views;
using FModel.Views.Resources.Controls;
using FModel.Views.Snooper;
using Serilog.Events;

namespace FModel.ViewModels;

public class ExportSessionViewModel : ViewModel
{
    public static ExportSessionViewModel Instance { get; } = new();

    private DispatcherTimer? _toastTimer;
    public bool ShowQueueToast
    {
        get;
        set
        {
            if (!SetProperty(ref field, value)) return;
            if (!value)
            {
                _toastTimer?.Stop();
                return;
            }

            if (_toastTimer == null)
            {
                _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                _toastTimer.Tick += (_, _) =>
                {
                    field = false;
                    RaisePropertyChanged(nameof(ShowQueueToast));
                    _toastTimer.Stop();
                };
            }

            _toastTimer.Stop();
            _toastTimer.Start();
        }
    }

    private ExportSession? _session;
    public ExportSession Session
    {
        get
        {
            if (_session != null) return _session;
            _session = new ExportSession((args, ct) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var window = new StreamingLevelFilterWindow(new StreamingLevelFilterViewModel(args));
                    _stopwatch.Stop();
                    window.ShowDialog();
                    _stopwatch.Start();
                }, DispatcherPriority.Normal, ct);
            })
            {
                Extension = FModel.Services.FModelV3ExportSessionExtension.Current
            };
            _session.PropertyChanged += OnSessionPropertyChanged;
            return _session;
        }
    }

    public ExportOptionsViewModel Options { get; } = new();

    public bool IsRunning
    {
        get;
        private set
        {
            if (!SetProperty(ref field, value)) return;
            RaisePropertyChanged(nameof(CanExport));
        }
    }
    public bool IsFinished
    {
        get;
        private set => SetProperty(ref field, value);
    }
    public bool CanExport => !IsRunning && Session.TotalQueued > 0;

    public int CompletedCount
    {
        get;
        private set => SetProperty(ref field, value);
    }
    public int SucceededCount
    {
        get;
        private set => SetProperty(ref field, value);
    }
    public int FailedCount
    {
        get;
        private set
        {
            if (!SetProperty(ref field, value)) return;
            RaisePropertyChanged(nameof(HasFailures));
        }
    }
    public bool HasFailures => FailedCount > 0;
    public string? CurrentItemName
    {
        get;
        private set => SetProperty(ref field, value);
    }
    public TimeSpan ElapsedTime
    {
        get;
        private set => SetProperty(ref field, value);
    }
    public TimeSpan? EtaTime
    {
        get;
        private set => SetProperty(ref field, value);
    }
    public bool IsCanceled
    {
        get;
        private set => SetProperty(ref field, value);
    }
    public double ProgressValue
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public ObservableCollection<ClassGroupViewModel> ClassGroups { get; } = [];

    private readonly object _ctsLock = new();
    private readonly object _logLock = new();
    private readonly object _progressLock = new();
    private CancellationTokenSource? _cts;
    private readonly Stopwatch _stopwatch = new();
    private readonly ConcurrentQueue<LogEvent> _pendingLogs = new();
    private readonly Dictionary<string, ClassGroupViewModel> _classGroupLookup = new(StringComparer.Ordinal);
    private readonly Dictionary<ClassGroupViewModel, Dictionary<string, ObjectGroupViewModel>> _objectGroupLookup = [];
    private const int MaxPendingLogEvents = 4096;
    private const int MaxRetainedLogEntries = 10000;
    private int _pendingLogCount;
    private int _logDrainScheduled;
    private int _retainedLogCount;
    private int _droppedLogCount;
    private ExportProgress _latestProgress;
    private int _progressSucceededCount;
    private int _progressFailedCount;
    private long _progressGeneration;
    private int _progressDrainScheduled;
    private DispatcherTimer? _uiTimer;

    private ExportSessionViewModel()
    {
        ImGuiSink.Instance.OnExporterLogEvent += OnLogEvent;
    }

    private void OnLogEvent(LogEvent log)
    {
        lock (_logLock)
        {
            _pendingLogs.Enqueue(log);
            _pendingLogCount++;
            while (_pendingLogCount > MaxPendingLogEvents && _pendingLogs.TryDequeue(out _))
            {
                _pendingLogCount--;
            }
        }

        ScheduleLogDrain();
    }

    private void ScheduleLogDrain()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || Interlocked.Exchange(ref _logDrainScheduled, 1) != 0)
            return;

        try
        {
            dispatcher.InvokeAsync(() =>
            {
                try
                {
                    DrainLogs();
                }
                finally
                {
                    Interlocked.Exchange(ref _logDrainScheduled, 0);
                    if (Volatile.Read(ref _pendingLogCount) > 0)
                        ScheduleLogDrain();
                }
            });
        }
        catch
        {
            Interlocked.Exchange(ref _logDrainScheduled, 0);
        }
    }

    private int _previousCount;
    private int _sessionUpdateScheduled;
    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ExportSession.TotalQueued)) return;

        ScheduleSessionUiUpdate();
    }

    private void ScheduleSessionUiUpdate()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || Interlocked.Exchange(ref _sessionUpdateScheduled, 1) != 0)
            return;

        try
        {
            dispatcher.InvokeAsync(() =>
            {
                try
                {
                    var count = _session?.TotalQueued ?? 0;
                    if (count > 0 && _previousCount == 0)
                    {
                        ClearExportHistory();
                    }

                    ShowQueueToast = count switch
                    {
                        > 0 when _previousCount == 0 => true,
                        0 => false,
                        _ => ShowQueueToast
                    };
                    _previousCount = count;
                    RaisePropertyChanged(nameof(CanExport));
                }
                finally
                {
                    Interlocked.Exchange(ref _sessionUpdateScheduled, 0);
                    if ((_session?.TotalQueued ?? 0) != _previousCount)
                        ScheduleSessionUiUpdate();
                }
            });
        }
        catch
        {
            Interlocked.Exchange(ref _sessionUpdateScheduled, 0);
        }
    }

    public async Task<IReadOnlyList<ExportResult>> ExportAsync()
    {
        if (IsRunning || Session.TotalQueued == 0) return null;
        if (FModel.Services.FModelV3ContainerExportScope.IsBatchRunning) return null;

        IsRunning = true;
        IsFinished = false;
        IsCanceled = false;
        CompletedCount = 0;
        SucceededCount = 0;
        FailedCount = 0;
        _stopwatch.Restart();
        var progressGeneration = BeginProgressGeneration();

        CancellationTokenSource cts;
        lock (_ctsLock)
        {
            cts = new CancellationTokenSource();
            _cts = cts;
        }
        StartUiTimer();

        string exportDirectory;
        ExportOptions exportOptions;
        if (Options.OverrideOptions)
        {
            exportDirectory = Options.OutputDirectory;
            exportOptions = Options.BuildOptions();
        }
        else
        {
            exportDirectory = UserSettings.Default.ModelDirectory;
            exportOptions = UserSettings.GetExportOptions();
        }

        // Do not use Progress<T> here: when constructed on the WPF thread it posts
        // one Dispatcher work item per asset. A large export would therefore queue
        // thousands of progress callbacks and retain their ExportResult payloads.
        // DirectExportProgress records only the latest snapshot and coalesces the UI
        // drain to one dispatcher item at a time.
        var progress = new DirectExportProgress(p => QueueProgress(p, progressGeneration));

        IReadOnlyList<ExportResult> results = null;
        try
        {
            results = await Session.RunAsync(exportDirectory, exportOptions, progress, cts.Token).ConfigureAwait(false);
            // The additive UEFormat layer verifies only its declared resources
            // before terminal UI counts/automatic summaries consume the results.
            results = FModel.Services.FModelV3ExportFeedback.Report(results, exportDirectory,
                exportOptions.MeshFormat == EMeshFormat.UEFormat, Session.OperationId, cts.Token);
            if (exportOptions.MeshFormat == EMeshFormat.UEFormat)
            {
                SetUeFormatTerminalProgress(results, progressGeneration);
            }
        }
        catch (OperationCanceledException)
        {
            FModel.Services.FModelV3ExportFeedback.ReportCanceled(Session.OperationId);
            Application.Current?.Dispatcher.InvokeAsync(() => IsCanceled = true);
        }
        catch
        {
            // Unexpected host/progress failures must not leave the static V3
            // ownership registry retaining every queued ObjectPath.
            FModelV3ExporterFactory.ClearPending(Session.OperationId);
            throw;
        }
        finally
        {
            lock (_ctsLock)
            {
                if (ReferenceEquals(_cts, cts))
                    _cts = null;
                cts.Dispose();
            }

            _stopwatch.Stop();
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                StopUiTimer();
                IsRunning = false;
                IsFinished = !IsCanceled;
                UpdateElapsedAndEta();
            });
        }
        return results;
    }

    internal static async Task<System.Collections.Generic.IReadOnlyList<ExportResult>> RunExplicitAsync(
        ExportSession session,
        string exportDirectory,
        ExportOptions exportOptions,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(exportOptions);
        if (string.IsNullOrWhiteSpace(exportDirectory))
            throw new ArgumentException("An explicit export directory is required.", nameof(exportDirectory));

        System.IO.Directory.CreateDirectory(exportDirectory);
        return await session.RunAsync(exportDirectory, exportOptions, progress, cancellationToken).ConfigureAwait(false);
    }
    public async Task ExportAutomaticallyAsync()
    {
        if (!UserSettings.Default.ExportImmediately) return;

        var results = await ExportAsync();
        if (results is { Count: > 0 }) LogSummary(results);
    }

    private static void LogSummary(IReadOnlyList<ExportResult> results)
    {
        if (results.Count == 1 && results[0].DiskFilePaths is not { Count: > 1 })
        {
            var result = results[0];
            switch (result.Success)
            {
                case true when result.DiskFilePaths is { Count: > 0 } files:
                    FLogger.Append(ELog.Information, () =>
                    {
                        FLogger.Text("Successfully exported ", Constants.WHITE);
                        FLogger.Link(Path.GetFileName(files[0]), files[0], true);
                    });
                    break;
                case false when result.Error is { } exception:
                    FLogger.Append(exception);
                    break;
            }
            return;
        }

        var failed = 0;
        var groups = new Dictionary<string, (int Count, string[] Source, string[] Directory)>(StringComparer.OrdinalIgnoreCase);

        foreach (var result in results)
        {
            if (!result.Success)
            {
                failed++;
                continue;
            }

            if (result.DiskFilePaths == null) continue;

            foreach (var file in result.DiskFilePaths)
            {
                var extension = Path.GetExtension(file).TrimStart('.');
                var source = SplitDirectory(result.ObjectPath.TrimStart('/', '\\'));
                var directory = SplitDirectory(file);
                groups[extension] = groups.TryGetValue(extension, out var group)
                    ? (group.Count + 1, CommonPrefix(group.Source, source), CommonPrefix(group.Directory, directory))
                    : (1, source, directory);
            }
        }

        foreach (var (extension, group) in groups)
        {
            var source = string.Join('/', group.Source);
            var directory = string.Join(Path.DirectorySeparatorChar, group.Directory);
            if (source.Length == 0)
                source = "multiple folders";

            FLogger.Append(ELog.Information, () =>
            {
                FLogger.Text($"Successfully exported {group.Count} {extension} from ", Constants.WHITE);
                if (directory.Length > 0) FLogger.Link(source, directory, true);
                else FLogger.Text(source, Constants.WHITE, true);
            });
        }

        if (failed > 0)
        {
            FLogger.Append(ELog.Error, () => FLogger.Text($"Failed to export {failed} asset{(failed == 1 ? "" : "s")}, open the Export Session window for more details.", Constants.WHITE, true));
        }
    }

    private static string[] SplitDirectory(string path) => Path.GetDirectoryName(path)?.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) ?? [];
    private static string[] CommonPrefix(string[] a, string[] b)
    {
        var i = 0;
        while (i < a.Length && i < b.Length && string.Equals(a[i], b[i], StringComparison.OrdinalIgnoreCase)) i++;
        return i == a.Length ? a : a[..i];
    }

    public void CancelExport()
    {
        lock (_ctsLock)
        {
            _cts?.Cancel();
        }
    }

    public void ClearQueue()
    {
        if (IsRunning) return;
        _session?.Clear();
        if (_session is not null)
            FModelV3ExporterFactory.ClearPending(_session.OperationId);
        ClearExportHistory();
    }

    public void RemoveFromQueue(ObjectGroupViewModel item)
    {
        if (IsRunning || _session?.Remove(item.Path) != true)
            return;

        FModelV3ExporterFactory.ForgetOwnedObjectPath(Session.OperationId, item.Path);
        var group = ClassGroups.FirstOrDefault(x => x.Objects.Contains(item));
        if (group == null)
            return;

        group.Objects.Remove(item);
        if (_objectGroupLookup.TryGetValue(group, out var objects))
            objects.Remove(item.Path);
        if (group.Objects.Count == 0)
        {
            ClassGroups.Remove(group);
            _classGroupLookup.Remove(group.Name);
            _objectGroupLookup.Remove(group);
        }
    }

    private void StartUiTimer()
    {
        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _uiTimer.Tick += (_, _) => UpdateElapsedAndEta();
        _uiTimer.Start();
    }

    private void StopUiTimer()
    {
        _uiTimer?.Stop();
        _uiTimer = null;
    }

    private void UpdateElapsedAndEta()
    {
        ElapsedTime = _stopwatch.Elapsed;

        var remaining = Session.TotalQueued;
        if (IsRunning && remaining > 0 && CompletedCount > 1 && ElapsedTime.TotalSeconds > 0)
        {
            var rate = CompletedCount / ElapsedTime.TotalSeconds;
            if (rate > 0)
            {
                EtaTime = TimeSpan.FromSeconds(remaining / rate);
                return;
            }
        }
        EtaTime = null;
    }

    private long BeginProgressGeneration()
    {
        lock (_progressLock)
        {
            var generation = ++_progressGeneration;
            _latestProgress = new ExportProgress(0, 0);
            _progressSucceededCount = 0;
            _progressFailedCount = 0;
            return generation;
        }
    }

    private bool IsCurrentProgressGeneration(long generation)
    {
        lock (_progressLock)
        {
            return generation == _progressGeneration;
        }
    }

    // Publish effective UEFormat terminal results into the same snapshot read by
    // Background drains. Updating only public UI counts allowed an older queued
    // drain to restore raw root-success counts after required-texture failure.
    private void SetUeFormatTerminalProgress(IReadOnlyList<ExportResult> results, long generation)
    {
        lock (_progressLock)
        {
            if (!IsCurrentProgressGeneration(generation)) return;
            _latestProgress = new ExportProgress(results.Count, results.Count, results.LastOrDefault());
            _progressSucceededCount = results.Count(static result => result.Success);
            _progressFailedCount = results.Count - _progressSucceededCount;
        }
        ScheduleProgressDrain();
    }

    private void QueueProgress(ExportProgress progress, long generation)
    {
        lock (_progressLock)
        {
            if (generation != _progressGeneration)
                return;

            _latestProgress = progress;
            if (progress.LastResult is { Success: true })
                _progressSucceededCount++;
            else if (progress.LastResult is not null)
                _progressFailedCount++;
        }

        ScheduleProgressDrain();
    }

    private void ScheduleProgressDrain()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            DrainProgress();
            return;
        }

        if (Interlocked.Exchange(ref _progressDrainScheduled, 1) != 0)
            return;

        try
        {
            dispatcher.InvokeAsync(DrainProgress, DispatcherPriority.Background);
        }
        catch
        {
            Interlocked.Exchange(ref _progressDrainScheduled, 0);
        }
    }

    private void DrainProgress()
    {
        ExportProgress progress;
        int succeeded;
        int failed;

        lock (_progressLock)
        {
            progress = _latestProgress;
            succeeded = _progressSucceededCount;
            failed = _progressFailedCount;
        }

        CompletedCount = progress.Completed;
        SucceededCount = succeeded;
        FailedCount = failed;
        CurrentItemName = progress.LastResult?.ObjectPath;
        ProgressValue = progress.Total > 0 ? (double)progress.Completed / progress.Total : 0;

        Interlocked.Exchange(ref _progressDrainScheduled, 0);
        var needsDrain = false;
        lock (_progressLock)
        {
            if (_latestProgress != progress
                || _progressSucceededCount != succeeded
                || _progressFailedCount != failed)
            {
                needsDrain = true;
            }
        }
        if (needsDrain)
            ScheduleProgressDrain();
    }

    private void ClearExportHistory()
    {
        lock (_logLock)
        {
            _pendingLogs.Clear();
            _pendingLogCount = 0;
        }
        Interlocked.Exchange(ref _retainedLogCount, 0);
        Interlocked.Exchange(ref _droppedLogCount, 0);

        CompletedCount = 0;
        SucceededCount = 0;
        FailedCount = 0;
        ProgressValue = 0;
        ElapsedTime = TimeSpan.Zero;
        EtaTime = null;
        CurrentItemName = null;
        IsFinished = false;
        IsCanceled = false;
        ClassGroups.Clear();
        _classGroupLookup.Clear();
        _objectGroupLookup.Clear();
    }

    private sealed class DirectExportProgress(Action<ExportProgress> callback) : IProgress<ExportProgress>
    {
        public void Report(ExportProgress value) => callback(value);
    }

    private void DrainLogs()
    {
        while (true)
        {
            LogEvent log;
            lock (_logLock)
            {
                if (!_pendingLogs.TryDequeue(out log!))
                    break;
                _pendingLogCount--;
            }

            if (Volatile.Read(ref _retainedLogCount) >= MaxRetainedLogEntries)
            {
                Interlocked.Increment(ref _droppedLogCount);
                continue;
            }

            var className = log.GetContext("ClassName");
            var objectPath = log.GetContext("ObjectPath");
            var filePath = log.GetContext("FilePath");

            var cg = FindOrCreateClass(className);
            var og = FindOrCreateObject(cg, objectPath);
            if (log.Level >= LogEventLevel.Error)
            {
                og.ErrorCount++;
                cg.ErrorCount++;
            }
            if (og.FirstFilePath == null && !string.IsNullOrEmpty(filePath))
                og.FirstFilePath = filePath;
            og.Entries.Add(new LogEntryViewModel(log));
            Interlocked.Increment(ref _retainedLogCount);
        }
    }

    private ClassGroupViewModel FindOrCreateClass(string name)
    {
        if (_classGroupLookup.TryGetValue(name, out var existing))
            return existing;

        var cg = new ClassGroupViewModel(name);
        ClassGroups.Add(cg);
        _classGroupLookup.Add(name, cg);
        return cg;
    }

    private ObjectGroupViewModel FindOrCreateObject(ClassGroupViewModel cg, string path)
    {
        if (!_objectGroupLookup.TryGetValue(cg, out var objects))
        {
            objects = new Dictionary<string, ObjectGroupViewModel>(StringComparer.Ordinal);
            _objectGroupLookup.Add(cg, objects);
        }

        if (objects.TryGetValue(path, out var existing))
            return existing;

        var og = new ObjectGroupViewModel(path);
        cg.Objects.Add(og);
        objects.Add(path, og);
        return og;
    }
}

public class ClassGroupViewModel(string name) : ViewModel
{
    public string Name { get; } = name;
    public ObservableCollection<ObjectGroupViewModel> Objects { get; } = [];

    public bool IsExpanded
    {
        get;
        set => SetProperty(ref field, value);
    }

    public int ErrorCount
    {
        get;
        set
        {
            SetProperty(ref field, value);
            RaisePropertyChanged(nameof(HasErrors));
        }
    }
    public override bool HasErrors => ErrorCount > 0;
}

public class ObjectGroupViewModel(string path) : ViewModel
{
    public string Path { get; } = path;
    public string Directory { get; } = path.SubstringBeforeLast('/');
    public string Name { get; } = path.SubstringAfterLast('.');
    public ObservableCollection<LogEntryViewModel> Entries { get; } = [];

    public bool IsExpanded
    {
        get;
        set => SetProperty(ref field, value);
    }

    public int ErrorCount
    {
        get;
        set
        {
            SetProperty(ref field, value);
            RaisePropertyChanged(nameof(HasErrors));
        }
    }
    public override bool HasErrors => ErrorCount > 0;

    public string? FirstFilePath
    {
        get;
        set
        {
            SetProperty(ref field, value);
            RaisePropertyChanged(nameof(HasFilePath));
        }
    }
    public bool HasFilePath => FirstFilePath != null;
}

public class LogEntryViewModel(LogEvent log)
{
    public LogEventLevel Level { get; } = log.Level;
    public DateTimeOffset Timestamp { get; } = log.Timestamp;
    public string Message { get; } = log.Exception switch
    {
        NullReferenceException or ArgumentException => log.RenderMessage(),
        _ => log.Exception?.Message ?? log.RenderMessage()
    };
    public Exception? Exception { get; } = log.Exception;
    public IReadOnlyList<ExceptionDetailsViewModel> ExceptionDetails { get; } =
        log.Exception is { } exception ? [new ExceptionDetailsViewModel(exception)] : [];
}

public class ExceptionDetailsViewModel
{
    public string Header { get; }
    public string Details { get; }

    public ExceptionDetailsViewModel(Exception exception)
    {
        var text = exception.ToString().ReplaceLineEndings("\n");
        var newline = text.IndexOf('\n');

        if (newline < 0)
        {
            Header = text;
            Details = string.Empty;
        }
        else
        {
            Header = text[..newline];
            Details = text[(newline + 1)..];
        }
    }
}
