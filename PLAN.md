# Unlocked Schematics — Implementation Plan

Supersedes the open questions in `UnlockedSchematics-brief.md`. Findings below are read
from decompiled `Assembly-CSharp.dll`, **RimWorld 1.6.4871 rev590**, plus the shipped def
XML for Core + Royalty + Ideology + Biotech + Anomaly + Odyssey.

---

## 0. State of play — start here

**Last updated 2026-08-26.** This file is the handoff; there is no separate handoff doc.

| | |
|---|---|
| Change 1 — XML, drop the exclude list | **done, verified in game** |
| Change 2 — Harmony, unlock-aware eligibility | **done, verified in game** |
| Change 3 — Harmony, gate clamp backstop | built and shipping; its one scenario is untested |

**The mod does what it is meant to do.** Verified in a real save 2026-08-26, harness off:

| Observed | Confirms |
|---|---|
| Everything researched bar the locked lines → only already-researched subjects spawn | Locked research is correctly excluded; the empty pool falls back exactly as §6a predicts |
| Gained a mechanitor → BasicMechtech schematics start appearing | `PlayerMechanitorRequirementMet` gate works |
| Studied a grav engine → BasicGravtech schematics start appearing | `InspectionRequirementsMet` gate works |
| Applied a techprint → that project's schematics start appearing | `TechprintRequirementMet` gate works, and clause (e) is successfully neutralised |

That is test plan §6 rows 1, 2, 4, 5 and 6 passing. It exercises both directions of Change
2 — nothing locked gets through, and each gate opens the moment it is satisfied.

**Still untested:** row 7, the only remaining path into Change 3 — generate a mechtech
schematic, then let the colony's only mechanitor die, then read it. Needs deliberate
setup. Rows 8 (bench-research regression) and 10 (pre-mod save) are also unrun, though row
8 is guarded by code: `AddProgress` has exactly one caller in the whole assembly (§2).

### Cleanup owed before release

- `Dev/TestHarness` is installed as a separate mod at
  `RimWorld/Mods/UnlockedSchematics_TESTONLY`. Disable and delete it before any real
  playthrough — it replaces the candidate pool and rewrites the Schematic label. It has
  done its job (it proved Change 1 applies) and is now actively misleading: leaving it on
  is what produced the unreadable result in §6a.
- `About/Preview.png` does not exist yet. Steam Workshop wants one before publishing.

### Environment notes for the next session

- The decompiled source is **not** in this repo and must not be committed. Regenerate with
  the `ilspycmd` command in `CLAUDE.md`; `Dev/validate_patches.py` takes its path as an
  argument.
- Run `Dev/validate_patches.py` before trusting any patch XML. It catches things an XML
  parser and an XPath test both miss — two bugs this session got past both.

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

**Confirmed 2026-08-26 by grepping the full decompile:** `ResearchManager.AddProgress` has
**exactly one caller in the entire assembly** — `ReadingOutcomeDoerGainResearch
.OnReadingTick`. (The other `AddProgress` hits are `CompSpawnSubplant.AddProgress`, an
unrelated method with the same name.) So in vanilla the clamp patch can only ever fire on
the schematic reading path. Test 6 is still worth running, but the code already proves it.

One more reason the clamp is load-bearing rather than belt-and-braces: `OnReadingTick`
skips a project when `IsProjectVisible` is false, and that reads `Props.usesHiddenProjects`
-> `CanStartNow`, which *does* consult every gate. If the `Schematic` ThingDef set
`usesHiddenProjects`, gated projects would receive no progress at all and Change 3 would be
unnecessary. It does not set it, and the field defaults to `false` — checked in the shipped
`Core/Defs/Books/BookDefs.xml`. So progress really does flow to gated projects.

---

## 2a. `IsValid` checks one gate out of four — a vanilla bug

`IsValid`'s clause (e) tests techprints. It never tests the other three unlock gates, so
**vanilla already generates schematics for research the player cannot start.** The visible
case, spotted in play 2026-08-26:

`WastepackAtomizer` carries `<requiredAnalyzed><li>NanostructuringChip</li></requiredAnalyzed>`.
Until that chip is studied, `AnalyzedThingsRequirementsMet` is false and the project cannot
be researched — yet schematics for it spawn normally. `StandardMechtech`, `HighMechtech`
and `UltraMechtech` carry the same field (`SignalChip`, `PowerfocusChip`,
`NanostructuringChip`); they are only hidden from books by the exclude list, not by any
check on the requirement itself.

So the exclude list was never enforcing a rule. It is a hand-written patch over one symptom
of a filter that asks the wrong question. Change 2 asks the right one instead, which
removes the vanilla case as a side effect — schematics for a chip-locked project stop
generating until the chip is studied.

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

> **Design changed 2026-08-26 — read this before §3.** The original design let schematics
> generate for *gated* research and relied on Change 3 to stop them completing it. Play
> testing killed that: a player who never intends to take a mechanitor would see mechtech
> schematics forever, crowding out books they could actually use. Most players never touch
> mechtech at all.
>
> **The mod now ties generation to whether the research is unlocked, not to which DLC
> feature it belongs to.** No mechanitor, no mechtech schematics. Apply a techprint and
> that project starts generating. This is a *better* fit for the brief's "without weakening
> the gate itself", and it also fixes a vanilla inconsistency for free — see §2a.
>
> Consequences: Change 2 below now checks the four gates as well as zeroing
> `techprintCount`, and Change 3 drops from "the thing that preserves the gate" to a
> backstop for gates that lapse after a book exists.

### Change 2 — Harmony: make `IsValid` ask "is this unlocked?"

Target: `AccessTools.Method(typeof(ReadingOutcomeDoerGainResearch), "IsValid")`

The patch pulls in both directions at once:

- **Wider** — zero `techprintCount` for the duration of the call so clause (e) stops
  rejecting a techprint project whose techprint the player has already applied.
- **Tighter** — reject any project whose unlock gates are not met, which vanilla never
  checks (§2a).

```csharp
static bool Prefix(ResearchProjectDef project, ref bool __result, out int __state)
{
    __state = NoRestore;                      // -1 sentinel
    if (project == null) return true;

    if (!project.TechprintRequirementMet          // techprints applied
        || !project.PlayerMechanitorRequirementMet // mechanitor in colony
        || !project.AnalyzedThingsRequirementsMet  // required chip studied
        || !project.InspectionRequirementsMet)     // grav engine inspected
    {
        __result = false;
        return false;                         // still locked — skip the original
    }

    __state = project.techprintCount;
    project.techprintCount = 0;
    return true;
}

// Finalizer, not Postfix — a Postfix is skipped if the original throws. Finalizers also
// run when a prefix skipped the original, hence the sentinel.
static void Finalizer(ResearchProjectDef project, int __state)
{
    if (project != null && __state != NoRestore) project.techprintCount = __state;
}
```

**The gate check has to run before the field is zeroed.** `TechprintRequirementMet` reads
`TechprintCount` -> `techprintCount`, the very field the prefix clears, so testing it from
a Postfix would always report "met". This is the one ordering constraint in the patch.

Conditions (a)–(d) are still evaluated exactly as vanilla evaluates them, so a sixth
condition added in 1.7 is inherited rather than silently ignored. Book generation is
single-threaded and the window is one method call, so the mutation is not observable
elsewhere.

Covers the 13 Royalty projects and any modded techprint research — Vanilla Expanded and
friends add a lot of it, and that is where most of the real-world value sits.

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

### The pool is smaller than 20 at any given moment

Unblocking a project does not make it a book subject — `OnBookGenerated`'s main loop also
requires `PrerequisitesCompleted && !IsFinished`. Six of the 20 wait on a prerequisite that
is itself gated, so they only become eligible after the one before them is finished:

| Not yet eligible | Waiting on |
|---|---|
| StandardMechtech / HighMechtech / UltraMechtech | the previous mechtech tier |
| StandardGravtech / AdvancedGravtech | the previous gravtech tier |
| VenomSynthesis | CompactWeaponry (itself techprint-gated) |

**Validated against a real save, 2026-08-26.** On a colony with every ungated project
finished bar one, the model predicts exactly one eligible subject with the mod off
(`WastepackAtomizer`) and 15 with it on. The player observed exactly that — one book type
off, the rest on. Reproduce with:

```bash
python Dev/simulate_pool.py "<RimWorld>/Data" --finished-all-ungated --unfinished WastepackAtomizer
```

The `--unfinished` list has to be supplied by hand: whether an ungated project is done
depends on the colony (a missing MultiAnalyzer, or nobody bothering), and nothing in the
defs reveals it.

`WastepackAtomizer` is worth knowing about as a red herring: it lives in
`Biotech/.../ResearchProjects_Mechanitor.xml` and so looks mech-gated, but it inherits
nothing from `MechtechBase` and has no techprint. It is an ordinary project costing 8000
and needing a Hi-Tech bench plus a MultiAnalyzer — commonly the last ungated project left
unresearched, which is exactly why it was the lone survivor. Note also that `IsValid` never
consults `CanStartNow`, so a project the player *cannot currently research* is still a
valid book subject.

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
3. **No GitHub remote yet, by decision (2026-08-26).** Everything in the repo so far is
   test scaffolding that will be thrown away; the remote gets created once Changes 2 and 3
   land and there is something permanent to publish. Until then the folder-wide "commit and
   push every change" rule is commit-only — do **not** create the remote to satisfy it.

### Phase 1 — skeleton

**Flat layout, no `Common/` and no `LoadFolders.xml`** — those exist to serve more than
one game version, and 1.6 is the only supported version (§7, Settled). Items marked
*to build* do not exist yet.

```
UnlockedSchematics/
  About/
    About.xml            packageId, Harmony dependency, supportedVersions 1.6
    Preview.png          to build
  Patches/
    UnlockedSchematics_SchematicExcludes.xml
  Assemblies/
    UnlockedSchematics.dll     build output, committed - the repo is the mod
  Source/
    UnlockedSchematics/
      UnlockedSchematics.csproj
      Mod.cs                 Harmony bootstrap
      Patch_SchematicEligibility.cs
      Patch_ResearchGateClamp.cs
  Dev/
    README.md
    validate_patches.py
    TestHarness/           separate throwaway mod, forces schematic subjects for testing
  .rimignore
  .gitignore
  CLAUDE.md
  PLAN.md
  UnlockedSchematics-brief.md
```

`packageId` and `.rimignore` are both done — see §7, Settled. The brief's open question
about whether Patches are processed before Defs within one `loadFolders` entry is now moot
twice over: there is no `LoadFolders.xml`, and the mod ships no Defs.

Build target: `net472` via the `Krafs.Rimworld.Ref` + `Lib.Harmony` NuGet packages —
**confirmed by building, 2026-08-26**, not taken on trust. Details in Phase 3.

### Phase 2 — Change 1, verify in isolation — **done, verified in game 2026-08-26**

XML only, no assembly yet, so a failure here is unambiguously the def half. Installed to
`RimWorld/Mods/UnlockedSchematics`; `Dev/TestHarness` installed alongside as
`UnlockedSchematics_TESTONLY` to make the result binary rather than a lucky sample.

The xpath was checked against the shipped `Core/Defs/Books/BookDefs.xml`: one match, seven
children. That only proves the patch targets the right node — it does not prove RimWorld
applies it, which is what the in-game test is for.

Test: dev mode -> debug output -> Text generation -> Books -> Schematic (generates 30 and
logs them). With the harness on, every result should be a mechtech or gravtech subject.
For an exact project name, spawn a Schematic and open its info card.

**Result:** pass. Every schematic came back mechtech or gravtech, and the harness label
read `schematic (OK - blocklist removed)`.

It took three attempts to get there, and none of the failures were in the mod itself —
worth knowing, because the same traps are waiting in Phase 3:

1. Only the harness was enabled. Alone it sets `include` to seven projects that all still
   fail `IsValid`, so the pool empties and `OnBookGenerated` falls back to scanning every
   project while skipping the `IsFinished` *and* `PrerequisitesCompleted` filters. The
   output looks like a plausible random assortment, so a sampling test cannot tell this
   apart from "the patch is broken". That is why the harness now writes its verdict onto
   the item label instead.
2. The harness's `PatchOperationConditional` nested `<Operation Class="...">` inside
   `<match>`, leaving that node class-less. RimWorld built the abstract base
   `PatchOperation` and logged "Attempted to use PatchOperation directly; patch will always
   fail" in either load order.
3. The label used `[ ]`, which `Def.DisallowedLabelCharsRegex` rejects.

Bugs 2 and 3 both parse as valid XML and both target the right node, so the XPath
simulation used before the first test said "PASS" on each. `Dev/validate_patches.py` was
written afterwards and catches both; run it before trusting patch XML.

### Phase 3 — Changes 2 and 3 — **built 2026-08-26, untested in game**

One Harmony class each, both shipped in the same build so there is never a build in which
the gate is bypassable:

- `Source/UnlockedSchematics/Patch_SchematicEligibility.cs` — Change 2
- `Source/UnlockedSchematics/Patch_ResearchGateClamp.cs` — Change 3
- `Source/UnlockedSchematics/Mod.cs` — `[StaticConstructorOnStartup]` Harmony bootstrap

**Toolchain, confirmed rather than assumed:**

```bash
cd Source/UnlockedSchematics && dotnet build -c Release
```

Writes straight to `Assemblies/UnlockedSchematics.dll`. Built with dotnet SDK 10.0.400;
`net472` resolves without Visual Studio thanks to the
`Microsoft.NETFramework.ReferenceAssemblies` package. Versions are **pinned** for
reproducibility: `Krafs.Rimworld.Ref` 1.6.4871 (exactly the installed game build) and
`Lib.Harmony` 2.4.2.

**The Harmony mod's version and the Harmony library's version are separate version lines
that collide numerically.** Do not read one as the other:

| | Version | Released |
|---|---|---|
| HarmonyRimWorld **mod** (what the mod manager shows) | 2.4.2.0 | 2025-09-03 |
| `Current/Assemblies/0Harmony.dll`, the **library** it ships | 2.4.1.0 | 2025-08-22 |
| Harmony **library** 2.4.2.0 | 2.4.2.0 | **2025-11-13** |

The mod's 2.4.2.0 predates the library's 2.4.2.0 by two months, so this is not a stale or
un-rebuilt library — the mod bundled the newest library that existed when it shipped. Its
own version simply counts independently. Verified against Pardeike's repos via the commit
SHA embedded in each DLL's `ProductVersion`: the shipped `0Harmony.dll` is built from
`789df191`, exactly one commit before the `v2.4.1.0` tag — a CI build taken just before
tagging.

**So `Lib.Harmony` is pinned to 2.4.1, matching the library actually loaded, not 2.4.2.**
Reference *versions* bind loosely — `0Harmony` and `Assembly-CSharp` are both built without
a strong name (`PublicKeyToken` is null), so the runtime resolves by simple name and ignores
the version, verified by reflection rather than assumed. But missing *members* do not bind
loosely: compiling against a newer Harmony than the one loaded is how you earn a
`MissingMethodException` at startup. The loose binding is what makes ref 1.6.4871 work
against any 1.6.x game build; it is not a licence to compile against API that will not be
there.

Build-time verification done: 0 warnings; both patch classes decompiled back out of the
built DLL to confirm the attributes and `AccessTools` lookup are what was intended;
`IsValid`'s exact name/arity and all four gate property names read off the decompile first.

### Phase 4 — testing — **core behaviour verified 2026-08-26**

Rows 1, 2, 4, 5 and 6 of §6 pass in a real save. See §0 for the evidence table and for what
is still unrun.

### Phase 5 — publish

Steam Workshop under `VitaKaninen`. Commit and push per the folder rules.

---

## 6. Test plan

Dev mode, Royalty + Biotech + Odyssey enabled.

**Disable `Dev/TestHarness` for every test below.** Its `include` list replaces the whole
candidate pool, and the include branch skips the `PrerequisitesCompleted` check the normal
branch applies — which is exactly what produced the confusing 2026-08-26 result (see §6a).

Predict each expected column with `Dev/simulate_pool.py` before looking in game.

| # | Test | Expected |
|---|---|---|
| 1 | No mechanitor, no techprints, no grav engine. Spawn ~30 schematics | **No** mechtech, gravtech or techprint subjects at all |
| 2 | Apply 2 Cataphract techprints, spawn ~30 | CataphractArmor now appears as a subject |
| 3 | Read that CataphractArmor schematic to 100% | Completes normally — the gate is satisfied, so this is correct |
| 4 | Recruit a mechanitor, spawn ~30 | BasicMechtech appears; StandardMechtech does not (needs BasicMechtech + SignalChip) |
| 5 | Inspect a grav engine, spawn ~30 | BasicGravtech appears |
| 6 | Before studying a NanostructuringChip, spawn ~30 | **No** WastepackAtomizer subject — the vanilla bug in §2a is fixed |
| 7 | Generate a mechtech schematic, then let the only mechanitor die, then read it | Progress parks below 100%; research does not complete (Change 3's backstop) |
| 8 | Normal bench research on an ungated project | Completes exactly as vanilla — proves the `AddProgress` clamp does not touch `ResearchPerformed` |
| 9 | Read a schematic for an ordinary project | Unchanged from vanilla |
| 10 | Load a save made before the mod | No errors, no progress reset |

Test 1 is the one the design change exists for. Test 7 is the only remaining exercise of
Change 3 and needs setting up deliberately. Test 8 is the regression guard for the riskiest
patch.

### 6a. Why the 2026-08-26 session's result was misleading

Observed: StandardGravtech and StandardMechtech schematics spawning, no VenomSynthesis.
Both are explained by `Dev/TestHarness` still being enabled, not by the mod:

- its `include` list is exactly the seven mechtech/gravtech projects, and the include
  branch of `OnBookGenerated` never checks `PrerequisitesCompleted`, so Standard tiers
  qualified even with Basic unresearched;
- VenomSynthesis is not in that list, so it could not appear at all.

The harness is a debugging tool that *deliberately* replaces the candidate pool. Any test
of what the player actually sees has to run with it off.

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

### Settled — do not reopen without a reason

- **Keep the `requiredAnalyzed` fix (2026-08-26).** Change 2 checks all four unlock gates,
  which stops vanilla generating schematics for chip-locked research — `WastepackAtomizer`
  being the visible case (§2a). This *is* a deliberate deviation from vanilla behaviour and
  was confirmed as wanted. Do not add a compatibility toggle for it unless someone asks:
  it falls straight out of "only generate books for research that is unlocked", and
  special-casing it back would mean re-introducing the exact inconsistency the mod exists
  to remove.

- **RimWorld 1.5 support: no.** 1.6 only (decided 2026-08-26). `supportedVersions` in
  `About/About.xml` is correct as it stands; don't add a 1.5 entry or a 1.5 load folder.
- **GitHub remote: not yet.** See Phase 0 step 3.
- **`packageId` and `.rimignore`: done.** `Vita.UnlockedSchematics`, matching
  CarryCapacityFromBionics; `.rimignore` covers `PLAN.md`, `*-brief.md`, `Source`, `Dev`.

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
