using System;
using System.Collections.Generic;

namespace DialogueSystem
{
    public abstract class ScenarioCondition
    {
        public abstract bool Matches(ScenarioContext context);
        public virtual void Validate(ScenarioContext context) { }
    }

    public sealed class Always : ScenarioCondition
    {
        public override bool Matches(ScenarioContext context) => true;
    }

    public sealed class FactIs<T> : ScenarioCondition
    {
        public StoryKey<T> Key { get; }
        public T Value { get; }
        public FactIs(StoryKey<T> key, T value) { Key = key; Value = value; }
        public override bool Matches(ScenarioContext context) => EqualityComparer<T>.Default.Equals(context.State.Get(Key), Value);
        public override void Validate(ScenarioContext context) => context.State.Register(Key);
    }

    public sealed class ItemIs : ScenarioCondition
    {
        public ItemId[] Items { get; }
        public ItemIs(params ItemId[] items) => Items = items ?? throw new ArgumentNullException(nameof(items));
        public override bool Matches(ScenarioContext context) => context.Item != null && Array.IndexOf(Items, context.Item.Id) >= 0;
    }

    public sealed class DuringIntroduction : ScenarioCondition
    {
        public override bool Matches(ScenarioContext context) => context.WasIntroducing;
    }

    public sealed class InDialogue : ScenarioCondition
    {
        public override bool Matches(ScenarioContext context) => context.WasInDialogue;
    }

    public sealed class Not : ScenarioCondition
    {
        public ScenarioCondition Condition { get; }
        public Not(ScenarioCondition condition) => Condition = condition ?? throw new ArgumentNullException(nameof(condition));
        public override bool Matches(ScenarioContext context) => !Condition.Matches(context);
        public override void Validate(ScenarioContext context) => Condition.Validate(context);
    }

    public sealed class All : ScenarioCondition
    {
        public ScenarioCondition[] Conditions { get; }
        public All(params ScenarioCondition[] conditions) => Conditions = conditions ?? throw new ArgumentNullException(nameof(conditions));
        public override bool Matches(ScenarioContext context)
        {
            foreach (ScenarioCondition condition in Conditions) if (!condition.Matches(context)) return false;
            return true;
        }
        public override void Validate(ScenarioContext context)
        {
            foreach (ScenarioCondition condition in Conditions) condition.Validate(context);
        }
    }

    public sealed class Any : ScenarioCondition
    {
        public ScenarioCondition[] Conditions { get; }
        public Any(params ScenarioCondition[] conditions) => Conditions = conditions ?? throw new ArgumentNullException(nameof(conditions));
        public override bool Matches(ScenarioContext context)
        {
            foreach (ScenarioCondition condition in Conditions) if (condition.Matches(context)) return true;
            return false;
        }
        public override void Validate(ScenarioContext context)
        {
            foreach (ScenarioCondition condition in Conditions) condition.Validate(context);
        }
    }
}
