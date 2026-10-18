using DialogueSystem;

/// <summary>
/// Подключает сценарий Иннокентия к NPC в сцене; его идентификатор и камеры позволяют Илье включать деда в совместную сцену.
/// </summary>
public sealed class InnokentyNpcDialog : NpcDialog
{
    protected override NpcScenario CreateScenario() => new InnokentyScenario();
}
