using System;
using System.Collections.Generic;
using UnityEngine;

namespace DialogueSystem
{
    public sealed class ScenarioRunner
    {
        private readonly NpcDialog npc;
        private readonly Stack<ScenarioStep> pending = new Stack<ScenarioStep>();
        private readonly HashSet<NpcDialog> worldSpeakers = new HashSet<NpcDialog>();
        private readonly List<Action> cleanup = new List<Action>();
        private int generation;
        private bool waiting, pumping, running;
        public ScenarioContext Context { get; private set; }
        public bool ItemsLocked { get; private set; }

        public ScenarioRunner(NpcDialog npc) => this.npc = npc;

        public void Start(ScenarioRule rule, ScenarioContext context)
        {
            if (rule.ItemDisposition == ItemDisposition.Consume &&
                (context.Item == null || context.Item.Owner != null || !context.Item.WasDropped || !context.Item.gameObject.activeInHierarchy))
                throw new InvalidOperationException("Only a dropped, unowned item can be consumed.");
            rule.Steps.Validate(context);
            foreach (FactChange change in rule.OnReceipt) change.Validate(context);
            context.State.ApplyReceipt(rule.OnReceipt);
            Cancel(false);
            Context = context;
            running = true;
            if (rule.ItemDisposition == ItemDisposition.Consume)
            {
                context.Item.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(context.Item.gameObject);
            }
            Push(rule.Steps);
            Pump();
        }

        public void Push(ScenarioStep step) => pending.Push(step ?? throw new ArgumentNullException(nameof(step)));

        public Action Await()
        {
            waiting = true;
            int token = generation;
            bool resumed = false;
            return () =>
            {
                if (resumed || token != generation || !running) return;
                resumed = true;
                waiting = false;
                Pump();
            };
        }

        public void TrackWorldSpeaker(NpcDialog speaker) => worldSpeakers.Add(speaker);
        public void OnEnd(Action action) => cleanup.Add(action ?? throw new ArgumentNullException(nameof(action)));

        public void SetItemLock(bool value)
        {
            ItemsLocked = value;
            if (npc.Presenter != null) npc.Presenter.SetItemLock(npc, value);
        }

        private void Pump()
        {
            if (pumping) return;
            pumping = true;
            try
            {
                while (running && !waiting && pending.Count > 0) pending.Pop().Execute(this);
                if (running && !waiting && pending.Count == 0) End();
            }
            catch (Exception exception)
            {
                Debug.LogError($"Scenario '{npc.CharacterId}': {exception}", npc);
                End();
            }
            finally { pumping = false; }
        }

        public void End() => Cancel(true);

        public void Cancel(bool closeDialogue)
        {
            generation++;
            running = waiting = false;
            pending.Clear();
            Action[] actions = cleanup.ToArray();
            cleanup.Clear();
            for (int i = actions.Length - 1; i >= 0; i--)
            {
                try { actions[i](); }
                catch (Exception exception) { Debug.LogException(exception, npc); }
            }
            foreach (NpcDialog speaker in worldSpeakers)
                if (speaker != null) speaker.HideWorld();
            worldSpeakers.Clear();
            npc.SetIntroduction(false);
            SetItemLock(false);
            if (npc.Presenter != null)
            {
                if (closeDialogue) npc.Presenter.Close(npc);
                else npc.Presenter.ClearContinuation(npc);
            }
        }
    }
}
