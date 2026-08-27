using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace UnlockedSchematics
{
    /// <summary>
    /// Applies techprints where they lie, instead of hauling them to a research bench.
    ///
    /// Vanilla's CompTechprint.CompFloatMenuOptions builds an ApplyTechprint job only if it
    /// finds a bench; with none, the menu option still appears but clicking it just prints
    /// "MessageNoResearchBenchForTechprint". A colony that does not research therefore has to
    /// build and keep a bench for this one interaction - and the bench does nothing for it,
    /// since JobDriver_ApplyTechprint is a flat 600 tick wait with no stat read off the bench.
    ///
    /// So this replaces the option outright rather than falling back only when no bench is
    /// found. The chip half of the same feature is plain def data - see
    /// Patches/UnlockedSchematics_AnalyzeInPlace.xml.
    /// </summary>
    [HarmonyPatch(typeof(CompTechprint), nameof(CompTechprint.CompFloatMenuOptions))]
    public static class Patch_TechprintInPlace
    {
        private static bool Prefix(CompTechprint __instance, Pawn selPawn, ref IEnumerable<FloatMenuOption> __result)
        {
            // Vanilla opens with ModLister.CheckRoyalty, which logs if Royalty is missing.
            // Read the flag directly so this check cannot produce that message itself, and
            // leave the no-Royalty case to vanilla.
            if (!ModLister.RoyaltyInstalled || selPawn == null || selPawn.Map == null)
            {
                return true;
            }

            __result = InPlaceOptions(__instance, selPawn);
            return false;
        }

        /// <summary>
        /// Mirrors vanilla's option-building, minus the bench. The eligibility checks and the
        /// label are vanilla's own, so a pawn who cannot do the work is refused for the same
        /// reason and in the same words as before, and no new translation keys are needed.
        /// </summary>
        private static IEnumerable<FloatMenuOption> InPlaceOptions(CompTechprint comp, Pawn selPawn)
        {
            Thing techprint = comp.parent;

            JobFailReason.Clear();
            if (selPawn.WorkTypeIsDisabled(WorkTypeDefOf.Research) || selPawn.WorkTagIsDisabled(WorkTags.Intellectual))
            {
                JobFailReason.Is("WillNever".Translate("Research".TranslateSimple().UncapitalizeFirst()));
            }
            else if (!selPawn.CanReach(techprint, PathEndMode.ClosestTouch, Danger.Some))
            {
                JobFailReason.Is("CannotReach".Translate());
            }

            // Called for its side effect, exactly as vanilla does: it sets JobFailReason for a
            // forbidden or out-of-area techprint. The in-place job does no hauling, but keeping
            // the call keeps the forbidden-item messaging identical to vanilla's.
            HaulAIUtility.PawnCanAutomaticallyHaul(selPawn, techprint, forced: true);

            if (JobFailReason.HaveReason)
            {
                yield return new FloatMenuOption(
                    "CannotGenericWorkCustom".Translate("ApplyTechprint".Translate(techprint.Label)) + ": " + JobFailReason.Reason.CapitalizeFirst(),
                    null);
                JobFailReason.Clear();
                yield break;
            }

            yield return FloatMenuUtility.DecoratePrioritizedTask(
                new FloatMenuOption("ApplyTechprint".Translate(techprint.Label).CapitalizeFirst(), delegate
                {
                    Job job = JobMaker.MakeJob(UnlockedSchematicsDefOf.UnlockedSchematics_ApplyTechprintInPlace, techprint);
                    selPawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                }),
                selPawn,
                techprint);
        }
    }
}
