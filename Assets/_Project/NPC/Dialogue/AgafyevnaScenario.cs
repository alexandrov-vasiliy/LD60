namespace DialogueSystem
{
    /// <summary>
    /// История Агафьевны: два этапа знакомства и приём медицинских предметов после первого обращения. Каждая передача заменяет вид помощи Лизе до начала реплик; таблетки дают хороший исход, спирт, стетоскоп и отсутствие помощи — плохой.
    /// </summary>
    public sealed class AgafyevnaScenario : NpcScenario
    {
        public override string Id => "agafyevna";

        private static Sequence Request() => new Sequence(
            new SetIntroduction(false),
            new PlayFeedback("NeedItem"),
            new Say(SpeechMode.Dialogue,
                "Не дотянет до свету.",
                "Я двадцать таких выходила и двадцать схоронила. Я знаю, как оно выглядит."));

        private static Sequence Reply(string feedback, SpeechMode mode, params string[] lines) => new Sequence(
            new Say(mode, lines) { StartFeedback = feedback });

        private static ScenarioRule Receive(ItemId item, LizaHelp help, string feedback, params string[] lines) =>
            new ScenarioRule(ScenarioEvent.ItemHit, new ItemIs(item),
                new Sequence(
                    new Branch(new DuringIntroduction(),
                        Reply("EarlyCorrectItem", SpeechMode.Dialogue, "А? Чего ты мне тут суёшь?!"),
                        new Sequence()),
                    Reply(feedback, SpeechMode.Dialogue, lines)),
                ItemDisposition.Consume,
                new SetFact<bool>(StoryFacts.AgafyevnaIntroduced, true),
                new SetFact<LizaHelp>(StoryFacts.LizaTreatment, help));

        public override ScenarioRule[] Rules { get; } = new[]
        {
            new ScenarioRule(ScenarioEvent.Interact, new Not(new FactIs<bool>(StoryFacts.AgafyevnaMet, true)),
                new Sequence(
                    new SetFact<bool>(StoryFacts.AgafyevnaMet, true),
                    new SetIntroduction(true),
                    Reply("BeforeItem", SpeechMode.Dialogue,
                        "Не лезь.",
                        "Ходит тут. Ходит, а толку нет."))),
            new ScenarioRule(ScenarioEvent.Interact, new Not(new FactIs<bool>(StoryFacts.AgafyevnaIntroduced, true)),
                new Sequence(
                    new SetFact<bool>(StoryFacts.AgafyevnaIntroduced, true),
                    new SetIntroduction(true),
                    Reply("SecondIntroduction", SpeechMode.Dialogue,
                        "Ты кто таков? Не из наших. И не прохожий — прохожие так не стоят.",
                        "…",
                        "Ну и стой. Мне-то что.",
                        "…"),
                    Request())),

            new ScenarioRule(ScenarioEvent.Interact, new FactIs<LizaHelp>(StoryFacts.LizaTreatment, LizaHelp.Pills),
                Reply("AfterItem", SpeechMode.Dialogue, "Хоть бы до утра. Хоть бы до свету дожить")),
            new ScenarioRule(ScenarioEvent.Interact, new FactIs<LizaHelp>(StoryFacts.LizaTreatment, LizaHelp.Alcohol),
                Reply("AfterItem", SpeechMode.Dialogue, "Хоть что-то путное за вечер.")),
            new ScenarioRule(ScenarioEvent.Interact, new FactIs<LizaHelp>(StoryFacts.LizaTreatment, LizaHelp.Stethoscope),
                Reply("AfterItem", SpeechMode.Dialogue, "Это не сглаз. Сглаз не хрипит. Сглаз — он тихий.")),
            new ScenarioRule(ScenarioEvent.Interact, new Always(), Request()),

            new ScenarioRule(ScenarioEvent.ItemHit, new Not(new FactIs<bool>(StoryFacts.AgafyevnaMet, true)),
                Reply("BeforeMeeting", SpeechMode.Dialogue, "Убери от меня эту дребедень!")),
            Receive(ItemId.Stethoscope, LizaHelp.Stethoscope, "StethoscopeReceived",
                "Чего это? Железо и кишка. И что мне с этого?",
                "Я не поняла, чего ты сказал.",
                "Приложить это к ушам. А это к груди. А-а-а.",
                "Хрипит. Вот тут хрипит. И вот тут.",
                "Это не сглаз. Сглаз не хрипит.",
                "Сглаз — он тихий.",
                "Ты откуда такой?"),
            Receive(ItemId.Alcohol, LizaHelp.Alcohol, "AlcoholReceived",
                "Вот это я знаю. Крепкий. Добрый.",
                "Растирать будем. Грудь, ножки, под мышками. Иди. Не мешай. Мужик в горнице — беда.",
                "Хоть что-то путное за вечер."),
            Receive(ItemId.Pills, LizaHelp.Pills, "PillsReceived",
                "Плесенью пахнет",
                "Хлебной",
                "…",
                "Гниль — гнилью, так и лечат.",
                "Сколь давать? Сама знаю, я двадцать выходила",
                "…",
                "Хоть бы до утра. Хоть бы до свету дожить"),
            new ScenarioRule(ScenarioEvent.ItemHit, new DuringIntroduction(),
                new Sequence(
                    new SetFact<bool>(StoryFacts.AgafyevnaIntroduced, true),
                    Reply("EarlyWrongItem", SpeechMode.Dialogue, "Убери от меня эту дребедень!"))),
            new ScenarioRule(ScenarioEvent.ItemHit, new Always(),
                Reply("WrongItem", SpeechMode.Dialogue, "Убери от меня эту дребедень!"))
        };

        public override EndingRule[] Endings { get; } = new[]
        {
            new EndingRule(new FactIs<LizaHelp>(StoryFacts.LizaTreatment, LizaHelp.Pills),
                "Агафьевна дала Лизе таблетки. К утру жар отступил, и девочка выжила."),
            new EndingRule(new FactIs<LizaHelp>(StoryFacts.LizaTreatment, LizaHelp.Alcohol),
                "Агафьевна растирала Лизу спиртом, но до утра девочка не дожила."),
            new EndingRule(new FactIs<LizaHelp>(StoryFacts.LizaTreatment, LizaHelp.Stethoscope),
                "Агафьевна послушала дыхание Лизы, но помочь ей не смогла. До утра девочка не дожила."),
            new EndingRule(new Always(), "Агафьевна так и не получила лекарства. До утра Лиза не дожила.")
        };
    }
}
