namespace DialogueSystem
{
    /// <summary>
    /// Вид помощи Лизе; таблетки имеют приоритет и больше не заменяются спиртом или стетоскопом. Определяет реплики и будущий исход, но не означает уже наступившую смерть или выздоровление.
    /// </summary>
    public enum LizaHelp { None, Pills, Alcohol, Stethoscope }

    /// <summary>
    /// Общие ключи фактов, связывающих истории NPC: знакомства, лечение Лизы, передача калькулятора и чтение письма. Все сценарии используют одни и те же экземпляры ключей.
    /// </summary>
    public static class StoryFacts
    {
        public static readonly StoryKey<int> IlyaVisits = new StoryKey<int>("ilya.visits", 0);
        public static readonly StoryKey<bool> IlyaCalculatorGiven = new StoryKey<bool>("ilya.calculator", false);
        public static readonly StoryKey<int> InnokentyVisits = new StoryKey<int>("innokenty.visits", 0);
        public static readonly StoryKey<bool> InnokentyRequestReached = new StoryKey<bool>("innokenty.request", false);
        public static readonly StoryKey<bool> LetterRead = new StoryKey<bool>("innokenty.letter.read", false);
        public static readonly StoryKey<bool> AnnaMet = new StoryKey<bool>("anna.met", false);
        public static readonly StoryKey<int> ProkhorVisits = new StoryKey<int>("prokhor.visits", 0);
        public static readonly StoryKey<int> AgafyevnaVisits = new StoryKey<int>("agafyevna.visits", 0);
        public static readonly StoryKey<LizaHelp> LizaTreatment = new StoryKey<LizaHelp>("liza.help", LizaHelp.None);
    }
}
