namespace DialogueSystem
{
    /// <summary>
    /// Простой сценарий Барбоса: каждое обращение показывает «ГАВ!». Реакций на предметы нет, пустая концовка исключает его из общего экрана итогов.
    /// </summary>
    public sealed class BarbosScenario : NpcScenario
    {
        public override string Id => "barbos";

        public override ScenarioRule[] Rules { get; } = new[]
        {
            new ScenarioRule(ScenarioEvent.Interact, new Always(),
                new Say(SpeechMode.Dialogue, "ГАВ!"))
        };

        public override EndingRule[] Endings { get; } = new[]
        {
            new EndingRule(new Always(), string.Empty)
        };
    }
}
