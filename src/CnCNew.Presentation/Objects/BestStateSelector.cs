namespace CnCNew.Presentation.Objects;

/// <summary>
/// Model state selection, rewritten from <c>W3DModelDrawModuleData::findBestInfo</c> and <c>SparseMatchFinder::findBestInfoSlow</c>
/// (<c>SparseMatchFinder.h</c>). The reference caches results per bit set; the result is a pure function of the spec and the bits, so no
/// cache is needed for correctness.
/// </summary>
public static class BestStateSelector
{
    /// <summary>
    /// Index into <see cref="DrawSpec.States"/> of the best match for <paramref name="conditions"/>, or -1 when the module has no states.
    /// The <see cref="DrawSpec.IgnoreConditionStates"/> bits are cleared first. States are scanned in declaration order and each state's
    /// alternatives (header line, then aliases) from last to first; an alternative wins when it matches more set bits than the best so far,
    /// or at least as many with fewer required bits that are not set. Ties keep the earlier candidate.
    /// </summary>
    public static int FindBest(DrawSpec spec, ModelConditionSet conditions)
    {
        var bits = conditions.Except(spec.IgnoreConditionStates);
        var result = -1;
        var bestYesMatch = 0;
        var bestExtraneous = 999;
        foreach (var state in spec.States)
        {
            for (var i = state.Conditions.Length - 1; i >= 0; i--)
            {
                var yes = state.Conditions[i];
                var yesMatch = bits.CountIntersection(yes);
                var extraneous = bits.CountInverseIntersection(yes);
                if (yesMatch > bestYesMatch || (yesMatch >= bestYesMatch && extraneous < bestExtraneous))
                {
                    result = state.Index;
                    bestYesMatch = yesMatch;
                    bestExtraneous = extraneous;
                }
            }
        }
        return result < 0 ? spec.DefaultStateIndex : result;
    }

    /// <summary>
    /// The transition state to play between two real states (<c>W3DModelDraw::setModelState</c>, "normal-&gt;normal state transition"): both
    /// states need a transition key and a <c>TransitionState = from to</c> with exactly these keys must exist. Returns an index into
    /// <see cref="DrawSpec.Transitions"/> or -1.
    /// </summary>
    public static int FindTransition(DrawSpec spec, int fromState, int toState)
    {
        if (fromState < 0 || toState < 0 || fromState == toState) return -1;
        var from = spec.States[fromState].TransitionKey;
        var to = spec.States[toState].TransitionKey;
        if (from is null || to is null) return -1;
        for (var i = 0; i < spec.Transitions.Length; i++)
            if (spec.Transitions[i].TransitionFrom == from && spec.Transitions[i].TransitionTo == to) return i;
        return -1;
    }
}

/// <summary>Which state a <see cref="ModelStateTracker"/> is showing: a real state index or a transition index.</summary>
public readonly record struct ModelStateRef(int Index, bool IsTransition)
{
    public static ModelStateRef None => new(-1, false);
    public bool IsNone => Index < 0;
}

/// <summary>
/// Client-side state switching with transitions and "wait for state to finish" (<c>W3DModelDraw::setModelState</c>). The renderer owns
/// animation time, so it reports completion; the tracker only decides which state plays. Optional: a renderer that ignores transitions
/// can use <see cref="BestStateSelector.FindBest"/> directly.
/// </summary>
public sealed class ModelStateTracker(DrawSpec spec)
{
    public ModelStateRef Current { get; private set; } = ModelStateRef.None;

    /// <summary>The real state that follows the current transition or waiting state (<c>m_nextState</c>); -1 when none.</summary>
    public int Pending { get; private set; } = -1;

    public DrawSpec Spec { get; } = spec;

    public ModelStateSpec? CurrentState => Current.IsNone ? null : Current.IsTransition ? Spec.Transitions[Current.Index] : Spec.States[Current.Index];

    /// <summary>Requests a real state; <paramref name="currentAnimationComplete"/> is the renderer's <c>isAnimationComplete</c> for the playing state.</summary>
    public void Request(int state, bool currentAnimationComplete)
    {
        if (state < 0) return;
        if (Current.IsNone)
        {
            Current = new ModelStateRef(state, false);
            return;
        }
        if ((!Current.IsTransition && Current.Index == state && Pending < 0) || Pending == state) return;
        var newState = Spec.States[state];
        var current = CurrentState!;
        var isSame = !Current.IsTransition && Current.Index == state;
        if (!isSame && newState.WaitForStateToFinishKey is not null && newState.WaitForStateToFinishKey == current.TransitionKey && !currentAnimationComplete)
        {
            Pending = state;
            return;
        }
        if (!isSame && !Current.IsTransition && FindTransitionFrom(Current.Index, state) is var transition and >= 0)
        {
            Current = new ModelStateRef(transition, true);
            Pending = state;
            return;
        }
        Current = new ModelStateRef(state, false);
        Pending = -1;
    }

    /// <summary>The renderer finished the current state's animation: a pending state (after a transition or a wait) now takes over.</summary>
    public void AnimationCompleted()
    {
        if (Pending < 0) return;
        Current = new ModelStateRef(Pending, false);
        Pending = -1;
    }

    private int FindTransitionFrom(int from, int to) => BestStateSelector.FindTransition(Spec, from, to);
}
