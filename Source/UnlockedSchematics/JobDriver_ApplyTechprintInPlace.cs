using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace UnlockedSchematics
{
    /// <summary>
    /// Applies a techprint where it lies, with no research bench involved.
    ///
    /// Vanilla's JobDriver_ApplyTechprint cannot be reused: it casts targetA to
    /// Building_ResearchBench and its toils haul the techprint to that bench's interaction
    /// cell. This driver keeps everything else the same - same 600 tick duration, same
    /// ResearchManager.ApplyTechprint call, same sound - so the only difference the player
    /// sees is the missing walk. The intellectual XP comes from ApplyTechprint itself, which
    /// is why no activeSkill is set here; vanilla's driver does not set one either.
    /// </summary>
    public class JobDriver_ApplyTechprintInPlace : JobDriver
    {
        private const TargetIndex TechprintInd = TargetIndex.A;

        /// <summary>Matches JobDriver_ApplyTechprint.Duration.</summary>
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
