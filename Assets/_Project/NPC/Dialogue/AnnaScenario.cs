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
            new Say(SpeechMode.Dialogue, "Ты видал чтобы на дитя молча смотрели?"));

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
                        "Тише, тише, моя. Ну тише же!",
                        "Она с вечера, я ей грудь, не берёт. Я ей тряпку мокрую, не берёт.",
                        "Муж два дня как приехал, и она вот…",
                        "Он на неё смотрит, да молчит…"),
                    Request())),

            new ScenarioRule(ScenarioEvent.Interact, new FactIs<LizaHelp>(StoryFacts.LizaTreatment, LizaHelp.Pills),
                Reply("AfterItem", SpeechMode.Dialogue, "Агафьевна говорит, лекарство. Я только бы до утра её довела. А там, может, полегчает.")),
            new ScenarioRule(ScenarioEvent.Interact, new FactIs<LizaHelp>(StoryFacts.LizaTreatment, LizaHelp.Alcohol),
                Reply("AfterItem", SpeechMode.Dialogue, "Растёрли её. Лежит тихо. Я всё слушаю, как дышит.")),
            new ScenarioRule(ScenarioEvent.Interact, new FactIs<LizaHelp>(StoryFacts.LizaTreatment, LizaHelp.Stethoscope),
                Reply("AfterItem", SpeechMode.Dialogue, "Послушала она её. А я всё на лицо смотрю. Жду, когда глаза откроет.")),
            new ScenarioRule(ScenarioEvent.Interact, new Always(), Request()),

            new ScenarioRule(ScenarioEvent.ItemHit, new Not(new FactIs<bool>(StoryFacts.AnnaMet, true)),
                Reply("BeforeMeeting", SpeechMode.Dialogue, "Ай!")),
            new ScenarioRule(ScenarioEvent.ItemHit,
                new ItemIs(ItemId.Stethoscope, ItemId.Pills, ItemId.Alcohol),
                Reply("ItemReceived", SpeechMode.Dialogue, "Не мне.", "Отдай Агафьевне.")),
            new ScenarioRule(ScenarioEvent.ItemHit, new DuringIntroduction(),
                new Sequence(Reply("EarlyWrongItem", SpeechMode.Dialogue, "К чему это?"), Request())),
            new ScenarioRule(ScenarioEvent.ItemHit, new Always(),
                Reply("WrongItem", SpeechMode.Dialogue, "К чему это?"))
        };

        public override EndingRule[] Endings { get; } = new[]
        {
            new EndingRule(new FactIs<LizaHelp>(StoryFacts.LizaTreatment, LizaHelp.Pills),
                "К утру Лиза открыла глаза. Анна ещё долго сидела рядом, боясь поверить, что жар отступил."),
            new EndingRule(new Always(),
                "До утра Лиза не дожила. Анна всё держала её за руку и просила говорить потише.")
        };
    }
}
