using HarmonyLib;
using RimWorld;
using Verse;

namespace UnlockedSchematics
{
    // Schematics only for projects the colony has unlocked
    [HarmonyPatch(typeof(ReadingOutcomeDoerGainResearch), "IsValid")]
    public static class Patch_SchematicEligibility
    {
        // techprintCount untouched
        private const int NoRestore = -1;

        // Unlock checks from CanStartNow
        private static bool Unlocked(ResearchProjectDef project)
        {
            return project.TechprintRequirementMet          // Royalty techprints applied
                && project.PlayerMechanitorRequirementMet   // Biotech mechanitor in colony
                && project.AnalyzedThingsRequirementsMet    // required item studied
                && project.InspectionRequirementsMet;       // Odyssey grav engine inspected
        }

        // Rejects locked projects, then zeroes techprintCount so vanilla accepts the rest
        private static bool Prefix(ResearchProjectDef project, ref bool __result, out int __state)
        {
            __state = NoRestore;

            if (project == null)
            {
                return true;
            }

            // Must run before techprintCount is zeroed
            if (!Unlocked(project))
            {
                __result = false;
                return false;
            }

            __state = project.techprintCount;
            project.techprintCount = 0;
            return true;
        }

        // Restores techprintCount, even if vanilla throws
        private static void Finalizer(ResearchProjectDef project, int __state)
        {
            if (project != null && __state != NoRestore)
            {
                project.techprintCount = __state;
            }
        }
    }
}
