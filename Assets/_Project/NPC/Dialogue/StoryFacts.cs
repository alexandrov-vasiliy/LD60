namespace DialogueSystem
{
    public enum LizaHelp { None, Pills, Alcohol, Stethoscope }

    public static class StoryFacts
    {
        public static readonly StoryKey<bool> AnnaMet = new StoryKey<bool>("anna.met", false);
        public static readonly StoryKey<bool> ProkhorMet = new StoryKey<bool>("prokhor.met", false);
        public static readonly StoryKey<bool> AgafyevnaMet = new StoryKey<bool>("agafyevna.met", false);
        public static readonly StoryKey<bool> AgafyevnaIntroduced = new StoryKey<bool>("agafyevna.introduced", false);
        public static readonly StoryKey<LizaHelp> LizaTreatment = new StoryKey<LizaHelp>("liza.help", LizaHelp.None);
    }
}
