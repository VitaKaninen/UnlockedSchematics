using HarmonyLib;
using Verse;

namespace UnlockedSchematics
{
    /// <summary>
    /// Harmony bootstrap. Runs once after defs are loaded.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class UnlockedSchematicsMod
    {
        static UnlockedSchematicsMod()
        {
            new Harmony("Vita.UnlockedSchematics").PatchAll();
        }
    }
}
