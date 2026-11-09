namespace CnCNew.Launcher;

public sealed record StepStatus(StepId Id, string Title, StepState State, string Detail);

/// <summary>The list of steps the window shows, with the state of each. Pure state: it is told what the engine reports and says what is current.</summary>
public sealed class StepList
{
    private readonly List<StepStatus> _steps = [];

    public StepList(bool includeVerify)
    {
        Add(StepId.Checks, "Check your computer");
        Add(StepId.Folder, "Choose where the game goes");
        Add(StepId.Download, "Download and install the program");
        Add(StepId.MakeGame, "Make the game from your Steam copy");
        if (includeVerify) Add(StepId.Verify, "Check the simulation (optional)");
        Add(StepId.Play, "Play");
    }

    private void Add(StepId id, string title) => _steps.Add(new StepStatus(id, title, StepState.Pending, ""));

    public event Action<StepStatus>? Changed;

    public IReadOnlyList<StepStatus> Steps => _steps;

    public StepStatus this[StepId id] => _steps.First(step => step.Id == id);

    public StepStatus? Current => _steps.FirstOrDefault(step => step.State is StepState.Running or StepState.Failed);

    public void Set(StepId id, StepState state, string detail = "")
    {
        var index = _steps.FindIndex(step => step.Id == id);
        if (index < 0) return;
        _steps[index] = _steps[index] with { State = state, Detail = detail };
        Changed?.Invoke(_steps[index]);
    }

    /// <summary>Takes an engine report: the step shows the state and the detail; every earlier step still pending is done (a step is only reached after its predecessors).</summary>
    public void Apply(EngineProgress progress)
    {
        foreach (var step in _steps.TakeWhile(step => step.Id != progress.Step).ToArray())
            if (step.State == StepState.Pending) Set(step.Id, StepState.Done);
        Set(progress.Step, progress.State, progress.Detail.Length > 0 ? progress.Detail : progress.Headline);
    }

    public void MarkAllDone()
    {
        foreach (var step in _steps.ToArray())
            if (step.State is StepState.Pending or StepState.Running) Set(step.Id, StepState.Done, step.Detail);
    }

    public void Fail(StepId id, string detail) => Set(id, StepState.Failed, detail);
}

public enum UpdateKind { None, Available }

public sealed record UpdateOffer(UpdateKind Kind, SemanticVersion? Installed, SemanticVersion? Latest, ReleaseInfo? Release)
{
    public bool IsAvailable => Kind == UpdateKind.Available;

    /// <summary>"Version 0.3.0 is available (you have 0.2.1)". Newer pre-release versions are never offered unless the installed one is itself a pre-release.</summary>
    public static UpdateOffer Evaluate(SemanticVersion? installed, ReleaseInfo? latest, string skipped = "")
    {
        if (installed is null || latest is null) return new UpdateOffer(UpdateKind.None, installed, latest?.Version, latest);
        if (latest.Version <= installed) return new UpdateOffer(UpdateKind.None, installed, latest.Version, latest);
        if (latest.Version.IsPreRelease && !installed.IsPreRelease) return new UpdateOffer(UpdateKind.None, installed, latest.Version, latest);
        if (skipped.Length > 0 && SemanticVersion.TryParse(skipped) is { } skip && skip == latest.Version) return new UpdateOffer(UpdateKind.None, installed, latest.Version, latest);
        return new UpdateOffer(UpdateKind.Available, installed, latest.Version, latest);
    }

    public string Banner => IsAvailable ? $"Version {Latest} is available (you have {Installed})." : "";
}
