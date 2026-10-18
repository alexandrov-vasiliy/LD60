using System;
using System.Collections.Generic;

namespace DialogueSystem
{
    /// <summary>
    /// Базовое условие выбора реакции, ветки или концовки. Проверяет настройки и читает контекст, не изменяя сюжетное состояние.
    /// </summary>
    public abstract class ScenarioCondition
    {
        public abstract bool Matches(ScenarioContext context);
        public virtual void Validate(ScenarioContext context) { }
    }

    /// <summary>
    /// Условие, которое подходит всегда; используется последним правилом как запасная реакция или концовка.
    /// </summary>
    public sealed class Always : ScenarioCondition
    {
        public override bool Matches(ScenarioContext context) => true;
    }

    /// <summary>
    /// Проверяет равенство общего сюжетного факта заданному значению, например факт знакомства или выбранный вид помощи.
    /// </summary>
    public sealed class FactIs<T> : ScenarioCondition
    {
        public StoryKey<T> Key { get; }
        public T Value { get; }
        public FactIs(StoryKey<T> key, T value) { Key = key; Value = value; }
        public override bool Matches(ScenarioContext context) => EqualityComparer<T>.Default.Equals(context.State.Get(Key), Value);
        public override void Validate(ScenarioContext context) => context.State.Register(Key);
    }

    /// <summary>
    /// Проверяет, что попавший в NPC предмет имеет один из перечисленных типов; без предмета условие не выполняется.
    /// </summary>
    public sealed class ItemIs : ScenarioCondition
    {
        public ItemId[] Items { get; }
        public ItemIs(params ItemId[] items) => Items = items ?? throw new ArgumentNullException(nameof(items));
        public override bool Matches(ScenarioContext context) => context.Item != null && Array.IndexOf(Items, context.Item.Id) >= 0;
    }

    /// <summary>
    /// Проверяет, произошло ли исходное событие во время вступления NPC. Использует снимок контекста, даже если вступление уже завершено последующими шагами.
    /// </summary>
    public sealed class DuringIntroduction : ScenarioCondition
    {
        public override bool Matches(ScenarioContext context) => context.WasIntroducing;
    }

    /// <summary>
    /// Проверяет, был ли на момент исходного события открыт разговор именно с владельцем сценария.
    /// </summary>
    public sealed class InDialogue : ScenarioCondition
    {
        public override bool Matches(ScenarioContext context) => context.WasInDialogue;
    }

    /// <summary>
    /// Инвертирует вложенное условие, например позволяет выбрать реакцию до знакомства с NPC.
    /// </summary>
    public sealed class Not : ScenarioCondition
    {
        public ScenarioCondition Condition { get; }
        public Not(ScenarioCondition condition) => Condition = condition ?? throw new ArgumentNullException(nameof(condition));
        public override bool Matches(ScenarioContext context) => !Condition.Matches(context);
        public override void Validate(ScenarioContext context) => Condition.Validate(context);
    }

    /// <summary>
    /// Требует выполнения всех вложенных условий; пустой набор считается выполненным.
    /// </summary>
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

    /// <summary>
    /// Требует выполнения хотя бы одного вложенного условия; пустой набор не выполняется.
    /// </summary>
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
