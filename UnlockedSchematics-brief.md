# Schematics Unlocked — Project Brief

## What the mod does

Research schematics — books that grant research progress when read — are only
generated for research projects the game considers *eligible*. Eligibility
appears to exclude any project carrying a secondary unlock gate, even when all
ordinary prerequisites are met.

Known or suspected gates:

| Gate | Status |
|---|---|
| Techprint requirement (`techprintCount > 0`) | Confirmed by testing |
| Mechanitor requirement (mechlink / mech chips) | Reported on the wiki, not tested |
| Odyssey grav-engine gating | Unknown — may use a different mechanism |

The mod makes schematics generate for these projects while leaving the gate
itself intact. A pawn can read a schematic and accumulate progress toward a
techprint-gated project; the project stays un-completable at the bench until
the techprint is applied.

## Why this isn't a def edit

Zeroing `techprintCount` makes schematics generate — that much is confirmed by
testing, and Steam Workshop item 2893432492 (FrozenSnowFoxTweaks) does it at
scale via `PatchOperationReplace`. But that removes the gameplay requirement,
which is the one thing this mod must not do.

Whether a def-level approach can work at all depends on a question that isn't
answered yet: does the eligibility filter read the same field the bench-side
completion check reads? If it does, no XML patch can separate the two, and a
Harmony patch on the eligibility method is the only route. **Assume Harmony
until the source says otherwise, but confirm it before building the skeleton
around that assumption.**

## What is actually established

- Removing techprints via XML causes previously-blocked projects to start
  generating schematics, provided nothing else blocks them.
- The RimWorld wiki's Schematic page states schematics are not generated for
  techprint-requiring or mechanitor-related projects.

Everything else below is a question, not a finding.

## Open questions — answer these from the decompiled source first

1. **Where is the filter?** Which class/method selects `ResearchProjectDef`s
   eligible to become schematics?
2. **How many call sites?** Quest rewards, trader stock, and random loot may
   each filter independently, or may share one helper. This determines whether
   the patch surface is one method or several.
3. **What does the check read?** A property like `TechprintRequirementMet`, a
   raw `techprintCount > 0`, or a hardcoded exclusion list — each implies a
   different patch.
4. **Is the mechanitor gate the same code path?** Likely a different field.
5. **Is Odyssey grav-engine research gated by this mechanism at all?**
6. **Does the bench-side completion check share the field?** This is what
   decides XML-vs-Harmony (see above).

Questions 1–3 gate everything else. Answer them before writing any code.

## Conventions

Folder-wide rules — commit/push expectations, the required `.rimignore`, and
the `loadFolders` shadowing trap — live in the parent folder's `CLAUDE.md`.
Beyond those:

- Repo name: PascalCase, no spaces, matching `DontTrashThis` and
  `WinterRyeGrass`. **Unresolved:** the folder is currently `Schematics
  Unlocked`; pick the final name before `git init`.
- Published to Steam Workshop under GitHub account `VitaKaninen`.

The previous draft asserted that RimWorld processes Patches before Defs within
a single `loadFolders` entry, and that Defs must therefore be listed first.
I can't confirm that, and it contradicts the usual understanding that all Defs
load before any patch is applied. Verify it against the source or a live test
before treating it as a constraint on the folder layout.

## First steps

1. Read the decompiled schematic/book generation code and answer questions
   1–3.
2. Propose the minimal patch surface — ideally one postfix on a shared
   eligibility method rather than scattered patches per generation path.
3. Only then build the mod skeleton: `About.xml`, `Assemblies/`, Harmony
   dependency, `LoadFolders.xml`, `.rimignore`.
