using DialogueSystem;

public sealed class AnnaNpcDialog : NpcDialog
{
    protected override NpcScenario CreateScenario() => new AnnaScenario();
}
