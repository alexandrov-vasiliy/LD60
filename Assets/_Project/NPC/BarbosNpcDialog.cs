using DialogueSystem;

/// <summary>
/// Подключает сценарий Барбоса к NPC в сцене для короткой реакции на обращение.
/// </summary>
public sealed class BarbosNpcDialog : NpcDialog
{
    protected override NpcScenario CreateScenario() => new BarbosScenario();
}
