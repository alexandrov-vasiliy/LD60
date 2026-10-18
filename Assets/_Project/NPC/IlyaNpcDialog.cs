using DialogueSystem;

/// <summary>
/// Подключает сценарий Ильи к NPC в сцене; именованные камеры используются в его репликах совместной сцены письма.
/// </summary>
public sealed class IlyaNpcDialog : NpcDialog
{
    protected override NpcScenario CreateScenario() => new IlyaScenario();
}
