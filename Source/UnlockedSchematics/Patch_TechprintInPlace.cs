using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace UnlockedSchematics
{
    /// <summary>
    /// Lets a colony with no research bench apply techprints.
    ///
    /// Vanilla's CompTechprint.CompFloatMenuOptions builds an ApplyTechprint job only if it
    /// finds a bench; with none, the menu option still appears but clicking it just prints
    /// "MessageNoResearchBenchForTechprint". A research-free colony therefore has to build
    /// and keep a bench for this one interaction.
    ///
    /// This prefix runs the same bench query vanilla runs. If it finds a bench, vanilla is
    /// left to run untouched - nothing changes for a colony that has one. Only when there is
    /// no bench does it substitute an option that queues the in-place job instead.
    /// </summary>
    [HarmonyPatch(typeof(CompTechprint), nameof(CompTechprint.CompFloatMenuOptions))]
    public static class Patch_TechprintInPlace
    {
        private static bool Prefix(CompTechprint __instance, Pawn selPawn, ref IEnumerable<FloatMenuOption> __result)
        {
            // Vanilla opens with ModLister.CheckRoyalty, which logs if Royalty is missing.
            // Read the flag directly so this check cannot produce that message itself.
            if (!ModLister.RoyaltyInstalled)
            {
                return true;
            }

            if (selPawn == null || selPawn.Map == null || FindBench(selPawn) != null)
            {
                return true; // A bench is available: vanilla behaviour, unmodified.
            }

            __result = InPlaceOptions(__instance, selPawn);
            return false;
        }

        /// <summary>
        /// The same query CompTechprint.CompFloatMenuOptions uses to find a bench. Kept
        /// identical so the fallback triggers on exactly the cases vanilla would refuse -
        /// no bench built, or none reachable, reservable and unforbidden for this pawn.
        /// Note this ignores power: JobDriver_ApplyTechprint never checks it either, so an
        /// unpowered hi-tech bench still counts as available, as in vanilla.
        /// </summary>
        private static Thing FindBench(Pawn selPawn)
        {
            return GenClosest.ClosestThingReachable(
                selPawn.Position,
                selPawn.Map,
                ThingRequest.ForGroup(ThingRequestGroup.ResearchBench),
                PathEndMode.InteractionCell,
                TraverseParms.For(selPawn, Danger.Some),
                9999f,
                (Thing t) => t is Building_ResearchBench && !t.IsForbidden(selPawn) && selPawn.CanReserve(t));
        }

        /// <summary>
        /// Mirrors vanilla's option-building, minus the bench. The eligibility checks and the
        /// label are vanilla's own, so a pawn who cannot do the work is refused for the same
        /// reason and in the same words as before.
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
