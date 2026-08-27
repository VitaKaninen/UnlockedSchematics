using RimWorld;
using Verse;

namespace UnlockedSchematics
{
    /// <summary>Defs this mod adds. Field names must match their defName.</summary>
    [DefOf]
    public static class UnlockedSchematicsDefOf
    {
        public static JobDef UnlockedSchematics_ApplyTechprintInPlace;

        static UnlockedSchematicsDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(UnlockedSchematicsDefOf));
        }
    }
}
