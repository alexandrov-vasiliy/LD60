namespace DialogueSystem
{
    /// <summary>
    /// История Агафьевны: три отдельных вступления и приём медицинских предметов после первого обращения. Предметы не меняют счётчик вступлений. Таблетки закрепляют спасение Лизы; после них спирт и стетоскоп больше не принимаются.
    /// </summary>
    public sealed class AgafyevnaScenario : NpcScenario
    {
        public override string Id => "agafyevna";

        private static Sequence Request() => new Sequence(
            new SetIntroduction(false),
            new PlayFeedback("NeedItem"),
            new Say(SpeechMode.Dialogue,
                "npc.agafyevna.01",
                "npc.agafyevna.02"));

        private static Sequence Reply(string feedback, SpeechMode mode, params string[] lines) => new Sequence(
            new Say(mode, lines) { StartFeedback = feedback });

        private static ScenarioRule Receive(ItemId item, LizaHelp help, string feedback, params string[] lines) =>
            new ScenarioRule(ScenarioEvent.ItemHit, new ItemIs(item),
                new Sequence(
                    new Branch(new DuringIntroduction(),
                        Reply("EarlyCorrectItem", SpeechMode.Dialogue, "npc.agafyevna.03"),
                        new Sequence()),
                    Reply(feedback, SpeechMode.Dialogue, lines)),
                ItemDisposition.Consume,
                new SetFact<LizaHelp>(StoryFacts.LizaTreatment, help));

        public override ScenarioRule[] Rules { get; } = new[]
        {
            new ScenarioRule(ScenarioEvent.Interact, new FactIs<int>(StoryFacts.AgafyevnaVisits, 0),
                new Sequence(
                    new SetFact<int>(StoryFacts.AgafyevnaVisits, 1),
                    new SetIntroduction(true),
                    Reply("BeforeItem", SpeechMode.Dialogue,
                        "npc.agafyevna.04",
                        "npc.agafyevna.05"))),
            new ScenarioRule(ScenarioEvent.Interact, new FactIs<int>(StoryFacts.AgafyevnaVisits, 1),
                new Sequence(
                    new SetFact<int>(StoryFacts.AgafyevnaVisits, 2),
                    new SetIntroduction(true),
                    Reply("SecondIntroduction", SpeechMode.Dialogue,
                        "npc.agafyevna.06",
                        "npc.agafyevna.07"))),
            new ScenarioRule(ScenarioEvent.Interact, new FactIs<int>(StoryFacts.AgafyevnaVisits, 2),
                new Sequence(
                    new SetFact<int>(StoryFacts.AgafyevnaVisits, 3),
                    new SetIntroduction(true),
                    Reply("SecondIntroduction", SpeechMode.Dialogue,
                        "npc.agafyevna.08",
                        "npc.agafyevna.09"))),

            new ScenarioRule(ScenarioEvent.Interact, new FactIs<LizaHelp>(StoryFacts.LizaTreatment, LizaHelp.Pills),
                Reply("AfterItem", SpeechMode.Dialogue, "npc.agafyevna.10")),
            new ScenarioRule(ScenarioEvent.Interact, new FactIs<LizaHelp>(StoryFacts.LizaTreatment, LizaHelp.Alcohol),
                Reply("AfterItem", SpeechMode.Dialogue, "npc.agafyevna.11")),
            new ScenarioRule(ScenarioEvent.Interact, new FactIs<LizaHelp>(StoryFacts.LizaTreatment, LizaHelp.Stethoscope),
                Reply("AfterItem", SpeechMode.Dialogue, "npc.agafyevna.12")),
            new ScenarioRule(ScenarioEvent.Interact, new Always(), Request()),

            new ScenarioRule(ScenarioEvent.ItemHit, new FactIs<int>(StoryFacts.AgafyevnaVisits, 0),
                Reply("BeforeMeeting", SpeechMode.Dialogue, "npc.agafyevna.13")),
            new ScenarioRule(ScenarioEvent.ItemHit,
                new All(new FactIs<LizaHelp>(StoryFacts.LizaTreatment, LizaHelp.Pills),
                    new ItemIs(ItemId.Stethoscope, ItemId.Alcohol)),
                new Say(SpeechMode.Dialogue, "npc.agafyevna.14")),
            Receive(ItemId.Stethoscope, LizaHelp.Stethoscope, "StethoscopeReceived",
                "npc.agafyevna.15",
                "npc.agafyevna.16",
                "npc.agafyevna.17",
                "npc.agafyevna.18",
                "npc.agafyevna.19",
                "npc.agafyevna.20",
                "npc.agafyevna.21"),
            Receive(ItemId.Alcohol, LizaHelp.Alcohol, "AlcoholReceived",
                "npc.agafyevna.22",
                "npc.agafyevna.23",
                "npc.agafyevna.24"),
            Receive(ItemId.Pills, LizaHelp.Pills, "PillsReceived",
                "npc.agafyevna.25",
                "npc.agafyevna.26",
                "npc.agafyevna.27",
                "npc.agafyevna.28",
                "npc.agafyevna.29",
                "npc.agafyevna.30",
                "npc.agafyevna.31"),
            new ScenarioRule(ScenarioEvent.ItemHit, new DuringIntroduction(),
                Reply("EarlyWrongItem", SpeechMode.Dialogue, "npc.agafyevna.32")),
            new ScenarioRule(ScenarioEvent.ItemHit, new Always(),
                Reply("WrongItem", SpeechMode.Dialogue, "npc.agafyevna.33"))
        };

        public override EndingRule[] Endings { get; } = new[]
        {
            new EndingRule(new FactIs<LizaHelp>(StoryFacts.LizaTreatment, LizaHelp.Pills),
                "npc.agafyevna.34"),
            new EndingRule(new FactIs<LizaHelp>(StoryFacts.LizaTreatment, LizaHelp.Alcohol),
                "npc.agafyevna.35"),
            new EndingRule(new FactIs<LizaHelp>(StoryFacts.LizaTreatment, LizaHelp.Stethoscope),
                "npc.agafyevna.36"),
            new EndingRule(new Always(), "npc.agafyevna.37")
        };
    }
}
