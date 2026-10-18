using DialogueSystem;

public sealed class ProkhorNpcDialog : NpcDialog
{
    protected override NpcScenario CreateScenario() => new ProkhorScenario();
}
