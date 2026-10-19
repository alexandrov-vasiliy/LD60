namespace DialogueSystem
{
    /// <summary>
    /// История Прохора: два отдельных обращения ведут к просьбе, отказы от предметов и направление за медицинской помощью к Агафьевне. Итог определяется общим фактом лечения Лизы: таблетки дают выздоровление, остальные варианты — смерть к утру.
    /// </summary>
    public sealed class ProkhorScenario : NpcScenario
    {
        public override string Id => "prokhor";

        private static Sequence Request() => new Sequence(
            new SetIntroduction(false),
            new PlayFeedback("NeedItem"),
            new Say(SpeechMode.Dialogue,
                "npc.prokhor.01",
                "npc.prokhor.02",
                "npc.prokhor.03",
                "npc.prokhor.04"));

        public override ScenarioRule[] Rules { get; } = new[]
        {
            new ScenarioRule(ScenarioEvent.Interact, new FactIs<int>(StoryFacts.ProkhorVisits, 0),
                new Sequence(
                    new SetFact<int>(StoryFacts.ProkhorVisits, 1),
                    new SetIntroduction(true),
                    new PlayFeedback("BeforeItem"),
                    new Say(SpeechMode.Dialogue, "npc.prokhor.05", "npc.prokhor.06", "npc.prokhor.07"),
                    new SetIntroduction(false))),
            new ScenarioRule(ScenarioEvent.Interact, new FactIs<int>(StoryFacts.ProkhorVisits, 1),
                new Sequence(
                    new SetFact<int>(StoryFacts.ProkhorVisits, 2),
                    new SetIntroduction(true),
                    new Say(SpeechMode.Dialogue, "npc.prokhor.08", "npc.prokhor.09", "npc.prokhor.10", "npc.prokhor.11"),
                    Request())),
            new ScenarioRule(ScenarioEvent.Interact, new Always(), Request()),

            new ScenarioRule(ScenarioEvent.ItemHit, new FactIs<int>(StoryFacts.ProkhorVisits, 0),
                new Sequence(new PlayFeedback("BeforeMeeting"), new Say(SpeechMode.Dialogue, "npc.prokhor.12"))),
            new ScenarioRule(ScenarioEvent.ItemHit, new ItemIs(ItemId.Stethoscope, ItemId.Pills, ItemId.Alcohol),
                new Sequence(new PlayFeedback("ItemReceived"), new Say(SpeechMode.Dialogue, "npc.prokhor.13", "npc.prokhor.14"))),
            new ScenarioRule(ScenarioEvent.ItemHit, new Always(),
                new Sequence(new PlayFeedback("WrongItem"), new Say(SpeechMode.Dialogue, "npc.prokhor.15")))
        };

        public override EndingRule[] Endings { get; } = new[]
        {
            new EndingRule(new FactIs<LizaHelp>(StoryFacts.LizaTreatment, LizaHelp.Pills),
                "npc.prokhor.16"),
            new EndingRule(new Always(), "npc.prokhor.17")
        };
    }
}
