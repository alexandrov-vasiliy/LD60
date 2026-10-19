namespace DialogueSystem
{
    /// <summary>
    /// История Ильи: три обращения ведут к просьбе помочь со счётом; ранний предмет переводит к третьему блоку. Сдаёт с калькулятором или в тишине после смерти Лизы; после калькулятора отказывается от счётов. Если просьба Иннокентия уже достигнута на момент передачи калькулятора, запускает защищённую сцену письма; хороший исход деда записывается после последней реплики.
    /// </summary>
    public sealed class IlyaScenario : NpcScenario
    {
        public override string Id => "ilya";

        private static Say Request() => new Say(SpeechMode.Dialogue, "npc.ilya.01");

        private static Say CalculatorReply() => new Say(SpeechMode.Dialogue,
            "npc.ilya.02",
            "npc.ilya.03",
            "npc.ilya.04",
            "npc.ilya.05",
            "npc.ilya.06",
            "dialogue.silence",
            "npc.ilya.07");

        private static ProtectedSequence LetterScene() => new ProtectedSequence(
            new Say("innokenty", SpeechMode.Dialogue, "Letter01_Call", "npc.ilya.08"),
            new Say("innokenty", SpeechMode.Dialogue, "Letter02_Come", "npc.ilya.09"),
            new Say("ilya", SpeechMode.Dialogue, "Letter03_Tired", "npc.ilya.10"),
            new Say("innokenty", SpeechMode.Dialogue, "Letter04_Waited", "npc.ilya.11"),
            new Say("innokenty", SpeechMode.Dialogue, "Letter05_Letter", "npc.ilya.12"),
            new Say("innokenty", SpeechMode.Dialogue, "Letter06_Quietly", "npc.ilya.13"),
            new Say("innokenty", SpeechMode.Dialogue, "Letter07_OneWord", "npc.ilya.14"),
            new Say("ilya", SpeechMode.Dialogue, "Letter08_Read", "dialogue.silence"),
            new Say("innokenty", SpeechMode.Dialogue, "Letter09_Question", "npc.ilya.15"),
            new Say("ilya", SpeechMode.Dialogue, "Letter10_Alive", "npc.ilya.16"),
            new Say("ilya", SpeechMode.Dialogue, "Letter11_DontDrink", "npc.ilya.17"),
            new Say("innokenty", SpeechMode.Dialogue, "Letter12_Silence", "dialogue.silence"),
            new Say("innokenty", SpeechMode.Dialogue, "Letter13_Really", "npc.ilya.18"),
            new Say("ilya", SpeechMode.Dialogue, "Letter14_Yes", "npc.ilya.19"),
            new Say("innokenty", SpeechMode.Dialogue, "Letter15_Fool", "npc.ilya.20"),
            new SetFact<bool>(StoryFacts.LetterRead, true));

        public override ScenarioRule[] Rules { get; } = new[]
        {
            new ScenarioRule(ScenarioEvent.Interact, new FactIs<bool>(StoryFacts.IlyaCalculatorGiven, true),
                new Say(SpeechMode.Dialogue, "npc.ilya.21")),
            new ScenarioRule(ScenarioEvent.Interact, new FactIs<int>(StoryFacts.IlyaVisits, 0),
                new Sequence(
                    new SetFact<int>(StoryFacts.IlyaVisits, 1),
                    new SetIntroduction(true),
                    new Say(SpeechMode.Dialogue,
                        "npc.ilya.22",
                        "npc.ilya.23",
                        "npc.ilya.24",
                        "npc.ilya.25"),
                    new SetIntroduction(false))),
            new ScenarioRule(ScenarioEvent.Interact, new FactIs<int>(StoryFacts.IlyaVisits, 1),
                new Sequence(
                    new SetFact<int>(StoryFacts.IlyaVisits, 2),
                    new SetIntroduction(true),
                    new Say(SpeechMode.Dialogue,
                        "npc.ilya.26",
                        "npc.ilya.27",
                        "npc.ilya.28",
                        "npc.ilya.29"),
                    new SetIntroduction(false))),
            new ScenarioRule(ScenarioEvent.Interact, new FactIs<int>(StoryFacts.IlyaVisits, 2),
                new Sequence(
                    new SetFact<int>(StoryFacts.IlyaVisits, 3),
                    new SetIntroduction(true),
                    new Say(SpeechMode.Dialogue,
                        "npc.ilya.30",
                        "npc.ilya.31",
                        "npc.ilya.32",
                        "dialogue.silence",
                        "npc.ilya.33",
                        "npc.ilya.34"),
                    new SetIntroduction(false), Request())),
            new ScenarioRule(ScenarioEvent.Interact, new Always(), Request()),

            new ScenarioRule(ScenarioEvent.ItemHit,
                new All(new ItemIs(ItemId.Calculator), new FactIs<bool>(StoryFacts.IlyaCalculatorGiven, false),
                    new FactIs<bool>(StoryFacts.InnokentyRequestReached, true)),
                new ProtectedSequence(CalculatorReply(), LetterScene()), ItemDisposition.Consume,
                new SetFact<int>(StoryFacts.IlyaVisits, 3),
                new SetFact<bool>(StoryFacts.IlyaCalculatorGiven, true)),
            new ScenarioRule(ScenarioEvent.ItemHit,
                new All(new ItemIs(ItemId.Calculator), new FactIs<bool>(StoryFacts.IlyaCalculatorGiven, false)),
                CalculatorReply(), ItemDisposition.Consume,
                new SetFact<int>(StoryFacts.IlyaVisits, 3),
                new SetFact<bool>(StoryFacts.IlyaCalculatorGiven, true)),
            new ScenarioRule(ScenarioEvent.ItemHit, new ItemIs(ItemId.Calculator),
                Reply("npc.ilya.35")),
            new ScenarioRule(ScenarioEvent.ItemHit, new ItemIs(ItemId.Pencil),
                Reply("npc.ilya.36", "npc.ilya.37",
                    "npc.ilya.38")),
            new ScenarioRule(ScenarioEvent.ItemHit,
                new All(new ItemIs(ItemId.Abacus), new FactIs<bool>(StoryFacts.IlyaCalculatorGiven, true)),
                Reply("npc.ilya.39")),
            new ScenarioRule(ScenarioEvent.ItemHit, new ItemIs(ItemId.Abacus),
                Reply("npc.ilya.40", "npc.ilya.41",
                    "npc.ilya.42")),
            new ScenarioRule(ScenarioEvent.ItemHit, new Always(), Reply("npc.ilya.43"))
        };

        private static Sequence Reply(params string[] lines) => new Sequence(
            new Branch(new Any(new FactIs<int>(StoryFacts.IlyaVisits, 0), new FactIs<int>(StoryFacts.IlyaVisits, 1)),
                new SetFact<int>(StoryFacts.IlyaVisits, 2), new Sequence()),
            new Say(SpeechMode.Dialogue, lines));

        public override EndingRule[] Endings { get; } = new[]
        {
            new EndingRule(new FactIs<bool>(StoryFacts.IlyaCalculatorGiven, true), "npc.ilya.44"),
            new EndingRule(new Not(new FactIs<LizaHelp>(StoryFacts.LizaTreatment, LizaHelp.Pills)),
                "npc.ilya.45"),
            new EndingRule(new Always(), "npc.ilya.46")
        };
    }
}
