namespace DialogueSystem
{
    public sealed class LizaScenario : NpcScenario
    {
        public override string Id => "liza";

        public override ScenarioRule[] Rules { get; } = new[]
        {
            new ScenarioRule(ScenarioEvent.Interact, new Always(),
                new ProtectedSequence(
                    new Say(SpeechMode.Dialogue, "npc.liza.01"),
                    new Say("prokhor", SpeechMode.Dialogue, "LizaGoAway", "npc.liza.02"))),
            new ScenarioRule(ScenarioEvent.ItemHit, new Always(),
                new ProtectedSequence(
                    new Say("agafyevna", SpeechMode.Dialogue, "LizaItemHit", "npc.liza.03")))
        };

        public override EndingRule[] Endings { get; } = new[]
        {
            new EndingRule(new FactIs<LizaHelp>(StoryFacts.LizaTreatment, LizaHelp.Pills), "npc.liza.04"),
            new EndingRule(new Always(), "npc.liza.05")
        };
    }
}
