using HarmonyLib;
using Verse;

namespace UnlockedSchematics
{
    // Applies the Harmony patches on startup
    [StaticConstructorOnStartup]
    public static class UnlockedSchematicsMod
    {
        static UnlockedSchematicsMod()
        {
            new Harmony("Vita.UnlockedSchematics").PatchAll(typeof(UnlockedSchematicsMod).Assembly);
        }
    }
}
