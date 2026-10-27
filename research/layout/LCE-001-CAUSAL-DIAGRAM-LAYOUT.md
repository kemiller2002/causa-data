# LCE-001: causal-diagram layout and the historical catalog

This note connects the `historical-v1` catalog to Forma's layout-constraint research (LCE-001). It changes no data:

- No catalog, workspace, contract or fixture file was modified.
- The investigation records were read only, offline.

The full report is `experiments/LCE-001/REPORT.md` in `kemiller2002/forma` (branch `claude/forma-layout-constraints-0z9rzl`).

## How the catalog was used

The Causa app (`kemiller2002/indy-init-rehearsal-004`, `src/app/View.fs`, `graphDocument`) projects each investigation's causal structure as a Forma workflow document:

- every hypothesis that belongs to a cause becomes a `task` node, labelled with its statement;
- every cause outcome becomes an `end` node;
- every membership becomes an edge, labelled with its role.

Forma's probe (`forma-layout-probe causa --data <causa-data>`) derives the same documents from each `workspaces/demo/historical-v1/investigations/<id>/snapshot.json`. It renders them at five settings, so the diagrams can be tested without the deployed site:

- 1280 px
- 375 px
- zoom 0.5
- zoom 1.5
- 200% text

| investigation | nodes | edges | longest hypothesis | longest outcome |
|---|---:|---:|---:|---:|
| Challenger (STS-51-L) `1784da3c` | 9 | 8 | 354 characters | 61 characters |
| Apollo 1 (AS-204) `c6b26614` | 7 | 6 | 304 characters | 108 characters |
| Apollo 11 `e3adbee5` | 9 | 8 | 330 characters | 160 characters |

The derived fixtures are `tests/layout/fixtures/causa/` in Forma. Each one records its source snapshot path.

## What was found

**With the published packages the app pins**, `@echelon-foundry/design-system` 0.9.0 and `@echelon-foundry/forma-workflow` 1.0.0, all 15 variants break seven layout constraints. These were measured in Chromium through the same WebAssembly runtime the app embeds:

- outcome text is cut by the pill shape's curve;
- nodes are taller than their place in the layout and overlap;
- connectors start inside nodes and run through other nodes;
- role labels are drawn on nodes;
- content lies outside the canvas.

**The cause is in Forma, not in this data or in the app's projection.** Two defects in Forma produce the failures:

- a character-count height estimate;
- a pill radius with no bound.

The catalog's long, carefully attributed statements are legitimate content. They should not be shortened to fit a layout.

**With packages built from the corrected Forma branch**, all 15 variants satisfy every constraint.

## Impact

- **For the catalog.** None. No record needs to change.
- **For the app.** It adopts a Forma release that contains LCE-001. No application code changes.
- **Frozen rehearsal evidence.** It was not touched.
