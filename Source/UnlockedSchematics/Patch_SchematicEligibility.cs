using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace UnlockedSchematics
{
    /// <summary>
    /// Change 2 - let techprint-gated projects become schematic subjects.
    ///
    /// ReadingOutcomeDoerGainResearch.IsValid ends with "return project.TechprintCount == 0;",
    /// which is the only one of its five conditions that is hardcoded rather than def data.
    /// Rather than re-implementing the method or transpiling its IL, zero the field that
    /// clause reads for the duration of the call and let vanilla run unchanged. Every other
    /// condition is still evaluated exactly as vanilla evaluates it, so a sixth condition
    /// added in a future version is inherited rather than silently dropped.
    ///
    /// The gate itself is untouched: this only affects which projects a schematic can be
    /// *about*. Holding a gated project below completion is Patch_ResearchGateClamp's job,
    /// and the two must ship together.
    ///
    /// Note: ReadingOutcomeDoerGainAnomalyResearch inherits IsValid without overriding it,
    /// so this patch applies to the Anomaly book too. Harmless - no Anomaly project has
    /// techprints - but stated here so it is not rediscovered as a surprise later.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_SchematicEligibility
    {
        // IsValid is private, so it cannot be named with nameof.
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(ReadingOutcomeDoerGainResearch), "IsValid");
        }

        private static void Prefix(ResearchProjectDef project, out int __state)
        {
            __state = project?.techprintCount ?? 0;

            if (project != null)
            {
                project.techprintCount = 0;
            }
        }

        /// <summary>
        /// A Finalizer rather than a Postfix: a Postfix is skipped when the original throws,
        /// which would leave the def permanently zeroed. Taking no __exception parameter and
        /// returning void means exceptions still propagate untouched.
        /// </summary>
        private static void Finalizer(ResearchProjectDef project, int __state)
        {
            if (project != null)
            {
                project.techprintCount = __state;
            }
        }
    }
}
