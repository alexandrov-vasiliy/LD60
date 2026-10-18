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
            "Ты не думай, я не боюсь. Я решил.",
            "Боюсь только, что забудут.",
            "Не того, что убьют — а что забудут, какой я был.");

        private static ProtectedSequence Photo() => new ProtectedSequence(
            new PreparePolaroid(),
            new Say(SpeechMode.Dialogue, "Что это?"),
            new TakeEgorPhoto(),
            new SetFact<bool>(StoryFacts.EgorPhotographed, true),
            new Say(SpeechMode.Dialogue,
                "Это я?", "Это я, мам",
                "А чего оно такое маленькое? Ничего. Маленькое — хорошо.",
                "В карман влезет. В шапку влезет", "Спасибо"));

        public override ScenarioRule[] Rules { get; } = new[]
        {
            new ScenarioRule(ScenarioEvent.Interact, new FactIs<bool>(StoryFacts.EgorPhotographed, true),
                new Say(SpeechMode.Dialogue, "Спасибо")),
            new ScenarioRule(ScenarioEvent.Interact, new FactIs<int>(Visits, 0),
                new Sequence(
                    new SetFact<int>(Visits, 1), new SetIntroduction(true),
                    new Say(SpeechMode.Dialogue, "Сядь, чего стоишь. Место есть."),
                    new SetIntroduction(false))),
            new ScenarioRule(ScenarioEvent.Interact, new FactIs<int>(Visits, 1),
                new Sequence(
                    new SetFact<int>(Visits, 2), new SetIntroduction(true),
                    new Say(SpeechMode.Dialogue,
                        "Утром обоз. Через Тихую Пядь пойдёт.",
                        "Мать там. Ей бы карточку. Чтоб не по памяти, а чтоб вот — есть лицо.",
                        "Да и кто ж нарисует. Писарь за пятак такое намалюет, что мать меня не узнает, а перекрестится."),
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
                        new ProtectedSequence(new PreparePolaroid(), new Say(SpeechMode.Dialogue, "Спасибо")),
                        Photo()))),
            new ScenarioRule(ScenarioEvent.ItemHit, new ItemIs(ItemId.Pencil),
                new Say(SpeechMode.Dialogue, "Я не рисую. И писарь не нарисует. Я тебе говорил уже.")),
            new ScenarioRule(ScenarioEvent.ItemHit, new Always(),
                new Say(SpeechMode.Dialogue, "А толку-то?"))
        };

        public override EndingRule[] Endings { get; } = new[]
        {
            new EndingRule(new FactIs<bool>(StoryFacts.EgorPhotographed, true), "Егор ушел на войну, со спокойной душой."),
            new EndingRule(new Always(), "Егор отправился на войну.")
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
