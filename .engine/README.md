# Product-owned Causa domain engine

This directory is the maintained build input for Causa Data's F# tooling. It is intentionally committed in this repository so creating or validating one rehearsal never reads another rehearsal repository.

The source under `src/engine/` was imported byte-for-byte from the engine recorded by the frozen storage-v1.0.0 provenance lock. The frozen `engine.lock.json` and contract continue to identify that historical origin; they are evidence, not fetch instructions. Builds, tests, compatibility gates, template validation, and workspace initialization use only the files committed here.

Do not add credentials, generated output, or nested Git metadata. Changes to engine semantics must pass the storage compatibility gate and may not rewrite the frozen v1.0.0 contract or historical investigations.
