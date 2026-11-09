namespace MacroMaker;

internal enum RunLogLevel { Run, Step, Result, Warning, Error }

internal sealed record RunLogEntry(DateTimeOffset Time, RunLogLevel Level, RunStep? Step, string Message)
{
    public string StepLabel => Step is { } step ? $"{step.Tab} · {step.Title}" : string.Empty;
}

internal sealed class RunStep(string tab, Guid commandId, string title)
{
    public string Tab { get; } = tab;
    public Guid CommandId { get; } = commandId;
    public string Title { get; } = title;
    public string Status { get; set; } = string.Empty;
}

internal sealed class RunTrace
{
    private const int EntryLimit = 500;
    private readonly object _gate = new();
    private readonly Queue<RunLogEntry> _entries = new(EntryLimit);
    private readonly List<RunStep> _activeSteps = new();
    private readonly HashSet<Exception> _reportedErrors = new(ReferenceEqualityComparer.Instance);
    private string _runStatus = string.Empty;
    private int _discardedEntries;

    public void SetRunStatus(string message)
    {
        lock (_gate)
        {
            _runStatus = message;
            Append(RunLogLevel.Run, null, message);
        }
    }

    public void StartStep(RunStep step)
    {
        lock (_gate)
        {
            _activeSteps.Add(step);
            Append(RunLogLevel.Step, step, string.Empty);
        }
    }

    public void EndStep(RunStep step)
    {
        lock (_gate)
        {
            _activeSteps.Remove(step);
            step.Status = string.Empty;
        }
    }

    public void UpdateProgress(RunStep? step, string message)
    {
        if (step is null)
            return;
        lock (_gate)
            step.Status = message;
    }

    public void AddResult(RunStep? step, string message, RunLogLevel level)
    {
        lock (_gate)
        {
            if (step is not null)
                step.Status = message;
            Append(level, step, message);
        }
    }

    public void RecordError(RunStep? step, Exception error)
    {
        lock (_gate)
        {
            if (_reportedErrors.Add(error))
                Append(RunLogLevel.Error, step, error.Message);
        }
    }

    public (string Heading, string Detail) GetProgress()
    {
        lock (_gate)
        {
            if (_activeSteps.Count == 0)
                return (_runStatus, string.Empty);

            var step = _activeSteps[^1];
            return ($"{step.Tab} · {step.Title}", step.Status);
        }
    }

    public (RunLogEntry[] Entries, int Discarded) GetEntries()
    {
        lock (_gate)
            return (_entries.ToArray(), _discardedEntries);
    }

    private void Append(RunLogLevel level, RunStep? step, string message)
    {
        if (_entries.Count == EntryLimit)
        {
            _entries.Dequeue();
            _discardedEntries++;
        }
        _entries.Enqueue(new RunLogEntry(DateTimeOffset.Now, level, step,
            message.Length > 500 ? message[..500] + "…" : message));
    }
}
