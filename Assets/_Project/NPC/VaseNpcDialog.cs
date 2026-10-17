public sealed class VaseNpcDialog : NpcDialog
{
    protected override string[] BeforeItem() => new[]
    {
        "Я Аня. Жду маму из больницы и приготовила её любимые цветы."
    };
    protected override string[] NeedItem() => new[]
    {
        "А ваза разбилась… Найдёшь другую? Подойди и брось её мне на Q."
    };
    protected override string[] EarlyWrongItem() => new[] { "Подожди, ты меня вообще слушаешь?" };
    protected override string[] EarlyCorrectItem() => new[] { "Как ты догадался? Я как раз хотела попросить вазу!" };
    protected override string[] BeforeMeeting() => new[] { "Ой! Зачем кидаешься? Мы ведь ещё даже не знакомы." };
    protected override string[] ItemReceived() => new[]
    {
        "Какая красивая ваза! Спасибо, теперь цветы не завянут.",
        "Когда мама вернётся, дома её будет ждать маленький праздник."
    };
    protected override string[] AfterItem() => new[] { "Цветы уже в воде. Спасибо, что помог подготовить встречу!" };
    protected override string[] WrongItem() => new[] { "Спасибо, но цветы сюда не поставишь. Мне нужна ваза." };
    public override string GetEndingText() => Want.HasReceivedItem
        ? "Аня поставила цветы в подаренную вазу. Мама вернулась домой и улыбнулась: её ждали."
        : "Аня не дождалась вазы. Цветы завяли на столе, но маму она всё равно встретила крепкими объятиями.";
}
