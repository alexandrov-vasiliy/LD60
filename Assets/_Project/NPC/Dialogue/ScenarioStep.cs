using System;

namespace DialogueSystem
{
    public abstract class ScenarioStep
    {
        public virtual void Validate(ScenarioContext context) { }
        public abstract void Execute(ScenarioRunner runner);
    }

    public sealed class Sequence : ScenarioStep
    {
        public ScenarioStep[] Steps { get; }
        public Sequence(params ScenarioStep[] steps) => Steps = steps ?? throw new ArgumentNullException(nameof(steps));
        public override void Validate(ScenarioContext context)
        {
            foreach (ScenarioStep step in Steps)
            {
                if (step == null) throw new InvalidOperationException("A sequence contains a missing step.");
                step.Validate(context);
            }
        }
        public override void Execute(ScenarioRunner runner)
        {
            for (int i = Steps.Length - 1; i >= 0; i--) runner.Push(Steps[i]);
        }
    }

    public sealed class Branch : ScenarioStep
    {
        public ScenarioCondition Condition { get; }
        public ScenarioStep Yes { get; }
        public ScenarioStep No { get; }
        public Branch(ScenarioCondition condition, ScenarioStep yes, ScenarioStep no)
        {
            Condition = condition ?? throw new ArgumentNullException(nameof(condition));
            Yes = yes ?? throw new ArgumentNullException(nameof(yes));
            No = no ?? throw new ArgumentNullException(nameof(no));
        }
        public override void Validate(ScenarioContext context)
        {
            Condition.Validate(context);
            Yes.Validate(context);
            No.Validate(context);
        }
        public override void Execute(ScenarioRunner runner) => runner.Push(Condition.Matches(runner.Context) ? Yes : No);
    }

    public sealed class Say : ScenarioStep
    {
        public string ActorId { get; }
        public string[] Lines { get; }
        public SpeechMode Mode { get; }
        public string CameraId { get; }
        public string StartFeedback { get; set; }
        public Say(SpeechMode mode, params string[] lines) : this(null, mode, null, lines) { }
        public Say(string actorId, SpeechMode mode, string cameraId, params string[] lines)
        {
            ActorId = actorId;
            Mode = mode;
            CameraId = cameraId;
            Lines = lines ?? throw new ArgumentNullException(nameof(lines));
        }
        public override void Validate(ScenarioContext context)
        {
            if (Lines.Length == 0) return;
            if (!string.IsNullOrEmpty(StartFeedback)) context.Npc.GetFeedback(StartFeedback);
            foreach (string line in Lines)
                if (string.IsNullOrWhiteSpace(line)) throw new InvalidOperationException("Say contains an empty line.");
            NpcDialog actor = context.Actor(ActorId);
            bool world = Mode == SpeechMode.World || (Mode == SpeechMode.Current && !context.WasInDialogue);
            actor.ValidateSpeech(world, CameraId);
            if (!world) context.Npc.ValidateSpeech(false, null);
        }
        public override void Execute(ScenarioRunner runner)
        {
            if (Lines.Length == 0) return;
            ScenarioContext context = runner.Context;
            NpcDialog actor = context.Actor(ActorId);
            bool world = Mode == SpeechMode.World || (Mode == SpeechMode.Current && !context.WasInDialogue);
            Action completed = runner.Await();
            if (world)
            {
                runner.TrackWorldSpeaker(actor);
                actor.ShowWorld(Lines, completed);
            }
            else if (!context.Npc.Presenter.TryShow(context.Npc, actor, Lines, actor.GetCamera(CameraId), completed, runner.ItemsLocked))
                throw new InvalidOperationException("Cannot open this dialogue session.");
            if (!string.IsNullOrEmpty(StartFeedback))
            {
                var feedback = context.Npc.GetFeedback(StartFeedback);
                runner.OnEnd(() =>
                {
                    if (feedback == null) return;
                    feedback.StopFeedbacks();
                    feedback.RestoreInitialValues();
                });
                feedback.PlayFeedbacks();
            }
        }
    }

    public abstract class FactChange : ScenarioStep
    {
        public abstract void Apply(StoryState state);
        public sealed override void Execute(ScenarioRunner runner) => Apply(runner.Context.State);
    }

    public sealed class SetFact<T> : FactChange
    {
        public StoryKey<T> Key { get; }
        public T Value { get; }
        public SetFact(StoryKey<T> key, T value) { Key = key; Value = value; }
        public override void Validate(ScenarioContext context) => context.State.Register(Key);
        public override void Apply(StoryState state) => state.Set(Key, Value);
    }

    public sealed class SetIntroduction : ScenarioStep
    {
        public bool Value { get; }
        public SetIntroduction(bool value) => Value = value;
        public override void Execute(ScenarioRunner runner) => runner.Context.Npc.SetIntroduction(Value);
    }

    public sealed class PlayFeedback : ScenarioStep
    {
        public string Id { get; }
        public PlayFeedback(string id) => Id = id;
        public override void Validate(ScenarioContext context) => context.Npc.GetFeedback(Id);
        public override void Execute(ScenarioRunner runner) => runner.Context.Npc.GetFeedback(Id).PlayFeedbacks();
    }

    public sealed class ProtectedSequence : ScenarioStep
    {
        public Sequence Content { get; }
        public ProtectedSequence(params ScenarioStep[] steps) => Content = new Sequence(steps);
        public override void Validate(ScenarioContext context) => Content.Validate(context);
        public override void Execute(ScenarioRunner runner)
        {
            runner.Push(new SetItemLock(runner.ItemsLocked));
            runner.Push(Content);
            runner.SetItemLock(true);
        }
    }

    internal sealed class SetItemLock : ScenarioStep
    {
        private readonly bool value;
        public SetItemLock(bool value) => this.value = value;
        public override void Execute(ScenarioRunner runner) => runner.SetItemLock(value);
    }

    public sealed class EndDialogue : ScenarioStep
    {
        public override void Execute(ScenarioRunner runner) => runner.End();
    }
}
