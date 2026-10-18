namespace DialogueSystem
{
    /// <summary>
    /// История Ильи: три обращения ведут к просьбе помочь со счётом; ранний предмет переводит к третьему блоку. Сдаёт с калькулятором или в тишине после смерти Лизы; после калькулятора отказывается от счётов. Если просьба Иннокентия уже достигнута на момент передачи калькулятора, запускает защищённую сцену письма; хороший исход деда записывается после последней реплики.
    /// </summary>
    public sealed class IlyaScenario : NpcScenario
    {
        public override string Id => "ilya";

        private static Say Request() => new Say(SpeechMode.Dialogue, "Я не хочу на фронт...");

        private static Say CalculatorReply() => new Say(SpeechMode.Dialogue,
            "Кнопки? Восемьдесят четыре.",
            "Оно запомнило...",
            "Оно запомнило! Я выпал, а оно — нет!",
            "И шесть. Девяносто. Три часа я сюда не мог дойти. Три часа.",
            "Ты кто? Не говори. Не говори, я собьюсь.",
            "...",
            "Девяносто три. Я так и думал, что девяносто три. Я не мог доказать.");

        private static ProtectedSequence LetterScene() => new ProtectedSequence(
            new Say("innokenty", SpeechMode.Dialogue, "Letter01_Call", "Эй. Грамотный!"),
            new Say("innokenty", SpeechMode.Dialogue, "Letter02_Come", "Слышишь, что ли. Поди сюда!"),
            new Say("ilya", SpeechMode.Dialogue, "Letter03_Tired", "Я всю ночь сводил. Я спать хочу."),
            new Say("innokenty", SpeechMode.Dialogue, "Letter04_Waited", "И я хочу! Три месяца как хочу!"),
            new Say("innokenty", SpeechMode.Dialogue, "Letter05_Letter", "Прочти."),
            new Say("innokenty", SpeechMode.Dialogue, "Letter06_Quietly", "Только вслух не надо."),
            new Say("innokenty", SpeechMode.Dialogue, "Letter07_OneWord", "Ты про себя прочти и мне скажи. Одним словом."),
            new Say("ilya", SpeechMode.Dialogue, "Letter08_Read", "..."),
            new Say("innokenty", SpeechMode.Dialogue, "Letter09_Question", "Ну?"),
            new Say("ilya", SpeechMode.Dialogue, "Letter10_Alive", "Живой."),
            new Say("ilya", SpeechMode.Dialogue, "Letter11_DontDrink", "Пишет — живой, и чтоб ты не пил"),
            new Say("innokenty", SpeechMode.Dialogue, "Letter12_Silence", "..."),
            new Say("innokenty", SpeechMode.Dialogue, "Letter13_Really", "Так и написано?"),
            new Say("ilya", SpeechMode.Dialogue, "Letter14_Yes", "Так и написано."),
            new Say("innokenty", SpeechMode.Dialogue, "Letter15_Fool", "Дурак..."),
            new SetFact<bool>(StoryFacts.LetterRead, true));

        public override ScenarioRule[] Rules { get; } = new[]
        {
            new ScenarioRule(ScenarioEvent.Interact, new FactIs<bool>(StoryFacts.IlyaCalculatorGiven, true),
                new Say(SpeechMode.Dialogue, "Теперь всё сходится.")),
            new ScenarioRule(ScenarioEvent.Interact, new FactIs<int>(StoryFacts.IlyaVisits, 0),
                new Sequence(
                    new SetFact<int>(StoryFacts.IlyaVisits, 1),
                    new SetIntroduction(true),
                    new Say(SpeechMode.Dialogue,
                        "Сорок один. Сорок два. Сорок три...",
                        "Сорок один. Сорок два, да, ГОСПОДИ.",
                        "Что?!",
                        "Молчи. Молчи, пожалуйста, я почти..."),
                    new SetIntroduction(false))),
            new ScenarioRule(ScenarioEvent.Interact, new FactIs<int>(StoryFacts.IlyaVisits, 1),
                new Sequence(
                    new SetFact<int>(StoryFacts.IlyaVisits, 2),
                    new SetIntroduction(true),
                    new Say(SpeechMode.Dialogue,
                        "Я так с вечера. Я не считать не умею. Я её слышу.",
                        "Она кричит — и у меня из головы всё вышибает. Как ведром.",
                        "Я дохожу до сорок второго и всё. Дальше пусто.",
                        "Я не прошу, чтоб она замолчала. Я прошу, чтоб я не слышал."),
                    new SetIntroduction(false))),
            new ScenarioRule(ScenarioEvent.Interact, new FactIs<int>(StoryFacts.IlyaVisits, 2),
                new Sequence(
                    new SetFact<int>(StoryFacts.IlyaVisits, 3),
                    new SetIntroduction(true),
                    new Say(SpeechMode.Dialogue,
                        "Мне к утру ведомость. Семьдесят шесть позиций.",
                        "Я трижды сводил. Трижды разное. И знаешь почему?",
                        "Потому что она ни разу за вечер не замолчала.",
                        "...",
                        "Прохор вчера мои бумаги глядел. Смотрел и говорит: «Не сдашь — пойдёшь четвёртым. А у него уже трое и так раз одного не хватает».",
                        "Я не боюсь. Я просто, что меня убьют за то, что я не могу в голове удержать цифру, которую знаю."),
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
                Reply("У меня уже есть. Спасибо.")),
            new ScenarioRule(ScenarioEvent.ItemHit, new ItemIs(ItemId.Pencil),
                Reply("Дело не в том, чем писать.", "Я записываю. Я всё записываю!",
                    "А потом слышу — и записываю уже не то. Вот тут, гляди, у меня «84» и «90» на одном листе стоит. Оба мои. Оба враньё.")),
            new ScenarioRule(ScenarioEvent.ItemHit,
                new All(new ItemIs(ItemId.Abacus), new FactIs<bool>(StoryFacts.IlyaCalculatorGiven, true)),
                Reply("Мне это больше не нужно.")),
            new ScenarioRule(ScenarioEvent.ItemHit, new ItemIs(ItemId.Abacus),
                Reply("Я знаю что это.", "Восемьдесят четыре. Восемьдесят пять.",
                    "Видишь? Костяшки остались. Мне не считать. Мне помнить.")),
            new ScenarioRule(ScenarioEvent.ItemHit, new Always(), Reply("Убери, я не торгую. Я сдаю"))
        };

        private static Sequence Reply(params string[] lines) => new Sequence(
            new Branch(new Any(new FactIs<int>(StoryFacts.IlyaVisits, 0), new FactIs<int>(StoryFacts.IlyaVisits, 1)),
                new SetFact<int>(StoryFacts.IlyaVisits, 2), new Sequence()),
            new Say(SpeechMode.Dialogue, lines));

        public override EndingRule[] Endings { get; } = new[]
        {
            new EndingRule(new FactIs<bool>(StoryFacts.IlyaCalculatorGiven, true), "Илья сдал экзамен."),
            new EndingRule(new Not(new FactIs<LizaHelp>(StoryFacts.LizaTreatment, LizaHelp.Pills)),
                "Когда прекратились крики, Илья смог сосредоточится."),
            new EndingRule(new Always(), "Илья не сдал, и ушел с Прохором на фронт.")
        };
    }
}
