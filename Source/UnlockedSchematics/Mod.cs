using HarmonyLib;
using Verse;

namespace UnlockedSchematics
{
    /// <summary>
    /// Entry point. RimWorld runs this static constructor once at startup, after defs load;
    /// PatchAll applies every Harmony patch class in this assembly.
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
