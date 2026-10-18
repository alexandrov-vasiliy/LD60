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
                new Say(SpeechMode.Dialogue, "Живой. Слышал? Живой.")),
            Visit(0, "..."),
            Visit(1, "..."),
            Visit(2, "Ну?"),
            Visit(3, "Отстань христа ради, я никому ничего не должен"),
            new ScenarioRule(ScenarioEvent.Interact, new FactIs<int>(StoryFacts.InnokentyVisits, 4),
                new Sequence(
                    new SetFact<int>(StoryFacts.InnokentyVisits, 5),
                    new SetIntroduction(true),
                    new Say(SpeechMode.Dialogue,
                        "Письмо у меня. От сына.", "Третий месяц за пазухой. А я не прочту.",
                        "Не умею.", "И не проси, тебе не дам.",
                        "Вон, грамотный. Всю ночь цифры бормочет."),
                    new SetIntroduction(false),
                    new SetFact<bool>(StoryFacts.InnokentyRequestReached, true),
                    new Say(SpeechMode.Dialogue, "Только он сам себя не слышит. Какая ему моя бумага"))),
            new ScenarioRule(ScenarioEvent.Interact, new Always(),
                new Say(SpeechMode.Dialogue, "Отстань христа ради, я никому ничего не должен")),

            new ScenarioRule(ScenarioEvent.ItemHit, new ItemIs(ItemId.Alcohol),
                Reply("О, это деду надо"), ItemDisposition.Consume),
            new ScenarioRule(ScenarioEvent.ItemHit, new ItemIs(ItemId.Glasses),
                Reply("Буквы вижу. А прочесть всё одно. Не могу.", "Буквы — они не сами говорят.")),
            new ScenarioRule(ScenarioEvent.ItemHit, new Always(), new Say(SpeechMode.Dialogue, "Убери"))
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
            new EndingRule(new FactIs<bool>(StoryFacts.LetterRead, true), "Дед не спился"),
            new EndingRule(new Always(), "Дед спился")
        };
    }
}
