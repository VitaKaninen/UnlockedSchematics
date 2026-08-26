using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace UnlockedSchematics
{
    /// <summary>
    /// Makes schematic eligibility depend on unlock state: a project qualifies once every
    /// unlock requirement is satisfied. Vanilla instead rejects any project carrying a
    /// techprint requirement and ignores the other requirements. The Anomaly book inherits
    /// IsValid unchanged, so it is covered too.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_SchematicEligibility
    {
        /// <summary>__state value meaning "the prefix did not touch techprintCount".</summary>
        private const int NoRestore = -1;

        // IsValid is private, so it cannot be reached with nameof.
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(ReadingOutcomeDoerGainResearch), "IsValid");
        }

        /// <summary>
        /// The four unlock gates ResearchProjectDef.CanStartNow consults. Its other tests -
        /// prerequisites, IsFinished, bench availability - are not unlock state.
        /// </summary>
        private static bool Unlocked(ResearchProjectDef project)
        {
            return project.TechprintRequirementMet          // Royalty techprints applied
                && project.PlayerMechanitorRequirementMet   // Biotech mechanitor in colony
                && project.AnalyzedThingsRequirementsMet    // required item studied
                && project.InspectionRequirementsMet;       // Odyssey grav engine inspected
        }

        /// <summary>
        /// Rejects locked projects, then clears techprintCount for the duration of the call so
        /// vanilla's final "TechprintCount == 0" clause accepts an unlocked project whose
        /// techprint has been applied. Vanilla's earlier conditions still decide the rest.
        /// </summary>
        private static bool Prefix(ResearchProjectDef project, ref bool __result, out int __state)
        {
            __state = NoRestore;

            if (project == null)
            {
                return true; // Let vanilla handle it.
            }

            // Must run before the field is cleared below - TechprintRequirementMet reads it,
            // and would report the requirement met once it is zero.
            if (!Unlocked(project))
            {
                __result = false;
                return false; // Locked, so not a valid subject.
            }

            __state = project.techprintCount;
            project.techprintCount = 0;
            return true;
        }

        /// <summary>
        /// Restores techprintCount. A Finalizer rather than a Postfix so it still runs if the
        /// original throws, which would otherwise leave the def permanently zeroed; taking no
        /// __exception parameter leaves exceptions to propagate untouched.
        /// </summary>
        private static void Finalizer(ResearchProjectDef project, int __state)
        {
            if (project != null && __state != NoRestore)
            {
                project.techprintCount = __state;
            }
        }
    }
}
