public sealed class WriterNpcDialog : NpcDialog
{
    protected override string[] BeforeItem() => new[]
    {
        "Я Илья. Сегодня последний день приёма рассказов, а моя печатная машинка сломалась."
    };
    protected override string[] NeedItem() => new[]
    {
        "Если найдёшь машинку, брось её мне клавишей Q. Тогда я успею закончить историю."
    };
    protected override string[] EarlyWrongItem() => new[] { "Ты меня вообще слушаешь? Я ещё не закончил!" };
    protected override string[] EarlyCorrectItem() => new[] { "Ого, ты читаешь мои мысли! Я ещё не успел попросить машинку." };
    protected override string[] BeforeMeeting() => new[] { "Эй, зачем кидаешься? Давай сначала познакомимся!" };
    protected override string[] ItemReceived() => new[]
    {
        "Машинка! И даже клавиша «Ё» на месте! Спасибо!",
        "Теперь я допишу рассказ. Ты обязательно появишься в посвящении."
    };
    protected override string[] AfterItem() => new[]
    {
        UnityEngine.Random.value < 0.5f ? "Уже печатаю последнюю страницу. Без тебя я бы не успел." : "Тук-тук-тук… Кажется, у этой истории будет хороший конец!"
    };
    protected override string[] WrongItem() => new[] { "Красиво, но рассказ на этом не напечатаешь. Мне нужна печатная машинка." };
    public override string GetEndingText() => Want.HasReceivedItem
        ? "Илья получил машинку, дописал рассказ и отправил его вовремя. В посвящении он поблагодарил незнакомца."
        : "Илья так и не получил машинку. Приём работ закончился, а его рассказ остался недописанным.";
}
