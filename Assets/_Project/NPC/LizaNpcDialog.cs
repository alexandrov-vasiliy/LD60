using DialogueSystem;

public sealed class LizaNpcDialog : NpcDialog
{
    protected override NpcScenario CreateScenario() => new LizaScenario();
}
