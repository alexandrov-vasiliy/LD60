using DialogueSystem;

/// <summary>
/// Подключает сценарий Прохора к NPC в сцене; обработку событий и показ реплик наследует от NpcDialog.
/// </summary>
public sealed class ProkhorNpcDialog : NpcDialog
{
    protected override NpcScenario CreateScenario() => new ProkhorScenario();
}
