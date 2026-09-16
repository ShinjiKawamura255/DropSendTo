using System;
using System.Threading;
using System.Threading.Tasks;
using DropSendTo.Models;

namespace DropSendTo.Services;

internal interface IMacroConcurrencyRuntime
{
    bool IsMacroRunning { get; }
    bool CancelCurrentMacro();
    Task<bool> CancelAllRunningMacrosAsync(CancellationToken cancellationToken);
    Task<IAsyncDisposable?> SuspendCurrentMacroAsync(TimeSpan timeout, CancellationToken cancellationToken);
}

internal enum MacroConcurrencyRejection
{
    Exclusive,
    SuspendFailed
}

internal enum MacroConcurrencyExecutionResult
{
    Executed,
    SameSlotCancellationRequested,
    ExclusiveRejected,
    SuspendRejected
}

internal sealed record MacroConcurrencyAdmission(
    MacroConcurrencyExecutionResult Result,
    IAsyncDisposable? Suspension)
{
    public bool ShouldProceed => Result == MacroConcurrencyExecutionResult.Executed;
}

internal sealed record MacroConcurrencyRequest(
    MacroConcurrencyMode Mode,
    bool ShouldRunMacro,
    bool IsSameSlotRunning,
    string TriggerDescription,
    Action OnCancelRequested,
    Action<bool> OnPauseChanged,
    Action<MacroConcurrencyRejection> OnRejected,
    Func<Exception, Task> OnResumeFailedAsync);

internal sealed class MacroConcurrencyCoordinator
{
    private static readonly TimeSpan SuspendTimeout = TimeSpan.FromSeconds(3);
    private readonly IMacroConcurrencyRuntime _runtime;
    private readonly IAppLogger _logger;

    public MacroConcurrencyCoordinator(IMacroConcurrencyRuntime runtime, IAppLogger logger)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<MacroConcurrencyExecutionResult> ExecuteAsync(
        MacroConcurrencyRequest request,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(operation);

        var admission = await EnterAsync(request, cancellationToken);
        if (!admission.ShouldProceed)
        {
            return admission.Result;
        }

        if (admission.Suspension == null)
        {
            await operation(cancellationToken);
        }
        else
        {
            await using var managedSuspension = admission.Suspension;
            await operation(cancellationToken);
        }

        return MacroConcurrencyExecutionResult.Executed;
    }

    public async Task<MacroConcurrencyAdmission> EnterAsync(
        MacroConcurrencyRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        IAsyncDisposable? suspension = null;
        if (_runtime.IsMacroRunning)
        {
            if (!request.ShouldRunMacro)
            {
                _logger.Info($"Command-only slot triggered while macro is active ({request.TriggerDescription}).");
            }
            else if (request.IsSameSlotRunning)
            {
                if (_runtime.CancelCurrentMacro())
                {
                    _logger.Info($"Requested cancel for running macro ({request.TriggerDescription}).");
                    request.OnCancelRequested();
                }
                return new MacroConcurrencyAdmission(
                    MacroConcurrencyExecutionResult.SameSlotCancellationRequested,
                    Suspension: null);
            }
            else
            {
                switch (request.Mode)
                {
                    case MacroConcurrencyMode.Interrupt:
                        _logger.Info($"Interrupting running macro before executing slot ({request.TriggerDescription}).");
                        request.OnCancelRequested();
                        await _runtime.CancelAllRunningMacrosAsync(cancellationToken);
                        break;

                    case MacroConcurrencyMode.SuspendAndResume:
                        request.OnPauseChanged(true);
                        Exception? suspensionFailure = null;
                        try
                        {
                            suspension = await _runtime.SuspendCurrentMacroAsync(SuspendTimeout, cancellationToken);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            request.OnPauseChanged(false);
                            throw;
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                        {
                            suspensionFailure = ex;
                        }

                        if (suspension == null)
                        {
                            var detail = suspensionFailure == null ? string.Empty : $": {suspensionFailure}";
                            _logger.Warn($"Rejected nested execution because the running macro could not be suspended ({request.TriggerDescription}){detail}");
                            request.OnPauseChanged(false);
                            request.OnRejected(MacroConcurrencyRejection.SuspendFailed);
                            return new MacroConcurrencyAdmission(
                                MacroConcurrencyExecutionResult.SuspendRejected,
                                Suspension: null);
                        }
                        break;

                    case MacroConcurrencyMode.Exclusive:
                    default:
                        _logger.Warn($"Rejected trigger while another macro is running ({request.TriggerDescription}).");
                        request.OnRejected(MacroConcurrencyRejection.Exclusive);
                        return new MacroConcurrencyAdmission(
                            MacroConcurrencyExecutionResult.ExclusiveRejected,
                            Suspension: null);
                }
            }
        }

        return new MacroConcurrencyAdmission(
            MacroConcurrencyExecutionResult.Executed,
            suspension == null ? null : new ManagedSuspension(suspension, request, _logger));
    }

    private sealed class ManagedSuspension : IAsyncDisposable
    {
        private readonly IAsyncDisposable _inner;
        private readonly MacroConcurrencyRequest _request;
        private readonly IAppLogger _logger;
        private bool _disposed;

        public ManagedSuspension(
            IAsyncDisposable inner,
            MacroConcurrencyRequest request,
            IAppLogger logger)
        {
            _inner = inner;
            _request = request;
            _logger = logger;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            try
            {
                await _inner.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to resume suspended macro: {ex}");
                await _request.OnResumeFailedAsync(ex);
            }
            finally
            {
                _request.OnPauseChanged(false);
            }
        }
    }
}
