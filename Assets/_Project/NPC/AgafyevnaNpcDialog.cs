using DialogueSystem;

/// <summary>
/// Подключает сценарий Агафьевны к NPC в сцене, предоставляя ему привязки камер и эффектов через NpcDialog.
/// </summary>
public sealed class AgafyevnaNpcDialog : NpcDialog
{
    protected override NpcScenario CreateScenario() => new AgafyevnaScenario();
}
