namespace DialogueSystem
{
    public sealed class LizaScenario : NpcScenario
    {
        public override string Id => "liza";

        public override ScenarioRule[] Rules { get; } = new[]
        {
            new ScenarioRule(ScenarioEvent.Interact, new Always(),
                new ProtectedSequence(
                    new Say(SpeechMode.Dialogue, "ААААААААААААААААА ааааааааааааааааааааааааа"),
                    new Say("prokhor", SpeechMode.Dialogue, "LizaGoAway", "Отойди от нее"))),
            new ScenarioRule(ScenarioEvent.ItemHit, new Always(),
                new ProtectedSequence(
                    new Say("agafyevna", SpeechMode.Dialogue, "LizaItemHit", "ТЫ С ДУБА РУХНУЛ!!!")))
        };

        public override EndingRule[] Endings { get; } = new[]
        {
            new EndingRule(new FactIs<LizaHelp>(StoryFacts.LizaTreatment, LizaHelp.Pills), "Лиза выжила."),
            new EndingRule(new Always(), "Лиза умерла.")
        };
    }
}
