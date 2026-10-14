using RimWorld;
using Verse;

namespace UnlockedSchematics
{
    // Field names must match the defNames
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
