namespace DialogueSystem
{
    /// <summary>
    /// История Анны: знакомство, просьба о помощи Лизе и перенаправление медицинских предметов Агафьевне без принятия. Последующие реплики отражают текущее лечение, а концовка зависит от того, получила ли Агафьевна таблетки.
    /// </summary>
    public sealed class AnnaScenario : NpcScenario
    {
        public override string Id => "anna";

        private static Sequence Request() => new Sequence(
            new SetIntroduction(false),
            new PlayFeedback("NeedItem"),
            new Say(SpeechMode.Dialogue, "npc.anna.01"));

        private static Sequence Reply(string feedback, SpeechMode mode, params string[] lines) => new Sequence(
            new PlayFeedback(feedback), new Say(mode, lines));

        public override ScenarioRule[] Rules { get; } = new[]
        {
            new ScenarioRule(ScenarioEvent.Interact, new Not(new FactIs<bool>(StoryFacts.AnnaMet, true)),
                new Sequence(
                    new SetFact<bool>(StoryFacts.AnnaMet, true),
                    new SetIntroduction(true),
                    new PlayFeedback("BeforeItem"),
                    new Say(SpeechMode.Dialogue,
                        "npc.anna.02",
                        "npc.anna.03",
                        "npc.anna.04",
                        "npc.anna.05"),
                    Request())),

            new ScenarioRule(ScenarioEvent.Interact, new FactIs<LizaHelp>(StoryFacts.LizaTreatment, LizaHelp.Pills),
                Reply("AfterItem", SpeechMode.Dialogue, "npc.anna.06")),
            new ScenarioRule(ScenarioEvent.Interact, new FactIs<LizaHelp>(StoryFacts.LizaTreatment, LizaHelp.Alcohol),
                Reply("AfterItem", SpeechMode.Dialogue, "npc.anna.07")),
            new ScenarioRule(ScenarioEvent.Interact, new FactIs<LizaHelp>(StoryFacts.LizaTreatment, LizaHelp.Stethoscope),
                Reply("AfterItem", SpeechMode.Dialogue, "npc.anna.08")),
            new ScenarioRule(ScenarioEvent.Interact, new Always(), Request()),

            new ScenarioRule(ScenarioEvent.ItemHit, new Not(new FactIs<bool>(StoryFacts.AnnaMet, true)),
                Reply("BeforeMeeting", SpeechMode.Dialogue, "npc.anna.09")),
            new ScenarioRule(ScenarioEvent.ItemHit,
                new ItemIs(ItemId.Stethoscope, ItemId.Pills, ItemId.Alcohol),
                Reply("ItemReceived", SpeechMode.Dialogue, "npc.anna.10", "npc.anna.11")),
            new ScenarioRule(ScenarioEvent.ItemHit, new DuringIntroduction(),
                new Sequence(Reply("EarlyWrongItem", SpeechMode.Dialogue, "npc.anna.12"), Request())),
            new ScenarioRule(ScenarioEvent.ItemHit, new Always(),
                Reply("WrongItem", SpeechMode.Dialogue, "npc.anna.13"))
        };

        public override EndingRule[] Endings { get; } = new[]
        {
            new EndingRule(new FactIs<LizaHelp>(StoryFacts.LizaTreatment, LizaHelp.Pills),
                "npc.anna.14"),
            new EndingRule(new Always(),
                "npc.anna.15")
        };
    }
}
