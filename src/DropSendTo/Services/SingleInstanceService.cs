using System;
using System.Threading;

namespace DropSendTo.Services;

internal sealed class SingleInstanceService : IDisposable
{
    private readonly string _mutexName;
    private Mutex? _mutex;
    private bool _ownsHandle;

    public SingleInstanceService(string mutexName)
    {
        if (string.IsNullOrWhiteSpace(mutexName))
        {
            throw new ArgumentException("Mutex name must be provided.", nameof(mutexName));
        }

        _mutexName = mutexName;
    }

    public bool TryAcquire()
    {
        if (_ownsHandle)
        {
            return true;
        }

        _mutex ??= new Mutex(false, _mutexName);
        try
        {
            _ownsHandle = _mutex.WaitOne(0, false);
        }
        catch (AbandonedMutexException)
        {
            // 別プロセスが異常終了した場合でもロックを引き継いで継続する。
            _ownsHandle = true;
        }
        catch (ObjectDisposedException)
        {
            _ownsHandle = false;
        }
        catch (UnauthorizedAccessException)
        {
            _ownsHandle = false;
        }

        return _ownsHandle;
    }

    public void Dispose()
    {
        if (_ownsHandle && _mutex != null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // 予期せぬ状態でもアプリは終了できるよう抑止。
            }
        }

        _ownsHandle = false;
        _mutex?.Dispose();
        _mutex = null;
    }
}
