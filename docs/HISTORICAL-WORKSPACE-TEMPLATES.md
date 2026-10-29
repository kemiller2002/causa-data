# Historical workspace templates

## Purpose and ownership

Causa Data owns the immutable `historical-v1` template and the F# operations that create, validate, publish, and verify workspaces from it. Orchestrators such as Conditor call this interface; they do not copy its storage or validation rules.

The template contains Apollo 1, Apollo 11, and Space Shuttle Challenger exactly as committed in `workspaces/demo/historical-v1/investigations/` at source commit `dcb8cb994eab81d0384b9c8abc5f0b593f398442`. The catalog and canonical historical workspace remain authoritative and unchanged.

The same interface also supports the additive `everyday-v1` and `demonstration-v1` templates. The latter contains the exact historical files plus the engine-generated everyday lightbulb investigation. Selecting a new template never mutates `historical-v1`.

## Layout

```text
templates/historical-v1/
  template-manifest.json
  investigations/<investigation-id>/
    manifest.json
    events.ndjson
    snapshot.json

workspaces/demo/<workspace>/
  investigations/<investigation-id>/
    manifest.json
    events.ndjson
    snapshot.json
```

The three investigation identifiers are:

- Apollo 1: `c6b26614-fc43-5013-a15c-cf95942ee07b`
- Apollo 11: `e3adbee5-5096-5a10-bbfb-cecda86a154b`
- Challenger: `1784da3c-7141-59ee-a495-1d5d44bf2284`

## Deterministic manifest

Each file digest is lowercase hexadecimal SHA-256 over the file's raw bytes. The overall template digest is lowercase hexadecimal SHA-256 over the UTF-8 canonical JSON representation of the manifest after removing `overallTemplateSha256`; no trailing newline participates in that digest. Canonical JSON sorts object keys, preserves array order, and uses the Causa storage contract's canonical scalar representation.

The committed overall digest is `fbc776c2f4aa27d0cf2d6825ebf45b1c0c2c5219186179638818007d0b116425`.

`template-create` exists for controlled one-time construction and refuses to replace a template. Normal consumers use `template-verify`:

```bash
dotnet run --project tools/src/Causa.Tool -- \
  template-verify --template historical-v1
```

Verification requires the exact three identifiers and exact storage file set, matches every raw-file digest, and loads each history and snapshot through the product-owned engine committed under `.engine/`. The frozen `engine.lock.json` remains provenance for storage-v1.0.0, not a runtime dependency on another rehearsal.

For `everyday-v1` and `demonstration-v1`, the manifest declares and pins their exact one- and four-investigation sets respectively. The verifier derives workspace and remote-validation counts from the selected, validated manifest rather than assuming three cases.

## Create and verify a workspace

Workspace names must match `^[a-z0-9][a-z0-9-]{0,63}$` and are always resolved beneath `workspaces/demo/`. The tool rejects traversal, separators, symlinked path ancestors, paths outside the approved root, and existing destinations.

```bash
dotnet run --project tools/src/Causa.Tool -- \
  workspace-create --template historical-v1 --workspace development-001

dotnet run --project tools/src/Causa.Tool -- \
  workspace-verify --template historical-v1 --workspace development-001
```

Creation verifies the template before copying, stages all files under the approved workspace root, verifies copied digests and complete event replay, and publishes the directory with one atomic rename. A failure removes only the staging directory created by that invocation. It never deletes or overwrites a destination, so a retry is safe after a failed partial attempt but still refuses a successfully created workspace.

The default `initialized` verification requires byte identity. After supported persistence has appended events, use:

```bash
dotnet run --project tools/src/Causa.Tool -- \
  workspace-verify --template historical-v1 --workspace development-001 --mode modified
```

Modified verification replays the complete current history and validates its snapshot. It additionally requires each current event stream to retain the template event stream as an exact byte prefix. Legitimate appended events pass; replacement, deletion, or rewriting of historical events fails.

## Rehearsal initialization

```bash
dotnet run --project tools/src/Causa.Tool -- \
  rehearsal-init --number 006 --template historical-v1
```

This derives `rehearsal-006`, creates and verifies it, and writes a local initialization receipt. The receipt says `LOCAL_CREATED` and `LOCAL_VERIFIED`; it explicitly says `NOT_PUBLISHED` and has no published commit until publication really occurs. Initialization does not start a timed rehearsal.

The receipt keeps identities distinct: `--number 006` produces `rehearsalIdentifier: "006"` and `workspaceIdentifier: "rehearsal-006"`.

## GitHub publication and remote verification

```bash
dotnet run --project tools/src/Causa.Tool -- \
  workspace-remote-check --workspace rehearsal-006 \
  --repository kemiller2002/causa-data --branch main

dotnet run --project tools/src/Causa.Tool -- \
  workspace-publish --workspace rehearsal-006 --template historical-v1 \
  --repository kemiller2002/causa-data --branch main

dotnet run --project tools/src/Causa.Tool -- \
  workspace-remote-verify --workspace rehearsal-006 --template historical-v1 \
  --repository kemiller2002/causa-data --branch main --commit <published-sha>
```

Publication uses the configured Git credential mechanism; tokens are never accepted as command arguments, stored in investigation files or receipts, or returned in errors. The publisher confirms the repository branch, refuses an existing remote workspace, creates one commit containing only the selected workspace prefix, and performs a non-forced push. Concurrent branch movement is a failure. It then reads the committed tree and every blob back through the repository backend, compares paths and bytes to the local initialization, validates all three investigations again, and returns the published commit SHA.

If publication fails, the local workspace remains local and may be inspected. Resolve the reported authentication, branch, concurrency, or remote-destination condition. Do not select a previous workspace or create an empty replacement. If a concurrent writer created the requested remote workspace, choose a new explicit workspace identifier; do not overwrite it.

## Application connection and isolation

The Causa provider resolves a selected workspace as `workspaces/demo/<workspace>/investigations/`. A generated workspace therefore uses the same application settings as `historical-v1`, with only the workspace value changed:

```text
repository: kemiller2002/causa-data
branch:     main
workspace:  rehearsal-006
```

This path contract was checked against the frozen Rehearsal 5 implementation at commit `b972cb446de543018a65d8726cae6f8283821791`: `src/persistence/Provider.fs` derives `Target.workspaceRoot` as `workspaces/demo/` plus the selected workspace and reads its `investigations` child. The reference repository remains unmodified.

Run the resolver and load/replay compatibility check with:

```bash
dotnet run --project tools/src/Causa.Tool -- \
  workspace-application-verify --workspace rehearsal-006 --template historical-v1
```

Every workspace is a physical copy. Supported writes under one workspace path cannot modify the template, canonical historical workspace, or another rehearsal workspace. The automated suite creates two workspaces, appends an event through the pinned engine and Causa persistence code to one, and proves that all other copies remain byte-identical.
