#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using CUE4Parse.FModelUEFormat.ExportPipeline.Pipeline;
using CUE4Parse_Conversion;
using CUE4Parse_Conversion.Exporters;
using FModel.ViewModels;

namespace FModel.Services;

/// <summary>
/// Host-side scope used by the container batch runner.
/// It supplies an explicit ExportSession and one immutable V3 policy for the
/// duration of a batch, without changing UserSettings output directories.
/// </summary>
internal static class FModelV3ContainerExportScope
{
    private static readonly AsyncLocal<Context?> CurrentContext = new();
    private static readonly SemaphoreSlim BatchGate = new(1, 1);
    private static int _batchRunning;

    internal static ExportSession? CurrentSession => CurrentContext.Value?.Session;
    internal static FModelUeFormatPolicy? CurrentPolicy => CurrentContext.Value?.Policy;
    internal static bool IsBatchRunning => Volatile.Read(ref _batchRunning) == 1;

    internal static async Task<BatchLease> EnterBatchAsync(CancellationToken cancellationToken = default)
    {
        await BatchGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        if (ExportSessionViewModel.Instance.IsRunning || ExportSessionViewModel.Instance.Session.IsRunning)
        {
            BatchGate.Release();
            throw new InvalidOperationException("A normal Export Session is already running; container batch execution is blocked.");
        }

        IDisposable? stateLock = null;
        try
        {
            Volatile.Write(ref _batchRunning, 1);
            stateLock = FModelV3ExportSessionState.Current.EnterBatchLock();
            var policy = FModelV3ExporterFactory.CreatePolicySnapshot();
            return new BatchLease(policy, stateLock);
        }
        catch
        {
            stateLock?.Dispose();
            Volatile.Write(ref _batchRunning, 0);
            BatchGate.Release();
            throw;
        }
    }

    internal static ScopeLease Push(ExportSession session, FModelUeFormatPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(policy);

        if (CurrentContext.Value is not null)
            throw new InvalidOperationException("An explicit container export session is already active on this async flow.");

        var previous = CurrentContext.Value;
        CurrentContext.Value = new Context(session, policy);
        return new ScopeLease(previous);
    }

    internal sealed class BatchLease : IDisposable
    {
        private readonly IDisposable _stateLock;
        private int _disposed;

        internal BatchLease(FModelUeFormatPolicy policy, IDisposable stateLock)
        {
            Policy = policy;
            _stateLock = stateLock;
        }

        internal FModelUeFormatPolicy Policy { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _stateLock.Dispose();
            Volatile.Write(ref _batchRunning, 0);
            BatchGate.Release();
        }
    }

    internal sealed class ScopeLease : IDisposable
    {
        private readonly Context? _previous;
        private int _disposed;

        internal ScopeLease(Context? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            CurrentContext.Value = _previous;
        }
    }

    internal sealed record Context(ExportSession Session, FModelUeFormatPolicy Policy);
}
