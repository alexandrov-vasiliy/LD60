using System;

namespace DialogueSystem
{
    /// <summary>
    /// Событие, по которому сценарий выбирает реакцию: обращение игрока или попадание предмета в NPC.
    /// </summary>
    public enum ScenarioEvent { Interact, ItemHit }
    /// <summary>
    /// Судьба попавшего предмета после принятия правила: оставить доступным игроку или убрать из мира.
    /// </summary>
    public enum ItemDisposition { Keep, Consume }
    /// <summary>
    /// Способ показа реплик: окно диалога, текст над NPC или выбор по состоянию разговора на момент события.
    /// </summary>
    public enum SpeechMode { Dialogue, World, Current }

    /// <summary>
    /// Данные события для условий и шагов: NPC, предмет и общее состояние истории. Состояние вступления и открытого разговора фиксируется при создании контекста, а сюжетные факты читаются из текущего состояния.
    /// </summary>
    public sealed class ScenarioContext
    {
        public NpcDialog Npc { get; }
        public StoryState State => Npc.Sequencer.Story;
        public Item Item { get; }
        public bool WasIntroducing { get; }
        public bool WasInDialogue { get; }
        public ScenarioContext(NpcDialog npc, Item item = null)
        {
            Npc = npc;
            Item = item;
            WasIntroducing = npc.IsIntroducing;
            WasInDialogue = npc.Presenter.Owner == npc;
        }

        public NpcDialog Actor(string id) => string.IsNullOrEmpty(id) ? Npc : Npc.Sequencer.GetNpc(id);
    }

    /// <summary>
    /// Реакция на событие: условие выбора, выполняемые шаги и способ обращения с предметом. Изменения OnReceipt применяются вместе до начала шагов; среди правил одного события выбирается первое подходящее.
    /// </summary>
    public sealed class ScenarioRule
    {
        public ScenarioEvent Event { get; }
        public ScenarioCondition Condition { get; }
        public ScenarioStep Steps { get; }
        public ItemDisposition ItemDisposition { get; }
        public FactChange[] OnReceipt { get; }

        public ScenarioRule(ScenarioEvent trigger, ScenarioCondition condition, ScenarioStep steps,
            ItemDisposition itemDisposition = ItemDisposition.Keep, params FactChange[] onReceipt)
        {
            Event = trigger;
            Condition = condition ?? throw new ArgumentNullException(nameof(condition));
            Steps = steps ?? throw new ArgumentNullException(nameof(steps));
            ItemDisposition = itemDisposition;
            OnReceipt = onReceipt ?? throw new ArgumentNullException(nameof(onReceipt));
            if (trigger != ScenarioEvent.ItemHit && (itemDisposition == ItemDisposition.Consume || OnReceipt.Length > 0))
                throw new ArgumentException("Receipt changes belong to an ItemHit rule.");
        }
    }

    /// <summary>
    /// Условие и текст итога персонажа в конце попытки. Выбирается первое подходящее правило; пустой текст скрывает персонажа на экране итогов.
    /// </summary>
    public sealed class EndingRule
    {
        public ScenarioCondition Condition { get; }
        public string Text { get; }
        public EndingRule(ScenarioCondition condition, string text) { Condition = condition; Text = text; }
    }

    /// <summary>
    /// Описание истории NPC: уникальный идентификатор, упорядоченные реакции и варианты концовки. Для события и для итога выбирается первое подходящее правило; последняя концовка должна покрывать случай без взаимодействий.
    /// </summary>
    public abstract class NpcScenario
    {
        public abstract string Id { get; }
        public abstract ScenarioRule[] Rules { get; }
        public abstract EndingRule[] Endings { get; }

        public ScenarioRule Select(ScenarioEvent trigger, ScenarioContext context)
        {
            foreach (ScenarioRule rule in Rules)
            {
                if (rule.Event != trigger) continue;
                rule.Condition.Validate(context);
                if (rule.Condition.Matches(context)) return rule;
            }
            return null;
        }

        public string GetEnding(ScenarioContext context)
        {
            foreach (EndingRule ending in Endings)
            {
                ending.Condition.Validate(context);
                if (ending.Condition.Matches(context)) return ending.Text;
            }
            throw new InvalidOperationException($"Scenario '{Id}' needs a fallback ending.");
        }
    }
}
