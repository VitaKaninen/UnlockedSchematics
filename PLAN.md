# Unlocked Schematics — Implementation Plan

Supersedes the open questions in `Schematics Unlocked-brief.md`. Findings below are read
from decompiled `Assembly-CSharp.dll`, **RimWorld 1.6.4871 rev590**, plus the shipped def
XML for Core + Royalty + Ideology + Biotech + Anomaly + Odyssey.

---

## 1. Findings — the brief's questions, answered

### Q1. Where is the filter?

`RimWorld.ReadingOutcomeDoerGainResearch.IsValid(ResearchProjectDef)` — a **private
instance method**. (The brief guessed at a `BookOutcomeDoer*` name; the doer class is
actually `ReadingOutcomeDoer*`, wired up by
`BookOutcomeProperties_GainResearch.DoerClass`.)

```csharp
private bool IsValid(ResearchProjectDef project)
{
    if (project.generalRules == null) return false;                    // (a)
    if (Props.exclude.Count > 0 && Props.exclude.ContainsAny(...))     // (b)
        return false;
    if ((!Props.ignoreZeroBaseCost || project.baseCost != 0f)          // (c)
        && (Props.tabs.NullOrEmpty() || Props.tabs.Exists(...)))       // (d)
        return project.TechprintCount == 0;                            // (e)
    return false;
}
```

### Q2. How many call sites?

**One.** `IsValid` is private and called only from
`ReadingOutcomeDoerGainResearch.OnBookGenerated`, which is reached from exactly one place
in the whole assembly: `Verse.Book.AppendDoerRules` (`Book.cs:262`), called by
`Book.GenerateBook`.

Every book source funnels through it:

```
trader stock / quest reward / map-gen loot / bookcase spawn
    -> BookUtility.MakeBook(...)
    -> Book.GenerateBook()
    -> AppendDoerRules()
    -> doer.OnBookGenerated()
    -> IsValid()
```

So a single patch covers all generation paths. The brief's worry about scattered
per-path filters does not apply.

### Q3. What does the check read?

Five conditions, only one of which is a gameplay gate:

| # | Condition | Where it lives | Blocks what |
|---|---|---|---|
| a | `generalRules == null` | def data | nothing in vanilla (164/164 defs have it); some modded research |
| b | `Props.exclude` contains the project | **`Schematic` ThingDef XML** | 4 Mechtech + 3 Gravtech |
| c | `ignoreZeroBaseCost` (default `true`) && `baseCost == 0` | props default | 40 Anomaly projects |
| d | project's `tab` not in `Props.tabs` (`Main` only) | **`Schematic` ThingDef XML** | Anomaly-tab projects |
| e | `project.TechprintCount != 0` | **hardcoded** | 13 Royalty projects |

Only **(e)** is hardcoded. **(b)** and **(d)** are plain def data on the `Schematic`
ThingDef in `Core/Defs/Books/BookDefs.xml`.

### Q4. Is the mechanitor gate the same code path?

**No — and it is not a code gate at all.** `IsValid` never reads `requiresMechanitor`.
Mechtech projects are blocked by an explicit XML exclude list:

```xml
<!-- Core/Defs/Books/BookDefs.xml, ThingDef Schematic -->
<li Class="BookOutcomeProperties_GainResearch">
  <tabs><Main /></tabs>
  <exclude>
    <BasicGravtech    MayRequire="Ludeon.RimWorld.Odyssey"/>
    <StandardGravtech MayRequire="Ludeon.RimWorld.Odyssey"/>
    <AdvancedGravtech MayRequire="Ludeon.RimWorld.Odyssey"/>
    <BasicMechtech    MayRequire="Ludeon.RimWorld.Biotech"/>
    <StandardMechtech MayRequire="Ludeon.RimWorld.Biotech"/>
    <HighMechtech     MayRequire="Ludeon.RimWorld.Biotech"/>
    <UltraMechtech    MayRequire="Ludeon.RimWorld.Biotech"/>
  </exclude>
</li>
```

The wiki's phrasing ("not generated for mechanitor-related projects") is right about the
outcome and wrong about the mechanism. It is a hand-written blocklist, not a rule.

### Q5. Is Odyssey grav-engine research gated by this mechanism?

**Yes, by the same blocklist** — the three Gravtech projects are named in it.
`requireGravEngineInspected` is never consulted by `IsValid`.

### Q6. Does the bench-side completion check share the field?

**Yes.** `IsValid` reads `TechprintCount`; the bench gate is
`ResearchProjectDef.CanStartNow`, which reads `TechprintRequirementMet` -> `TechprintCount`
-> the same `techprintCount` field. **No XML patch can separate them**, which settles
XML-vs-Harmony for the techprint case: Harmony.

---

## 2. The finding that changes the design

`ResearchManager.AddProgress` — the method a schematic calls every reading tick — ends
with this:

```csharp
progress[proj] = Mathf.Min(progress[proj], proj.Cost);
if (proj.PrerequisitesCompleted && proj.IsFinished)
    FinishProject(proj, doCompletionDialog: true, source);
```

and `IsFinished` is purely `ProgressReal >= Cost`. It consults **no secondary gate** —
not techprints, not mechanitor, not grav-engine inspection.

**Consequence: unblocking generation alone breaks the gate.** A colonist reading a
Cataphract Armor schematic long enough fills the bar to 100%, `AddProgress` calls
`FinishProject`, and the research completes with zero techprints applied. That is exactly
the outcome the brief says the mod must not produce.

This applies to the XML half too — remove the exclude entries and a schematic can complete
`BasicMechtech` for a colony with no mechanitor. **So there is no viable XML-only version
of this mod.** Harmony is required either way.

Vanilla never hits this because vanilla excludes every gated project from schematics in
the first place. The mod creates the situation and must therefore also close it.

Worth noting *why* clamping is the fix rather than suppressing `FinishProject`: recipes
and buildings test `researchPrerequisite.IsFinished`, which is progress-based. If progress
reaches `Cost` the tech is effectively unlocked whether or not `FinishProject` ran. So
progress must be held **strictly below** `Cost`.

The bench path is safe to leave alone: `ResearchManager.ResearchPerformed` writes
`progress[currentProj]` directly and never calls `AddProgress`. Patching `AddProgress`
cannot affect normal bench research.

---

## 3. What the mod does — three changes

### Change 1 — XML: drop the exclude list

`Patches/UnlockedSchematics_SchematicExcludes.xml`

```xml
<?xml version="1.0" encoding="utf-8" ?>
<Patch>
  <Operation Class="PatchOperationRemove">
    <xpath>/Defs/ThingDef[defName="Schematic"]/comps/li[@Class="CompProperties_Book"]/doers/li[@Class="BookOutcomeProperties_GainResearch"]/exclude</xpath>
  </Operation>
</Patch>
```

Removes the whole `<exclude>` node, so mod-added excludes go too — consistent with the
mod's purpose. Unblocks 7 projects.

### Change 2 — Harmony: neutralise clause (e) in `IsValid`

Target: `AccessTools.Method(typeof(ReadingOutcomeDoerGainResearch), "IsValid")`

Rather than re-implementing the method or transpiling the IL, temporarily zero the field
the clause reads, run vanilla unchanged, and restore:

```csharp
static void Prefix(ResearchProjectDef project, out int __state)
{
    __state = project.techprintCount;
    project.techprintCount = 0;
}

// Finalizer, not Postfix — a Postfix is skipped if the original throws.
static void Finalizer(ResearchProjectDef project, int __state)
{
    project.techprintCount = __state;
}
```

Every other condition (a–d) is still evaluated exactly as vanilla evaluates it, so if
Ludeon adds a sixth condition in 1.7 we inherit it instead of silently ignoring it.
Book generation is single-threaded and the window is one method call, so the mutation is
not observable elsewhere.

Unblocks the 13 Royalty projects and any modded techprint research (this is where most of
the real-world value is — Vanilla Expanded and friends add a lot of techprint-gated
research).

**Note:** `ReadingOutcomeDoerGainAnomalyResearch` inherits `IsValid` without overriding
it, so this patch also applies to the Anomaly book. Harmless — no Anomaly project has
techprints — but it should be stated in a code comment so it isn't rediscovered later.

### Change 3 — Harmony: hold gated projects below completion

Target: `ResearchManager.AddProgress(ResearchProjectDef, float, Pawn)`

```csharp
static void Prefix(ResearchProjectDef proj, ref float amount)
{
    if (proj.TechprintRequirementMet
        && proj.PlayerMechanitorRequirementMet
        && proj.AnalyzedThingsRequirementsMet
        && proj.InspectionRequirementsMet)
        return;                                   // no gate outstanding, vanilla behaviour

    float cap     = proj.Cost - Epsilon;          // Epsilon scaled off Cost, see below
    float current = Find.ResearchManager.GetProgress(proj);
    amount = Mathf.Max(0f, Mathf.Min(amount, cap - current));
}
```

All four gates, not just techprints — Change 1 unblocks Mechtech (`requiresMechanitor`,
`requiredAnalyzed`) and Gravtech (`requireGravEngineInspected`), so those need the same
protection.

`Epsilon`: a fixed `0.01f` is fine for vanilla costs (500–4000) but gets lost in float
precision on very large modded costs. Use `Mathf.Max(0.01f, proj.Cost * 0.001f)` so the
bar parks at ~99.9% and the remainder is always representable. The player applies the
techprint, selects the project, and one tick at the bench finishes it.

---

## 4. Scope — exactly what changes in vanilla

**20 projects unblocked:**

- 13 Royalty (techprint): CataphractArmor, JumpPack, BrainWiring, SpecializedLimbs,
  CompactWeaponry, VenomSynthesis, ArtificialMetabolism, NeuralComputation, SkinHardening,
  HealingFactors, FleshShaping, MolecularAnalysis, CircadianInfluence
- 4 Biotech (exclude list): BasicMechtech, StandardMechtech, HighMechtech, UltraMechtech
- 3 Odyssey (exclude list): BasicGravtech, StandardGravtech, AdvancedGravtech

All 20 declare no `<tab>`, so they default to `Main` and pass condition (d). All 20 have
`generalRules`, so they pass (a). All have `baseCost > 0`, so they pass (c).

**Deliberately out of scope:**

- The 40 zero-`baseCost` Anomaly projects. They are excluded by conditions (c) and (d),
  but that is by design — Anomaly ships its own book ThingDef using
  `BookOutcomeProperties_GainAnomalyResearch`, which grants *knowledge*, not research
  points. Nothing is missing there.
- Modded research with no `generalRules`. Condition (a) is unpatchable in any useful
  sense: the RulePack is what generates the book's title and description, so a book about
  such a project could not be named. Document as a known limitation.

---

## 5. Build order

### Phase 0 — rename and repo setup — **done 2026-08-26**

1. Folder renamed `Schematics Unlocked` -> `UnlockedSchematics`. The rename could not be
   done from inside a live session (`Device or resource busy` — the app holds the dir as
   its cwd, and a background retry job dies with the app), so it was done by hand after
   closing Claude. The four state stores keyed to the old path were then migrated with
   `~/.claude/bin/migrate_project_path.py`; procedure in `~/.claude/docs/SESSIONS.md`.
2. `git init` + `.gitignore` (`.claude/`, `obj/`, `bin/`).
3. No GitHub remote — the folder rules say to ask first, and it has not been asked yet.

### Phase 1 — skeleton

```
UnlockedSchematics/
  About/
    About.xml            packageId, Harmony dependency, supportedVersions 1.6
    Preview.png
  Common/
    Patches/
      UnlockedSchematics_SchematicExcludes.xml
    Assemblies/
      UnlockedSchematics.dll
  Source/
    UnlockedSchematics/
      UnlockedSchematics.csproj
      Mod.cs                 Harmony bootstrap
      Patch_SchematicEligibility.cs
      Patch_ResearchGateClamp.cs
  Dev/
    README.md
    TestHarness/           separate throwaway mod, forces schematic subjects for testing
  LoadFolders.xml
  .rimignore
  .gitignore
  CLAUDE.md
  PLAN.md
  UnlockedSchematics-brief.md
```

Two things to settle by copying a sibling mod rather than inventing (both live outside
this folder, so they need your go-ahead to read):

- `About.xml` `packageId` convention — match `CarryCapacityFromBionics` / `DontTrashThis`.
- `.rimignore` — the brief points at `CarryCapacityFromBionics` as the good template.
  Must additionally list `PLAN.md`, `*-brief.md`, and `Source/`.

On `LoadFolders.xml`: the brief flags a disputed claim that Patches are processed before
Defs within one entry. It does not bite us — this mod ships no Defs, only one Patch and
one assembly, so ordering within an entry is moot. Not worth resolving now.

Build target: the game assembly carries no `TargetFrameworkAttribute` (Unity does not emit
one) but `Managed/` contains `netstandard.dll`, i.e. the .NET 4.x profile. `net472` via
the `Krafs.Rimworld.Ref` + `Lib.Harmony` NuGet packages is the community standard for
1.5/1.6 — confirm on first build rather than taking it on trust.

### Phase 2 — Change 1, verify in isolation — **built, awaiting in-game test**

XML only, no assembly yet, so a failure here is unambiguously the def half. Installed to
`RimWorld/Mods/UnlockedSchematics`; `Dev/TestHarness` installed alongside as
`UnlockedSchematics_TESTONLY` to make the result binary rather than a lucky sample.

The xpath was checked against the shipped `Core/Defs/Books/BookDefs.xml`: one match, seven
children. That only proves the patch targets the right node — it does not prove RimWorld
applies it, which is what the in-game test is for.

Test: dev mode -> debug output -> Text generation -> Books -> Schematic (generates 30 and
logs them). With the harness on, every result should be a mechtech or gravtech subject.
For an exact project name, spawn a Schematic and open its info card.

### Phase 3 — Changes 2 and 3

Both patches in one Harmony class. Change 3 is not optional and must land in the same
build as Change 2, otherwise there is a build in which the gate is bypassable.

### Phase 4 — testing (see below)

### Phase 5 — publish

Steam Workshop under `VitaKaninen`. Commit and push per the folder rules.

---

## 6. Test plan

Dev mode, Royalty + Biotech + Odyssey enabled.

| # | Test | Expected |
|---|---|---|
| 1 | Spawn ~30 schematics, tally subjects | Techprint, Mechtech and Gravtech projects all appear |
| 2 | Read a CataphractArmor schematic to 100% | Progress parks at ~99.9%; **no completion letter**; not researchable at the bench |
| 3 | Then apply 2 techprints | Project becomes selectable; a short bench session completes it |
| 4 | Read a BasicMechtech schematic with no mechanitor in the colony | Parks below 100%; stays locked |
| 5 | Read a BasicGravtech schematic without a grav engine inspected | Parks below 100%; stays locked |
| 6 | Normal bench research on an ungated project | Completes exactly as vanilla — proves the `AddProgress` clamp does not touch `ResearchPerformed` |
| 7 | Read a schematic for an ordinary project | Unchanged from vanilla |
| 8 | Load a save made before the mod | No errors, no progress reset |
| 9 | Research tab UI on a 99.9% gated project | Check the progress bar and lock reason render sensibly — untested code path in vanilla |

Test 6 is the regression guard for the riskiest patch. Test 9 is the one most likely to
surface something cosmetic and unexpected.

---

## 7. Decisions still open

1. **XML vs. runtime for Change 1.** The plan uses a `PatchOperationRemove`. The
   alternative is clearing `Props.exclude` in a `StaticConstructorOnStartup`, which would
   let all three changes sit behind mod settings toggles (per-gate: techprints /
   mechanitor / gravtech). Recommendation: XML for 1.0 — simpler, visible to other mods,
   no settings UI to maintain. Revisit if anyone asks for toggles.
2. **Do techprint-gated projects deserve reduced schematic weight?** Right now an
   unblocked project is exactly as likely as any other. Arguably a Cataphract schematic
   should be rarer than a Stonecutting one. Out of scope for 1.0; noted so it is a choice
   rather than an oversight.
3. **`packageId` and `.rimignore`** — pending a read of a sibling mod.

---

## 8. Reproducing the source investigation

The decompile lives in this session's scratchpad, not in the repo — decompiled Ludeon code
must not be committed to a public GitHub repo.

```bash
ilspycmd -p -o <outdir> "C:/Program Files (x86)/Steam/steamapps/common/RimWorld/RimWorldWin64_Data/Managed/Assembly-CSharp.dll"
```

Files that matter:

- `RimWorld/ReadingOutcomeDoerGainResearch.cs` — `IsValid`, `OnBookGenerated`, `OnReadingTick`
- `RimWorld/BookOutcomeProperties_GainResearch.cs` — `include` / `exclude` / `tabs` / `ignoreZeroBaseCost`
- `RimWorld/ResearchManager.cs` — `AddProgress` (the hazard), `ResearchPerformed` (the safe bench path)
- `Verse/ResearchProjectDef.cs` — `CanStartNow`, `IsFinished`, `Cost`, and the four gate properties
- `Verse/Book.cs:262` — the single call site
- `Core/Defs/Books/BookDefs.xml` — the `Schematic` ThingDef and its exclude list
