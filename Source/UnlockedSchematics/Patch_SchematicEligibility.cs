using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace UnlockedSchematics
{
    /// <summary>
    /// Change 2 - decide schematic eligibility on whether the research is actually
    /// unlocked, rather than on which DLC feature it belongs to.
    ///
    /// This patch pulls in both directions, and both halves matter:
    ///
    ///   WIDER  - vanilla's last clause is "return project.TechprintCount == 0", which
    ///            rejects every techprint project outright, even one whose techprint the
    ///            player has already applied. Zeroing the field for the duration of the
    ///            call lets such a project through once it is genuinely unlocked.
    ///
    ///   TIGHTER- vanilla checks techprints but never checks the other three unlock
    ///            gates, so it will happily generate a book for research the player
    ///            cannot start. That is visible in vanilla: WastepackAtomizer requires a
    ///            NanostructuringChip to be analysed, and schematics for it spawn anyway.
    ///            Removing the exclude list (Change 1) would multiply that problem - a
    ///            colony that never takes a mechanitor would see mechtech schematics
    ///            forever, crowding out books it could actually use.
    ///
    /// Net effect: a schematic can be about any project whose unlock requirements are
    /// met, and about no project whose are not. Nothing is gated by which DLC it came
    /// from any more.
    ///
    /// The gate check runs in the prefix, before the field is zeroed, because
    /// TechprintRequirementMet reads the very field this patch temporarily clears -
    /// checking it afterwards would always report "met".
    ///
    /// Note: ReadingOutcomeDoerGainAnomalyResearch inherits IsValid without overriding
    /// it, so this patch applies to the Anomaly book too. Harmless - no Anomaly project
    /// has techprints - but stated here so it is not rediscovered as a surprise later.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_SchematicEligibility
    {
        /// <summary>__state sentinel meaning "the prefix never touched techprintCount".</summary>
        private const int NoRestore = -1;

        // IsValid is private, so it cannot be named with nameof.
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(ReadingOutcomeDoerGainResearch), "IsValid");
        }

        /// <summary>
        /// True when every second unlock step on the project has been satisfied. These are
        /// the four gates ResearchProjectDef.CanStartNow consults; the rest of what
        /// CanStartNow tests (bench availability, prerequisites, IsFinished) is either
        /// already handled by the caller or is a passing situation rather than an unlock.
        /// </summary>
        private static bool Unlocked(ResearchProjectDef project)
        {
            return project.TechprintRequirementMet          // Royalty techprints applied
                && project.PlayerMechanitorRequirementMet   // Biotech mechanitor in colony
                && project.AnalyzedThingsRequirementsMet    // required chip studied
                && project.InspectionRequirementsMet;       // Odyssey grav engine inspected
        }

        private static bool Prefix(ResearchProjectDef project, ref bool __result, out int __state)
        {
            __state = NoRestore;

            if (project == null)
            {
                return true; // Let vanilla deal with it.
            }

            if (!Unlocked(project))
            {
                __result = false;
                return false; // Still locked - not a valid schematic subject. Skip the original.
            }

            __state = project.techprintCount;
            project.techprintCount = 0;
            return true;
        }

        /// <summary>
        /// A Finalizer rather than a Postfix: a Postfix is skipped when the original throws,
        /// which would leave the def permanently zeroed. Taking no __exception parameter and
        /// returning void means exceptions still propagate untouched. Finalizers also run
        /// when a prefix skipped the original, hence the NoRestore sentinel.
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
