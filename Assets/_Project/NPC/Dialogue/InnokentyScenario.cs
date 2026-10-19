namespace DialogueSystem
{
    /// <summary>
    /// История Иннокентия: пять обращений ведут к просьбе прочесть письмо; только спирт и очки до просьбы переводят к пятому блоку. Хорошую концовку даёт только завершённая совместная сцена из сценария Ильи; спирт и очки не блокируют её.
    /// </summary>
    public sealed class InnokentyScenario : NpcScenario
    {
        public override string Id => "innokenty";

        public override ScenarioRule[] Rules { get; } = new[]
        {
            new ScenarioRule(ScenarioEvent.Interact, new FactIs<bool>(StoryFacts.LetterRead, true),
                new Say(SpeechMode.Dialogue, "npc.innokenty.01")),
            Visit(0, "dialogue.silence"),
            Visit(1, "dialogue.silence"),
            Visit(2, "npc.innokenty.02"),
            Visit(3, "npc.innokenty.03"),
            new ScenarioRule(ScenarioEvent.Interact, new All( new FactIs<int>(StoryFacts.InnokentyVisits, 4), new FactIs<bool>(StoryFacts.IlyaCalculatorGiven, false)),
                new Sequence(
                    new SetFact<int>(StoryFacts.InnokentyVisits, 5),
                    new SetIntroduction(true),
                    new Say(SpeechMode.Dialogue,
                        "npc.innokenty.04", "npc.innokenty.05",
                        "npc.innokenty.06", "npc.innokenty.07",
                        "npc.innokenty.08"),
                    new SetIntroduction(false),
                    new SetFact<bool>(StoryFacts.InnokentyRequestReached, true),
                    new Say(SpeechMode.Dialogue, "npc.innokenty.09"))),
            new ScenarioRule(ScenarioEvent.Interact, new Always(),
                new Say(SpeechMode.Dialogue, "npc.innokenty.10")),

            new ScenarioRule(ScenarioEvent.ItemHit, new ItemIs(ItemId.Alcohol),
                Reply("npc.innokenty.11"), ItemDisposition.Consume),
            new ScenarioRule(ScenarioEvent.ItemHit, new All(new ItemIs(ItemId.Glasses), new FactIs<bool>(StoryFacts.IlyaCalculatorGiven, false)),
                Reply("npc.innokenty.12", "npc.innokenty.13")),
            new ScenarioRule(ScenarioEvent.ItemHit, new Always(), new Say(SpeechMode.Dialogue, "npc.innokenty.14"))
        };

        private static ScenarioRule Visit(int previous, string line) => new ScenarioRule(
            ScenarioEvent.Interact, new FactIs<int>(StoryFacts.InnokentyVisits, previous),
            new Sequence(new SetFact<int>(StoryFacts.InnokentyVisits, previous + 1), new Say(SpeechMode.Dialogue, line)));

        private static Sequence Reply(params string[] lines) => new Sequence(
            new Branch(new FactIs<bool>(StoryFacts.InnokentyRequestReached, false),
                new SetFact<int>(StoryFacts.InnokentyVisits, 4), new Sequence()),
            new Say(SpeechMode.Dialogue, lines));

        public override EndingRule[] Endings { get; } = new[]
        {
            new EndingRule(new FactIs<bool>(StoryFacts.LetterRead, true), "npc.innokenty.15"),
            new EndingRule(new Always(), "npc.innokenty.16")
        };
    }
}
