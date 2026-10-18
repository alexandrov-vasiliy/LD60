using DialogueSystem;

public sealed class AgafyevnaNpcDialog : NpcDialog
{
    protected override NpcScenario CreateScenario() => new AgafyevnaScenario();
}
