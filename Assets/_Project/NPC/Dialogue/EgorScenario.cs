namespace DialogueSystem
{
    /// <summary>
    /// История Егора: три обращения ведут к просьбе оставить матери фотографию. Наличие полароида после третьего блока или его попадание запускает защищённую съёмку; спокойная концовка доступна после передачи карточки, а отказы от других предметов её не запрещают.
    /// </summary>
    public sealed class EgorScenario : NpcScenario
    {
        public static readonly StoryKey<int> Visits = new StoryKey<int>("egor.visits", 0);
        public override string Id => "egor";

        private static Say Need() => new Say(SpeechMode.Dialogue,
            "npc.egor.01",
            "npc.egor.02",
            "npc.egor.03");

        private static ProtectedSequence Photo() => new ProtectedSequence(
            new PreparePolaroid(),
            new Say(SpeechMode.Dialogue, "npc.egor.04"),
            new TakeEgorPhoto(),
            new SetFact<bool>(StoryFacts.EgorPhotographed, true),
            new Say(SpeechMode.Dialogue,
                "npc.egor.05", "npc.egor.06",
                "npc.egor.07",
                "npc.egor.08", "npc.egor.09"));

        public override ScenarioRule[] Rules { get; } = new[]
        {
            new ScenarioRule(ScenarioEvent.Interact, new FactIs<bool>(StoryFacts.EgorPhotographed, true),
                new Say(SpeechMode.Dialogue, "npc.egor.10")),
            new ScenarioRule(ScenarioEvent.Interact, new FactIs<int>(Visits, 0),
                new Sequence(
                    new SetFact<int>(Visits, 1), new SetIntroduction(true),
                    new Say(SpeechMode.Dialogue, "npc.egor.11"),
                    new SetIntroduction(false))),
            new ScenarioRule(ScenarioEvent.Interact, new FactIs<int>(Visits, 1),
                new Sequence(
                    new SetFact<int>(Visits, 2), new SetIntroduction(true),
                    new Say(SpeechMode.Dialogue,
                        "npc.egor.12",
                        "npc.egor.13",
                        "npc.egor.14"),
                    new SetIntroduction(false))),
            new ScenarioRule(ScenarioEvent.Interact, new FactIs<int>(Visits, 2),
                new Sequence(
                    new SetFact<int>(Visits, 3), new SetIntroduction(true),
                    Need(), new SetIntroduction(false),
                    new Branch(new HasPolaroid(), Photo(), new EndDialogue()))),
            new ScenarioRule(ScenarioEvent.Interact, new Always(),
                new Branch(new HasPolaroid(), Photo(), Need())),

            new ScenarioRule(ScenarioEvent.ItemHit, new ItemIs(ItemId.Polaroid),
                new Sequence(new SetFact<int>(Visits, 3),
                    new Branch(new FactIs<bool>(StoryFacts.EgorPhotographed, true),
                        new ProtectedSequence(new PreparePolaroid(), new Say(SpeechMode.Dialogue, "npc.egor.15")),
                        Photo()))),
            new ScenarioRule(ScenarioEvent.ItemHit, new ItemIs(ItemId.Pencil),
                new Say(SpeechMode.Dialogue, "npc.egor.16")),
            new ScenarioRule(ScenarioEvent.ItemHit, new Always(),
                new Say(SpeechMode.Dialogue, "npc.egor.17"))
        };

        public override EndingRule[] Endings { get; } = new[]
        {
            new EndingRule(new FactIs<bool>(StoryFacts.EgorPhotographed, true), "npc.egor.18"),
            new EndingRule(new Always(), "npc.egor.19")
        };
    }

    /// <summary>
    /// Проверяет наличие полароида в любом слоте инвентаря игрока для перехода от просьбы Егора к съёмке.
    /// </summary>
    internal sealed class HasPolaroid : ScenarioCondition
    {
        public override bool Matches(ScenarioContext context) =>
            ((EgorNpcDialog)context.Npc).Inventory.Contains(ItemId.Polaroid);
    }

    /// <summary>
    /// Готовит съёмку Егора: возвращает попавший полароид в инвентарь и выбирает слот с камерой; заранее проверяет необходимые ссылки.
    /// </summary>
    internal sealed class PreparePolaroid : ScenarioStep
    {
        public override void Validate(ScenarioContext context) => ((EgorNpcDialog)context.Npc).ValidatePhoto();
        public override void Execute(ScenarioRunner runner) => ((EgorNpcDialog)runner.Context.Npc).PreparePolaroid(runner.Context.Item);
    }

    /// <summary>
    /// Приостанавливает сценарий Егора до нажатия Q или подтверждения диалога и завершения передачи фотокарточки. Ожидание ввода, эффекты и очистку ресурсов выполняет EgorNpcDialog.
    /// </summary>
    internal sealed class TakeEgorPhoto : ScenarioStep
    {
        public override void Validate(ScenarioContext context) => ((EgorNpcDialog)context.Npc).ValidatePhoto();
        public override void Execute(ScenarioRunner runner) => ((EgorNpcDialog)runner.Context.Npc).WaitForPhoto(runner);
    }
}
