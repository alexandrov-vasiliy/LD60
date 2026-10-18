using DialogueSystem;

/// <summary>
/// Подключает сценарий Анны к NPC в сцене; обработку событий и показ реплик наследует от NpcDialog.
/// </summary>
public sealed class AnnaNpcDialog : NpcDialog
{
    protected override NpcScenario CreateScenario() => new AnnaScenario();
}
