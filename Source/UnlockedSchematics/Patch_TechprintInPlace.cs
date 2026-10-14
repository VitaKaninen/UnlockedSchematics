using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace UnlockedSchematics
{
    // Applies techprints where they lie instead of at a research bench
    [HarmonyPatch(typeof(CompTechprint), nameof(CompTechprint.CompFloatMenuOptions))]
    public static class Patch_TechprintInPlace
    {
        private static bool Prefix(CompTechprint __instance, Pawn selPawn, ref IEnumerable<FloatMenuOption> __result)
        {
            // No Royalty: leave it to vanilla
            if (!ModLister.RoyaltyInstalled || selPawn == null || selPawn.Map == null)
            {
                return true;
            }

            __result = InPlaceOptions(__instance, selPawn);
            return false;
        }

        // Vanilla's checks and messages, minus the bench
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

            // Kept for its forbidden-item message
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
