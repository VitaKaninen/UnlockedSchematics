using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace UnlockedSchematics
{
    /// <summary>
    /// Chooses which research projects a schematic may be written about.
    ///
    /// Vanilla answers this by feature rather than by state: ReadingOutcomeDoerGainResearch
    /// .IsValid rejects any project carrying a techprint requirement, applied or not, and
    /// ignores the other unlock requirements entirely - so it will also offer a book for
    /// research the player has no way to start. This patch makes the answer depend on
    /// unlock state:
    ///
    ///     eligible        every unlock requirement is satisfied, techprints included
    ///     not eligible    any unlock requirement is still outstanding
    ///
    /// Removing the &lt;exclude&gt; blocklist (see Patches/) is what lets mechtech and
    /// gravtech projects reach this check at all; this patch decides whether they pass it.
    ///
    /// Also applies to the Anomaly book, since ReadingOutcomeDoerGainAnomalyResearch
    /// inherits IsValid without overriding it. No Anomaly project uses techprints, so only
    /// the unlock-requirement half has any effect there.
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
        /// Whether every unlock requirement on the project is satisfied. These are the four
        /// gates ResearchProjectDef.CanStartNow consults for unlock state. Its remaining
        /// tests - prerequisites, IsFinished, bench availability - are either applied by the
        /// caller already or describe a passing situation rather than an unlock.
        /// </summary>
        private static bool Unlocked(ResearchProjectDef project)
        {
            return project.TechprintRequirementMet          // Royalty techprints applied
                && project.PlayerMechanitorRequirementMet   // Biotech mechanitor in colony
                && project.AnalyzedThingsRequirementsMet    // required item studied
                && project.InspectionRequirementsMet;       // Odyssey grav engine inspected
        }

        /// <summary>
        /// Rejects locked projects outright, and clears techprintCount for the duration of
        /// the call so that vanilla's own final clause - "return TechprintCount == 0" -
        /// accepts an unlocked project whose techprint has been applied. Conditions vanilla
        /// checks before that clause still decide the rest.
        /// </summary>
        private static bool Prefix(ResearchProjectDef project, ref bool __result, out int __state)
        {
            __state = NoRestore;

            if (project == null)
            {
                return true; // Let vanilla handle it.
            }

            // Order matters: Unlocked() reads TechprintRequirementMet, which reads the field
            // cleared below. Testing it afterwards would always report the requirement met.
            if (!Unlocked(project))
            {
                __result = false;
                return false; // Locked, so not a valid subject. Skip the original.
            }

            __state = project.techprintCount;
            project.techprintCount = 0;
            return true;
        }

        /// <summary>
        /// Restores techprintCount. A Finalizer rather than a Postfix because a Postfix is
        /// skipped when the original throws, which would leave the def permanently zeroed.
        /// Declaring no __exception parameter and returning void leaves exceptions to
        /// propagate untouched. Finalizers also run when a prefix skipped the original,
        /// which is what NoRestore distinguishes.
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
