using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace UnlockedSchematics
{
    /// <summary>
    /// Shared logic for the analyze-in-place patches below.
    ///
    /// Vanilla already supports analysing an item where it lies - CompProperties_Analyzable
    /// carries a canStudyInPlace flag, and Anomaly ships three things that set it. It is just
    /// never set on the Biotech chips (SignalChip, PowerfocusChip, NanostructuringChip), so
    /// studying one needs a powered, reachable research bench. These patches turn that flag on
    /// for the duration of the calls that read it, and only when the colony has no bench to
    /// use, so a research-free colony is not forced to keep a bench for this one interaction.
    ///
    /// Flipping shared def data is safe here for the same reason it is in
    /// Patch_SchematicEligibility: the window is a single synchronous call on the main thread,
    /// and a Finalizer restores the field even if the original throws.
    /// </summary>
    internal static class AnalyzeInPlace
    {
        /// <summary>
        /// Whether the bench check vanilla is about to make will succeed. Mirrors
        /// CompAnalyzable.CanInteract exactly, including its split: a named pawn gets the full
        /// reachable-reservable-powered query, while the no-pawn case (the gizmo, drawn every
        /// frame) gets the cheap map-wide lookup. Matching that split means these patches add
        /// no per-frame cost vanilla was not already paying.
        /// </summary>
        internal static bool BenchAvailable(CompAnalyzable comp, Pawn activateBy)
        {
            if (activateBy != null)
            {
                return StudyUtility.TryFindResearchBench(activateBy, out var _);
            }

            Map map = comp.parent.MapHeld;
            return map != null && map.listerBuildings.ColonistsHaveResearchBench();
        }

        /// <summary>Sets canStudyInPlace if there is no bench. Returns true if it did.</summary>
        internal static bool TryAllowInPlace(CompAnalyzable comp, Pawn activateBy)
        {
            CompProperties_Analyzable props = comp.Props;
            if (props.canStudyInPlace || BenchAvailable(comp, activateBy))
            {
                return false;
            }

            props.canStudyInPlace = true;
            return true;
        }

        internal static void Restore(CompAnalyzable comp, bool didSet)
        {
            if (didSet)
            {
                comp.Props.canStudyInPlace = false;
            }
        }
    }

    /// <summary>
    /// Stops CanInteract reporting "NoResearchBench" when there is no bench to be had, which
    /// is what greys out the float-menu option and the gizmo. Every other rejection this
    /// method can produce - already analyzed, incapable, on cooldown, and the mechanitor
    /// requirement in CompAnalyzableUnlockResearch - is untouched.
    /// </summary>
    [HarmonyPatch(typeof(CompAnalyzable), nameof(CompAnalyzable.CanInteract))]
    public static class Patch_AnalyzableCanInteract
    {
        private static void Prefix(CompAnalyzable __instance, Pawn activateBy, out bool __state)
        {
            __state = AnalyzeInPlace.TryAllowInPlace(__instance, activateBy);
        }

        private static void Finalizer(CompAnalyzable __instance, bool __state)
        {
            AnalyzeInPlace.Restore(__instance, __state);
        }
    }

    /// <summary>
    /// Makes the job that gets queued a bench-free one. OrderForceTarget reads
    /// canStudyInPlace to decide whether to look up a bench and put it in targetB; with the
    /// flag on, targetB is left empty and the pawn works where the item lies.
    /// </summary>
    [HarmonyPatch(typeof(CompAnalyzable), nameof(CompAnalyzable.OrderForceTarget))]
    public static class Patch_AnalyzableOrderForceTarget
    {
        private static void Prefix(CompAnalyzable __instance, LocalTargetInfo target, out bool __state)
        {
            __state = target.Pawn != null && AnalyzeInPlace.TryAllowInPlace(__instance, target.Pawn);
        }

        private static void Finalizer(CompAnalyzable __instance, bool __state)
        {
            AnalyzeInPlace.Restore(__instance, __state);
        }
    }

    /// <summary>
    /// The job driver reads canStudyInPlace again when the job actually starts, by which time
    /// the flag is back to false - so without this the pawn would try to reserve an empty
    /// targetB and the job would fail. Reading it off the job instead makes the decision
    /// per-job rather than per-def: no bench in the job means study in place.
    ///
    /// This cannot affect vanilla jobs. CompAnalyzable.OrderForceTarget is the only thing in
    /// the assembly that creates an AnalyzeItem job, and it never leaves targetB empty unless
    /// canStudyInPlace is set - in which case the original getter already returns true.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_StudyItemInPlace
    {
        // CanStudyInPlace is a protected property, so it cannot be reached with nameof.
        private static MethodBase TargetMethod()
        {
            return AccessTools.PropertyGetter(typeof(JobDriver_StudyItem), "CanStudyInPlace");
        }

        private static void Postfix(JobDriver __instance, ref bool __result)
        {
            if (!__result && __instance.job != null && !__instance.job.GetTarget(TargetIndex.B).IsValid)
            {
                __result = true;
            }
        }
    }
}
