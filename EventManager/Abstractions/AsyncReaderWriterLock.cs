using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace EventManager.Abstractions;

/// <summary>
/// Asynchronous reader/writer lock, allowing any number of readers OR a single writer to execute at the same time.
/// Based on code by Stephen Toub for the .NET blog: https://devblogs.microsoft.com/dotnet/building-async-coordination-primitives-part-7-asyncreaderwriterlock/
/// </summary>
[SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Part of the public API contract")]
public sealed class AsyncReaderWriterLock
{
    private readonly Task<IDisposable> _readerReleaser;
    private readonly Task<IDisposable> _writerReleaser;
    private readonly Lock _coreLock = new();
    private readonly Queue<TaskCompletionSource<IDisposable>> _waitingWriters = new();
    private TaskCompletionSource<IDisposable> _waitingReader = new();
    private int _readersWaiting;
    private int _status;

    /// <summary>
    /// Creates a new async reader-writer lock.
    /// </summary>
    public AsyncReaderWriterLock()
    {
        _readerReleaser = Task.FromResult<IDisposable>(new Releaser(this, false));
        _writerReleaser = Task.FromResult<IDisposable>(new Releaser(this, true));
    }

    /// <summary>
    /// Enters a reader lock or asynchronously waits to do so, releasing it once the result is disposed.
    /// </summary>
    public Task<IDisposable> EnterReaderLockAsync()
    {
        lock (_coreLock)
        {
            if (_status >= 0 && _waitingWriters.Count == 0)
            {
                _status += 1;
                return _readerReleaser;
            }

            _readersWaiting += 1;
            return _waitingReader.Task.ContinueWith(t => t.Result, TaskScheduler.Current);
        }
    }

    /// <summary>
    /// Enters the writer lock or asynchronously waits to do so, releasing it once the result is disposed.
    /// </summary>
    public Task<IDisposable> EnterWriterLockAsync()
    {
        lock (_coreLock)
        {
            if (_status == 0)
            {
                _status = -1;
                return _writerReleaser;
            }

            var waiter = new TaskCompletionSource<IDisposable>();
            _waitingWriters.Enqueue(waiter);
            return waiter.Task;
        }
    }

    private void ReaderRelease()
    {
        TaskCompletionSource<IDisposable>? toWake = null;

        lock (_coreLock)
        {
            _status -= 1;
            if (_status == 0 && _waitingWriters.Count > 0)
            {
                _status = -1;
                toWake = _waitingWriters.Dequeue();
            }
        }

        toWake?.SetResult(new Releaser(this, true));
    }

    private void WriterRelease()
    {
        TaskCompletionSource<IDisposable>? toWake;
        bool toWakeIsWriter = false;

        lock (_coreLock)
        {
            if (_waitingWriters.TryDequeue(out toWake))
            {
                toWakeIsWriter = true;
            }
            else if (_readersWaiting > 0)
            {
                toWake = _waitingReader;
                _status = _readersWaiting;
                _readersWaiting = 0;
                _waitingReader = new();
            }
            else
            {
                toWake = null;
                _status = 0;
            }
        }

        toWake?.SetResult(new Releaser(this, toWakeIsWriter));
    }

    private sealed class Releaser(AsyncReaderWriterLock toRelease, bool writer) : IDisposable
    {
        public void Dispose()
        {
            if (writer)
            {
                toRelease.WriterRelease();
            }
            else
            {
                toRelease.ReaderRelease();
            }
        }
    }
}