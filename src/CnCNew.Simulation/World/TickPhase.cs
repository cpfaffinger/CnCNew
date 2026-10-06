using CnCNew.Simulation.Modules;

namespace CnCNew.Simulation.World;

/// <summary>
/// The sections of one logic tick, in the order <see cref="SimulationWorld.Step"/> runs them. The first fifteen are the world's own; the
/// <c>Mission…</c> and <c>Skirmish…</c> entries belong to the sessions that wrap the world step (<see cref="Mission.MissionSession.Step"/>,
/// <see cref="Skirmish.SkirmishMatchSession.Step"/>).
/// </summary>
public enum TickPhase
{
    /// <summary><c>ExecuteCommands</c>: the commands addressed to this tick.</summary>
    Commands,
    /// <summary><c>FireTimers</c>: due timers.</summary>
    Timers,
    /// <summary><c>RunUpdates</c>: the sleepy module updates; movement and its collision delivery happen in here.</summary>
    Updates,
    /// <summary><c>StepAcquisition</c>: target acquisition of idle and attacking objects.</summary>
    Acquisition,
    /// <summary><c>StepTurrets</c>.</summary>
    Turrets,
    /// <summary><c>StepFiringTrackers</c>.</summary>
    FiringTrackers,
    /// <summary>First <c>DeliverEvents</c>: everything raised so far.</summary>
    Events,
    /// <summary><c>ProcessSales</c>.</summary>
    Sales,
    /// <summary><c>ProcessDestroys</c>.</summary>
    Destroys,
    /// <summary><c>ProcessDelayedDamage</c>.</summary>
    DelayedDamage,
    /// <summary><c>ProcessSpawns</c>.</summary>
    Spawns,
    /// <summary>Second <c>DeliverEvents</c>: what destroy, delayed damage and spawn raised.</summary>
    SpawnEvents,
    /// <summary>The reveal part of <c>RefreshVision</c>; empty on ticks between two refreshes and in a world without terrain.</summary>
    Vision,
    /// <summary>The hash over the vision cells that ends <c>RefreshVision</c>; empty whenever <see cref="Vision"/> is.</summary>
    VisionHash,
    /// <summary>The tick counter advances and the incremental state hash is computed.</summary>
    Hash,
    /// <summary><c>ScriptEngine.Update</c>, before the world step of a mission.</summary>
    MissionScripts,
    /// <summary>The session's own tail after the world step: shot counting and <c>ScriptEngine.Observe</c>.</summary>
    MissionEvents,
    /// <summary>The science stand-in and every <c>SkirmishAi.Update</c>, before the world step of a skirmish match (<see cref="Skirmish.SkirmishMatchSession.Step"/>).</summary>
    SkirmishAi,
    /// <summary>The periodic victory check after the world step of a skirmish match.</summary>
    SkirmishVictory,
}

/// <summary>
/// A measuring hook for tools. The world calls it at the boundaries of its tick phases and around every module update; it holds no clock of
/// its own. The observer is not part of the state: it is neither hashed nor saved, and it must not change the world. Without an observer the
/// step runs exactly as before.
/// </summary>
public interface ITickPhaseObserver
{
    void PhaseStarted(TickPhase phase);
    void PhaseEnded(TickPhase phase);

    /// <summary>Called right before and after one module's <c>Update</c> inside <see cref="TickPhase.Updates"/>.</summary>
    void UpdateStarted(SimModule module);
    void UpdateEnded(SimModule module);
}

/// <summary>
/// Counters of the incremental state hash (<see cref="SimulationWorld.HashWorkDone"/>): deterministic, so two builds can be compared without a clock. <c>Entities</c>: hashes
/// recomputed; <c>Words</c>: words folded for them; <c>Unchanged</c>: those that came out with the hash they had; <c>ModuleEntities</c>: of the entities, those of which only
/// single modules were described again (<see cref="Modules.IWorldServices.OnlyOwnStateChanged"/>).
/// </summary>
public readonly record struct HashWork(long Entities, long Words, long Unchanged, long ModuleEntities);

/// <summary>
/// The words an entity description writes between the states of its modules: after the state of module <c>i</c> up to the state of module <c>i + 1</c> (the end of one
/// module, the frame of the next), and after the last state up to the end. They are the same for every entity of a template; the hash checks that for each entity it keeps
/// words for instead of assuming it. Shared by the entities of one template in one world.
/// </summary>
internal sealed class TemplateGlue(int modules)
{
    public ulong[] Words = [];
    public readonly int[] Start = new int[modules];
    public readonly int[] Length = new int[modules];
}

/// <summary>
/// What the incremental hash keeps for an entity of which single modules change on their own (<see cref="Modules.IWorldServices.OnlyOwnStateChanged"/>): the state words of
/// every module as last described, and the state the fold had reached before the state of one module (the checkpoint). With the template's <see cref="TemplateGlue"/> that
/// is the entity's whole description from the checkpoint on. Bookkeeping of the hash, derived from the entity; never state, never saved.
/// </summary>
internal sealed class EntityHashCache(int modules)
{
    /// <summary>Scratch for the writer: word count before and after every module's state.</summary>
    public readonly int[] Marks = new int[2 * modules];
    public ulong[] States = [];
    public readonly int[] StateStart = new int[modules];
    public readonly int[] StateLength = new int[modules];
    public TemplateGlue? Glue;

    /// <summary>The kept states are those of the entity's last description.</summary>
    public bool Valid;

    /// <summary>The module before whose state <see cref="CheckpointHash"/> was taken; negative when there is none. Valid while no word before that state changes.</summary>
    public int CheckpointModule = -1;
    public ulong CheckpointHash;

    /// <summary>The module the next whole description should take a checkpoint for; negative for none.</summary>
    public int WantedCheckpoint = -1;

    /// <summary>A checkpoint was taken / was used since; and how many module-only ticks to answer with a plain whole description (see <c>SimulationWorld.TryRehashModules</c>).</summary>
    public bool Taken;
    public bool Used;
    public int Backoff;
    public int Skip;

    public void Invalidate()
    {
        Valid = false;
        CheckpointModule = -1;
    }

    /// <summary>
    /// Takes over a whole description (<paramref name="words"/>, which began at <paramref name="start"/> in the writer the marks were taken from). The words between the
    /// module states must be the template's glue; an entity for which they are not keeps nothing and is always described as a whole.
    /// </summary>
    public void Keep(ReadOnlySpan<ulong> words, int start, int marks, ref TemplateGlue? shared)
    {
        Invalidate();
        var wanted = WantedCheckpoint;
        WantedCheckpoint = -1;
        var modules = StateStart.Length;
        if (marks != Marks.Length || modules == 0) return;
        var total = 0;
        for (var index = 0; index < modules; index++) total += Marks[2 * index + 1] - Marks[2 * index];
        if (States.Length < total) States = new ulong[total + 8];
        var position = 0;
        for (var index = 0; index < modules; index++)
        {
            var from = Marks[2 * index] - start;
            var length = Marks[2 * index + 1] - Marks[2 * index];
            words.Slice(from, length).CopyTo(States.AsSpan(position));
            StateStart[index] = position;
            StateLength[index] = length;
            position += length;
        }
        if (shared is null)
        {
            // The first entity of the template writes the glue down.
            var glue = new TemplateGlue(modules);
            var size = 0;
            for (var index = 0; index < modules; index++) size += GlueEnd(index, words.Length, start) - (Marks[2 * index + 1] - start);
            glue.Words = new ulong[size];
            var at = 0;
            for (var index = 0; index < modules; index++)
            {
                var from = Marks[2 * index + 1] - start;
                var length = GlueEnd(index, words.Length, start) - from;
                words.Slice(from, length).CopyTo(glue.Words.AsSpan(at));
                glue.Start[index] = at;
                glue.Length[index] = length;
                at += length;
            }
            shared = glue;
        }
        else
        {
            if (shared.Start.Length != modules) return;
            for (var index = 0; index < modules; index++)
            {
                var from = Marks[2 * index + 1] - start;
                var length = GlueEnd(index, words.Length, start) - from;
                if (length != shared.Length[index] || !words.Slice(from, length).SequenceEqual(shared.Words.AsSpan(shared.Start[index], length))) return;
            }
        }
        Glue = shared;
        Valid = true;
        if (wanted < 0 || wanted >= modules) return;
        // Asked for by the module path: the state of the fold before the wanted module's state, from the words of this very description.
        CheckpointHash = State.StateHash.Fold(State.StateHash.Seed, words[..(Marks[2 * wanted] - start)]);
        CheckpointModule = wanted;
        Taken = true;
    }

    /// <summary>Where the glue after the state of module <paramref name="index"/> ends: at the next module's state, or at the end of the description.</summary>
    private int GlueEnd(int index, int length, int start) => index + 1 < StateStart.Length ? Marks[2 * index + 2] - start : length;

    /// <summary>Whether a fresh whole description (with its marks in <see cref="Marks"/>) is what is kept here: every module state and every stretch of glue.</summary>
    public bool Matches(ReadOnlySpan<ulong> words, int marks)
    {
        if (!Valid || Glue is not { } glue || marks != Marks.Length) return false;
        for (var index = 0; index < StateStart.Length; index++)
        {
            var from = Marks[2 * index];
            var length = Marks[2 * index + 1] - from;
            if (length != StateLength[index] || !words.Slice(from, length).SequenceEqual(States.AsSpan(StateStart[index], length))) return false;
            var glueFrom = Marks[2 * index + 1];
            var glueLength = GlueEnd(index, words.Length, 0) - glueFrom;
            if (glueLength != glue.Length[index] || !words.Slice(glueFrom, glueLength).SequenceEqual(glue.Words.AsSpan(glue.Start[index], glueLength))) return false;
        }
        return true;
    }
}
