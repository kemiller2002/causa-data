# Causa Data

Canonical datastore for the Causa Root-Cause Investigator.

**Contract status: proposed v1.0.0, NOT frozen or verified yet.**

The normative candidate specification, including exact field names, JSON types, versioning, event envelopes, manifests, snapshots, concurrency and required tests, is maintained in the Indy Init planning repository at `docs/CAUSA-DATA-CONTRACT-v1.md`. It must be reconciled with the actual F# engine and copied here into `contract/v1.0.0/CONTRACT.md` with complete JSON Schemas and golden fixtures before a freeze tag is created.

**Do not seed data until the schema has been verified.** Once frozen, fixture data is immutable across R002 and competition runs. Writes go only into isolated `workspaces/demo/<runId>/` paths. Never silently migrate, overwrite or reshape frozen fixtures. Preserve event history and semantic compatibility across application builds.

Planned scenarios: payment-failure, intermittent-outage, conflicting-metrics, api-latency, authentication-outage. Include separate negative fixtures.

Security: use synthetic information only; never commit GitHub credentials or secrets. Repository is currently public; review visibility and data policy before populating.

Competition: organizer rules on pre-existing data/schema must be checked before use.
