# Everyday lightbulb demonstration

## What it shows

`burned-out-lightbulb` applies the same Causa investigation model used by the historical cases to an ordinary problem: “The ceiling light does not illuminate when the wall switch is turned on.” The point is not that a failed bulb is difficult to diagnose. The point is that even a simple diagnosis benefits from keeping observations, interpretations, alternatives, actions, and verification distinct.

The investigation identifier is `fc3f71a9-4c14-5a31-8196-7ce17a7041c1`. Its committed storage lives in `workspaces/demo/everyday-v1/investigations/`, and `templates/everyday-v1/` is the immutable reusable copy. `templates/demonstration-v1/` combines it with Apollo 1, Apollo 11, and Challenger without changing a byte of those historical investigations.

## Reasoning sequence

The domain script records four hypotheses separately:

1. The bulb has failed.
2. The wall switch is defective.
3. Power is unavailable to the fixture.
4. The fixture itself is defective.

The evidence then records, in order, that the light is dark, other room lights work, the switch is operated, circuit power is switched off before the original bulb is removed, and a known-good compatible replacement is installed while power remains off. These are observations; they do not themselves assert a conclusion.

The corrective action, “Replace the failed bulb with a compatible replacement,” is first proposed, then approved, started, and marked implemented. Only after the engine enters `AwaitingVerificationOfEffect` is the post-replacement illumination evidence admitted. That fresh evidence supports the failed-bulb hypothesis and weakens the unchanged-switch, available-power, and unchanged-fixture alternatives. The conclusion deliberately leaves the bulb’s exact internal failure mechanism unknown.

The F# test suite also runs a separate outcome where the known-good replacement remains dark. The same success criterion is then recorded as failed, the failed-bulb hypothesis is weakened, the correction becomes `VerificationFailed`, and the engine refuses to confirm the bulb as the cause. That negative scenario is test-only and is not presented as a resolved demonstration record.

No step instructs anyone to work on live electrical equipment. The demonstration records that circuit power is off for bulb removal and installation.

## Reproduce and verify

The records are produced by commands sent to the pinned F# domain engine; event streams and snapshots are not manually assembled.

```bash
dotnet run --project tools/src/Causa.Tool -- everyday-build .
dotnet run --project tools/src/Causa.Tool -- template-verify --template everyday-v1
dotnet run --project tools/src/Causa.Tool -- template-verify --template demonstration-v1
dotnet test tools/tests/Causa.Contract.Tests/Causa.Contract.Tests.fsproj -c Release
```

Expected immutable template digests:

- `everyday-v1`: `7b66ac5aefa402a8a4cc70117b98988cc45bd4cfd5660a11aab498515d6dc83a`
- `demonstration-v1`: `eefc5651fa3b034031813f1eaab1dd3d65b54f77c8db6965d858b0fffe136149`

To create an isolated four-case workspace:

```bash
dotnet run --project tools/src/Causa.Tool -- \
  workspace-create --template demonstration-v1 --workspace development-demo-001

dotnet run --project tools/src/Causa.Tool -- \
  workspace-application-verify --template demonstration-v1 --workspace development-demo-001
```

The application uses its normal resolver, `workspaces/demo/<workspace>/investigations/`. There is no lightbulb-specific provider or UI path.

## Two-minute Indy Init walkthrough

1. Open the investigation and read the original observation. Point out that the page starts with a fact, not a diagnosis.
2. Show the four hypotheses. They remain separately identifiable and independently stateful.
3. Inspect the evidence chronology. Emphasize the safety boundary and that “other lights work” weakens only a broad power-loss theory; it does not prove fixture power.
4. Show the correction lifecycle. Proposed, implemented, and verified are three different states.
5. Open the post-replacement test evidence and its relationships. The unchanged switch and fixture plus a known-good replacement are what discriminate the bulb hypothesis.
6. Read the conclusion and its residual uncertainty: the original bulb failed, but the investigation does not pretend to know its internal failure mechanism.
7. Mention the negative automated scenario: when replacement does not restore light, Causa records failed verification and refuses the unsupported conclusion.
