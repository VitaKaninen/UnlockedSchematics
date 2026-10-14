using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace UnlockedSchematics
{
    // Applies a techprint where it lies; vanilla's job needs a research bench
    public class JobDriver_ApplyTechprintInPlace : JobDriver
    {
        private const TargetIndex TechprintInd = TargetIndex.A;

        // Same as vanilla
        private const int Duration = 600;

        private Thing Techprint => job.GetTarget(TechprintInd).Thing;

        private CompTechprint TechprintComp => Techprint?.TryGetComp<CompTechprint>();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Techprint, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TechprintInd);

            yield return Toils_Goto.GotoThing(TechprintInd, PathEndMode.ClosestTouch)
                .FailOnDespawnedNullOrForbidden(TechprintInd)
                .FailOnSomeonePhysicallyInteracting(TechprintInd);

            yield return Toils_General.Wait(Duration, TechprintInd)
                .FailOnDestroyedNullOrForbidden(TechprintInd)
                .FailOnCannotTouch(TechprintInd, PathEndMode.ClosestTouch)
                .WithProgressBarToilDelay(TechprintInd);

            Toil apply = ToilMaker.MakeToil("ApplyTechprintInPlace");
            apply.initAction = delegate
            {
                CompTechprint comp = TechprintComp;
                if (comp == null)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                Find.ResearchManager.ApplyTechprint(comp.Props.project, pawn);
                Techprint.Destroy();
                SoundDefOf.TechprintApplied.PlayOneShotOnCamera();
            };
            apply.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return apply;
        }
    }
}
