# Dev

Dev-only material. Excluded from the shipped mod by `.rimignore`; kept in git.

## TestHarness

A separate throwaway RimWorld mod that makes schematic-generation testing binary instead
of luck-based. It adds an `<include>` list of the seven blocklisted projects to the
`Schematic` ThingDef. `BookOutcomeProperties_GainResearch.include`, when non-empty,
replaces the whole candidate pool, and `OnBookGenerated` falls back to the normal pool if
nothing in it survives `IsValid`:

    every schematic is mechtech/gravtech  -> the blocklist removal worked
    schematics are ordinary projects      -> it did not

The include branch skips the `PrerequisitesCompleted` check the normal branch applies, so
it works on a brand-new colony with no research done.

To use: copy `TestHarness/` into RimWorld's `Mods/` folder as its own mod and enable it
alongside Unlocked Schematics. Load order between them does not matter. Disable it after.
