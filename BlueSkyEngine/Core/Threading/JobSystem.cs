using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using BlueSky.Core.Diagnostics;

namespace BlueSky.Core.Threading;

/// <summary>
/// High-performance lock-free job system with work-stealing.
/// Uses ManualResetEventSlim for microsecond-latency wake-ups (no Thread.Sleep).
/// </summary>
public sealed class JobSystem : IDisposable
{
    private readonly int _workerCount;
    private readonly Thread[] _workers;
    private readonly ConcurrentQueue<JobData>[] _queues; // Per-worker queues
    private readonly ConcurrentQueue<JobData> _globalQueue; // Overflow queue
    private readonly ManualResetEventSlim _workAvailable;
    private readonly long[] _jobsCompleted;
    private readonly long[] _totalTicks;
    private volatile bool _isRunning;
    private volatile bool _disposed;
    private int _nextWorker; // Round-robin dispatch

    public int WorkerCount => _workerCount;
    public bool IsRunning => _isRunning;
    public int QueuedJobs
    {
        get
        {
            int count = _globalQueue.Count;
            for (int i = 0; i < _workerCount; i++) count += _queues[i].Count;
            return count;
        }
    }

    public JobSystem(int? workerCount = null)
    {
        _workerCount = workerCount ?? System.Math.Max(1, Environment.ProcessorCount - 1);
        _workers = new Thread[_workerCount];
        _queues = new ConcurrentQueue<JobData>[_workerCount];
        _globalQueue = new ConcurrentQueue<JobData>();
        _workAvailable = new ManualResetEventSlim(false);
        _jobsCompleted = new long[_workerCount];
        _totalTicks = new long[_workerCount];

        for (int i = 0; i < _workerCount; i++)
            _queues[i] = new ConcurrentQueue<JobData>();

        ErrorHandler.LogInfo($"JobSystem: {_workerCount} workers (CPU: {Environment.ProcessorCount} cores)", "JobSystem");
    }

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;

        for (int i = 0; i < _workerCount; i++)
        {
            int id = i;
            _workers[i] = new Thread(() => WorkerLoop(id))
            {
                Name = $"BlueSky.Worker-{i}",
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };
            _workers[i].Start();
        }
    }

    /// <summary>Schedule a job. Returns a handle for waiting/chaining.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public JobHandle Schedule(Action action, string? name = null)
    {
        if (!_isRunning) Start();

        var completion = new JobCompletion();
        var job = new JobData { Action = action, Completion = completion, Name = name };

        // Round-robin to per-worker queues for locality
        int target = Interlocked.Increment(ref _nextWorker) % _workerCount;
        _queues[target].Enqueue(job);
        _workAvailable.Set();

        return new JobHandle(completion);
    }

    /// <summary>Schedule multiple jobs and return handles.</summary>
    public JobHandle[] ScheduleBatch(ReadOnlySpan<Action> actions, string? batchName = null)
    {
        var handles = new JobHandle[actions.Length];
        for (int i = 0; i < actions.Length; i++)
            handles[i] = Schedule(actions[i], batchName != null ? $"{batchName}[{i}]" : null);
        return handles;
    }

    /// <summary>
    /// Parallel for with batching — distributes work in chunks, not 1 job per iteration.
    /// Much more efficient than the old per-iteration approach.
    /// </summary>
    public void ParallelFor(int start, int end, Action<int> action, int batchSize = 0, string? name = null)
    {
        int total = end - start;
        if (total <= 0) return;

        if (batchSize <= 0)
            batchSize = System.Math.Max(1, total / (_workerCount * 4)); // Auto-tune

        int batchCount = (total + batchSize - 1) / batchSize;
        var completions = new JobCompletion[batchCount];

        for (int b = 0; b < batchCount; b++)
        {
            int batchStart = start + b * batchSize;
            int batchEnd = System.Math.Min(batchStart + batchSize, end);
            var completion = new JobCompletion();
            completions[b] = completion;

            var job = new JobData
            {
                Action = () =>
                {
                    for (int i = batchStart; i < batchEnd; i++)
                        action(i);
                },
                Completion = completion,
                Name = name != null ? $"{name}[{batchStart}..{batchEnd}]" : null
            };

            int target = Interlocked.Increment(ref _nextWorker) % _workerCount;
            _queues[target].Enqueue(job);
        }

        _workAvailable.Set();

        // Wait for all batches
        for (int i = 0; i < batchCount; i++)
            completions[i].Wait();
    }

    /// <summary>Wait for all queued jobs to complete.</summary>
    public void WaitForAll()
    {
        // Spin with yielding instead of Thread.Sleep
        var sw = new SpinWait();
        while (QueuedJobs > 0) sw.SpinOnce();
    }

    public void Stop()
    {
        if (!_isRunning) return;
        _isRunning = false;
        _workAvailable.Set(); // Wake sleeping workers so they exit

        for (int i = 0; i < _workerCount; i++)
            _workers[i]?.Join(1000);
    }

    public JobSystemStats GetStats()
    {
        long totalJobs = 0, totalTime = 0;
        for (int i = 0; i < _workerCount; i++)
        {
            totalJobs += _jobsCompleted[i];
            totalTime += _totalTicks[i];
        }
        double avgMs = totalJobs > 0 ? (double)totalTime / totalJobs / Stopwatch.Frequency * 1000.0 : 0;
        return new JobSystemStats
        {
            WorkerCount = _workerCount,
            QueuedJobs = QueuedJobs,
            TotalJobsCompleted = totalJobs,
            AverageExecutionTimeMs = avgMs
        };
    }

    private void WorkerLoop(int workerId)
    {
        while (_isRunning)
        {
            // 1. Try own queue first (locality)
            if (_queues[workerId].TryDequeue(out var job))
            {
                ExecuteJob(job, workerId);
                continue;
            }

            // 2. Try global queue
            if (_globalQueue.TryDequeue(out job))
            {
                ExecuteJob(job, workerId);
                continue;
            }

            // 3. Work-stealing: try other workers' queues
            bool stolen = false;
            for (int i = 1; i < _workerCount; i++)
            {
                int victim = (workerId + i) % _workerCount;
                if (_queues[victim].TryDequeue(out job))
                {
                    ExecuteJob(job, workerId);
                    stolen = true;
                    break;
                }
            }
            if (stolen) continue;

            // 4. No work found — sleep until signaled (microsecond wake-up, NOT Thread.Sleep(1))
            _workAvailable.Reset();
            _workAvailable.Wait(10); // 10ms max sleep, wakes instantly when Set()
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteJob(JobData job, int workerId)
    {
        long startTick = Stopwatch.GetTimestamp();
        try
        {
            job.Action();
        }
        catch (Exception ex)
        {
            job.Completion.SetException(ex);
            ErrorHandler.LogError($"Job '{job.Name}' failed on worker {workerId}", ex, "JobSystem");
            return;
        }
        long elapsed = Stopwatch.GetTimestamp() - startTick;
        _totalTicks[workerId] += elapsed;
        _jobsCompleted[workerId]++;
        job.Completion.SetCompleted();
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _workAvailable.Dispose();
        _disposed = true;
    }
}

// ── Internal types ─────────────────────────────────────────────────────

internal struct JobData
{
    public Action Action;
    public JobCompletion Completion;
    public string? Name;
}

/// <summary>Lock-free completion signal using ManualResetEventSlim.</summary>
public sealed class JobCompletion
{
    private volatile int _state; // 0=pending, 1=completed, 2=error
    private ManualResetEventSlim? _event;
    private Exception? _exception;

    public bool IsCompleted { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _state != 0; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void SetCompleted()
    {
        _state = 1;
        _event?.Set();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void SetException(Exception ex)
    {
        _exception = ex;
        _state = 2;
        _event?.Set();
    }

    public void Wait()
    {
        if (_state != 0) return;
        // Lazy-init the event only if we actually need to wait
        if (_event == null)
            Interlocked.CompareExchange(ref _event, new ManualResetEventSlim(false), null);
        if (_state != 0) { _event?.Set(); return; }
        _event.Wait();
        if (_exception != null) throw new AggregateException(_exception);
    }
}

/// <summary>Handle returned from Schedule() for waiting on job completion.</summary>
public readonly struct JobHandle
{
    private readonly JobCompletion _completion;
    internal JobHandle(JobCompletion completion) => _completion = completion;
    public bool IsCompleted => _completion.IsCompleted;
    public void Wait() => _completion.Wait();

    /// <summary>Schedule a continuation that runs after this job completes.</summary>
    public JobHandle ContinueWith(JobSystem system, Action action, string? name = null)
    {
        var completion = _completion;
        return system.Schedule(() => { completion.Wait(); action(); }, name);
    }
}

public class JobSystemStats
{
    public int WorkerCount { get; set; }
    public int QueuedJobs { get; set; }
    public long TotalJobsCompleted { get; set; }
    public double AverageExecutionTimeMs { get; set; }
}
