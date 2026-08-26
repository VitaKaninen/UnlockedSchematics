using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace UnlockedSchematics
{
    /// <summary>
    /// Change 3 - keep a gated project's banked progress strictly below its cost.
    ///
    /// This is the half that preserves the gate, and it is not optional. ResearchManager
    /// .AddProgress ends with:
    ///
    ///     progress[proj] = Mathf.Min(progress[proj], proj.Cost);
    ///     if (proj.PrerequisitesCompleted &amp;&amp; proj.IsFinished)
    ///         FinishProject(proj, doCompletionDialog: true, source);
    ///
    /// and IsFinished is purely "ProgressReal >= Cost" - it consults no secondary gate.
    /// So without this patch a colonist reading a Cataphract Armor schematic to 100% would
    /// complete the project with zero techprints applied. Suppressing the FinishProject call
    /// would not be enough either: recipes and buildings test researchPrerequisite.IsFinished,
    /// which is progress-based, so the tech would be effectively unlocked regardless.
    /// Progress has to be held *below* Cost.
    ///
    /// All four gates are checked, not just techprints: the def patch unblocks Mechtech
    /// (requiresMechanitor, requiredAnalyzed) and Gravtech (requireGravEngineInspected) too.
    ///
    /// Scope: in RimWorld 1.6 this method has exactly one caller in the whole assembly -
    /// ReadingOutcomeDoerGainResearch.OnReadingTick. Bench research runs through
    /// ResearchManager.ResearchPerformed, which writes progress[currentProj] directly and
    /// never calls AddProgress, so normal research cannot be affected by this patch.
    /// </summary>
    [HarmonyPatch(typeof(ResearchManager), nameof(ResearchManager.AddProgress))]
    [HarmonyPatch(new[] { typeof(ResearchProjectDef), typeof(float), typeof(Pawn) })]
    public static class Patch_ResearchGateClamp
    {
        private static void Prefix(ResearchManager __instance, ResearchProjectDef proj, ref float amount)
        {
            // Never interfere with a no-op or with a mod deliberately removing progress.
            if (proj == null || amount <= 0f)
            {
                return;
            }

            if (proj.TechprintRequirementMet
                && proj.PlayerMechanitorRequirementMet
                && proj.AnalyzedThingsRequirementsMet
                && proj.InspectionRequirementsMet)
            {
                return; // No gate outstanding - vanilla behaviour.
            }

            float cost = proj.Cost;
            if (cost <= 0f)
            {
                return;
            }

            // A flat epsilon is fine for vanilla costs (500-4000) but gets swallowed by float
            // precision on very large modded ones, which would let the bar reach Cost exactly.
            // Scaling it parks the bar at ~99.9% and keeps the remainder representable.
            float epsilon = Mathf.Max(0.01f, cost * 0.001f);
            float headroom = (cost - epsilon) - __instance.GetProgress(proj);

            // Never negative: progress already at or past the cap (an old save, say) is left
            // alone rather than rolled back.
            amount = Mathf.Clamp(amount, 0f, Mathf.Max(0f, headroom));
        }
    }
}
