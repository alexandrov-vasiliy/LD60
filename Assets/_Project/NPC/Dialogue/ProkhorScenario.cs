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
                "Я два дня как с дороги.",
                "Два дня. И за два дня я ей ничем не помог.",
                "Только орал на бабку.",
                "Это я могу."));

        public override ScenarioRule[] Rules { get; } = new[]
        {
            new ScenarioRule(ScenarioEvent.Interact, new FactIs<int>(StoryFacts.ProkhorVisits, 0),
                new Sequence(
                    new SetFact<int>(StoryFacts.ProkhorVisits, 1),
                    new SetIntroduction(true),
                    new PlayFeedback("BeforeItem"),
                    new Say(SpeechMode.Dialogue, "Стоишь — стой.", "Только не над ней.", "Отойди от неё!"),
                    new SetIntroduction(false))),
            new ScenarioRule(ScenarioEvent.Interact, new FactIs<int>(StoryFacts.ProkhorVisits, 1),
                new Sequence(
                    new SetFact<int>(StoryFacts.ProkhorVisits, 2),
                    new SetIntroduction(true),
                    new Say(SpeechMode.Dialogue, "Я тебе не рад.", "В комнате семеро.", "Ты восьмой.", "Восьмой — лишний."),
                    Request())),
            new ScenarioRule(ScenarioEvent.Interact, new Always(), Request()),

            new ScenarioRule(ScenarioEvent.ItemHit, new FactIs<int>(StoryFacts.ProkhorVisits, 0),
                new Sequence(new PlayFeedback("BeforeMeeting"), new Say(SpeechMode.Dialogue, "Убери"))),
            new ScenarioRule(ScenarioEvent.ItemHit, new ItemIs(ItemId.Stethoscope, ItemId.Pills, ItemId.Alcohol),
                new Sequence(new PlayFeedback("ItemReceived"), new Say(SpeechMode.Dialogue, "Это не мне.", "Отдай старухе."))),
            new ScenarioRule(ScenarioEvent.ItemHit, new Always(),
                new Sequence(new PlayFeedback("WrongItem"), new Say(SpeechMode.Dialogue, "Убери")))
        };

        public override EndingRule[] Endings { get; } = new[]
        {
            new EndingRule(new FactIs<LizaHelp>(StoryFacts.LizaTreatment, LizaHelp.Pills),
                "К утру Лизе стало лучше. Прохор впервые за два дня перестал срываться на окружающих."),
            new EndingRule(new Always(), "Лиза умерла. Прохор так и не нашёл слов, которыми можно было бы оправдаться.")
        };
    }
}
