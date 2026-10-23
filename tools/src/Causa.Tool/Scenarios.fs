namespace Causa.Tool

open System
open IndyInitRootCauseInvestigator.Engine
open Causa.Contract

/// One command issued to the real engine. `Refuse` steps document gates: the engine must
/// refuse them with the named code, and nothing is persisted.
type Expectation =
    | Accept
    | Refuse of code: string

type Step =
    { Actor: Actor
      Command: Command
      Expect: Expectation }

type Scenario =
    { Key: string
      Title: string
      Investigation: InvestigationId
      Start: DateTimeOffset
      ExpectedLifecycle: InvestigationLifecycle
      Steps: Step list }

/// Deterministic identifiers and actors for synthetic scenarios (no real people or systems).
module ScenarioKit =
    /// Namespace of fixture identifiers: UUIDv5(url, ".../fixtures/v1.0.0").
    let fixtureNamespace = Uuid.v5 Uuid.urlNamespace "https://github.com/kemiller2002/causa-data/fixtures/v1.0.0"

    let guid (scenario: string) (kind: string) (name: string) = Uuid.v5 fixtureNamespace (scenario + "/" + kind + "/" + name)

    let investigator = Actor.human "investigator-01"
    let sre = Actor.human "sre-02"
    let importer = Actor.system
    let assistant = Actor.agent "assistant-01" "synthetic-provider" "synthetic-model-1"

    let step actor command = { Actor = actor; Command = command; Expect = Accept }
    let refuse code actor command = { Actor = actor; Command = command; Expect = Refuse code }

    let utc (y, mo, d, h, mi) = DateTimeOffset(y, mo, d, h, mi, 0, TimeSpan.Zero)

/// The five frozen v1.0.0 scenarios (CONTRACT §12). All content is synthetic.
module Scenarios =
    open ScenarioKit

    let paymentFailure =
        let key = "payment-failure"
        let g = guid key
        let inv = Id.create<InvestigationTag> (g "investigation" "root")
        let h1, h2, h3 = Id.create (g "hypothesis" "gateway-timeout"), Id.create (g "hypothesis" "fraud-rule"), Id.create (g "hypothesis" "rounding")
        let h4g = g "hypothesis" "retry-storm"
        let e1, e2, e3 = Id.create (g "evidence" "gateway-log"), Id.create (g "evidence" "fraud-metric"), Id.create (g "evidence" "support-testimony")
        let start = utc (2026, 9, 8, 10, 0)
        { Key = key
          Title = "Card payments failing at checkout"
          Investigation = inv
          Start = start
          ExpectedLifecycle = Active
          Steps =
            [ step investigator (StartInvestigation(inv, "Card payments failing at checkout", Id.create (g "observation" "initial"),
                                                    "Since 09:40 UTC roughly 18% of card payments at checkout return 'payment declined' without a processor decline code."))
              step investigator ActivateInvestigation
              step investigator (RecordObservation(Id.create (g "observation" "eu-cluster"), "Failures cluster on the EU checkout cluster; US checkout is unaffected.", Some(start.AddMinutes -12.0)))
              step investigator (RecordHypothesis(h1, "The card processor gateway times out and the client maps timeouts to 'declined'.",
                                                  Some "Gateway latency exceeds the 3 s client timeout and the timeout handler returns a generic decline."))
              step investigator (RecordHypothesis(h2, "A fraud-scoring rule deployed this morning rejects legitimate cards.", None))
              step investigator (RecordHypothesis(h3, "Currency rounding in the EU price service produces amounts the processor rejects.",
                                                  Some "Amounts with three decimal places fail processor validation."))
              step importer (AdmitEvidence(e1, TextInput "Gateway log excerpt: 2,114 requests exceeded 3000 ms between 09:40 and 10:10 UTC.", Log,
                                           "checkout-gateway log export (synthetic)", Some(start.AddMinutes -5.0)))
              step investigator (AdmitEvidence(e2, TextInput "Fraud rule FR-118 rejection rate 0.4%, unchanged from its 7-day baseline.", Measurement,
                                               "fraud-scoring dashboard (synthetic)", Some(start.AddMinutes -3.0)))
              step sre (AdmitEvidence(e3, TextInput "Support reports several customers saw a 'card flagged' message before the decline.", Testimony,
                                      "support desk summary (synthetic)", None))
              step investigator (RelateEvidence(Id.create (g "relationship" "r1"), e1, h1, Supports, Some "Timeout volume matches the failure window."))
              step investigator (RelateEvidence(Id.create (g "relationship" "r2"), e2, h2, Contradicts, Some "The rule's rejection rate is at baseline."))
              step investigator (RelateEvidence(Id.create (g "relationship" "r3"), e3, h2, Supports, Some "Customers report fraud-style messaging."))
              step investigator (TransitionHypothesis(h1, MarkPlausible, "Timeout volume coincides with the failure window.", [ e1 ]))
              step investigator (TransitionHypothesis(h2, MarkPlausible, "Customer reports are consistent with a fraud-rule rejection despite the baseline metric.", [ e3 ]))
              step investigator (IdentifyUnknown(Id.create (g "unknown" "retries"), "Does the client retry declined payments, multiplying gateway load?", Material))
              step investigator (RecordAssumption(Id.create (g "assumption" "coverage"), "The synthetic log export covers all EU checkout nodes.", Material))
              step investigator (TransitionAssumption(Id.create (g "assumption" "coverage"), ProvisionallyAcceptAssumption))
              step investigator (RecordPrediction(Id.create (g "prediction" "timeout-raise"), h1,
                                                  "If timeouts cause the declines, raising the client timeout to 6 s on one node removes declines on that node.",
                                                  DiscriminatingPrediction, Prospective))
              step assistant (RecordProposal(Id.create (g "proposal" "retry-storm"), ProposedHypothesis, "A client-side retry storm amplifies gateway latency into timeouts.",
                                             Some "Retry counters were not inspected; retries would explain the latency growth."))
              step investigator (AcceptProposal(Id.create (g "proposal" "retry-storm"), h4g))
              step assistant (RecordProposal(Id.create (g "proposal" "rollback"), ProposedAction, "Roll back fraud rule FR-118 immediately.",
                                             Some "The rule changed this morning."))
              step investigator (RejectProposal(Id.create (g "proposal" "rollback"), "A rollback is a mitigation, not an investigative action, and the rule's metric is at baseline."))
              step investigator (RecordAction(Id.create (g "action" "timeout-test"), Test,
                                              "Raise the client timeout to 6 s on one EU node and compare decline rates for 15 minutes.", [ h1; Id.create h4g ]))
              step investigator (TransitionAction(Id.create (g "action" "timeout-test"), ApproveAction))
              step investigator (TransitionAction(Id.create (g "action" "timeout-test"), StartAction))
              // The causal question is open: no cause may be confirmed yet.
              refuse "RESOLVE.NO_CONFIRMED_CAUSE" investigator ResolveInvestigation ] }

    let intermittentOutage =
        let key = "intermittent-outage"
        let g = guid key
        let inv = Id.create<InvestigationTag> (g "investigation" "root")
        let h1, h2 = Id.create (g "hypothesis" "dns"), Id.create (g "hypothesis" "cert-rotation")
        let e1, e2, e3, e4 =
            Id.create (g "evidence" "lb-log"), Id.create (g "evidence" "dns-log"), Id.create (g "evidence" "rotation-history"), Id.create (g "evidence" "lb-log-corrected")
        let a1, a2, a3 = Id.create (g "action" "drain-inspection"), Id.create (g "action" "region-query"), Id.create (g "action" "staging-rotation")
        let start = utc (2026, 9, 14, 6, 30)
        let at (h, m) = Some(utc (2026, 9, 14, h, m))
        { Key = key
          Title = "Intermittent 502 errors on the order-status API"
          Investigation = inv
          Start = start
          ExpectedLifecycle = AwaitingEvidence
          Steps =
            [ step investigator (StartInvestigation(inv, "Intermittent 502 errors on the order-status API", Id.create (g "observation" "initial"),
                                                    "The order-status API returned bursts of HTTP 502 overnight; each burst lasted under a minute."))
              step investigator ActivateInvestigation
              step investigator (RecordObservation(Id.create (g "observation" "burst-1"), "First 502 burst observed at 02:14 UTC lasting about 40 seconds.", at (2, 14)))
              step investigator (RecordObservation(Id.create (g "observation" "burst-2"), "Second burst at 02:51 UTC, again about 40 seconds.", at (2, 51)))
              step sre (RecordObservation(Id.create (g "observation" "quiet"), "No bursts between 03:00 and 06:00 UTC.", at (6, 0)))
              step investigator (RecordHypothesis(h1, "DNS resolution for the upstream inventory service intermittently fails.", None))
              step investigator (RecordHypothesis(h2, "The load balancer drops connections during scheduled certificate rotation.",
                                                  Some "The rotation job reloads the listener certificate; drain timeout governs in-flight connections."))
              step importer (AdmitEvidence(e1, TextInput "LB access log: 502 responses at 02:14:03-02:14:44 and 02:51:10-02:51:49 UTC.", Log,
                                           "load-balancer access log export (synthetic)", at (6, 10)))
              step investigator (AdmitEvidence(e2, UrlInput "https://example.invalid/synthetic/resolver-query-log-2026-09-14.txt", Log,
                                               "resolver query log (synthetic external artifact)", at (6, 12)))
              step investigator (AdmitEvidence(e3, TextInput "Certificate rotation job history: runs at 02:13:58 and 02:51:06 UTC.", Document,
                                               "scheduler job history (synthetic)", at (6, 15)))
              step sre (VerifyEvidence(e1, Verified))
              step sre (VerifyEvidence(e2, PartiallyVerified))
              step importer (AdmitEvidence(e4, TextInput "Corrected LB access log export: 502 responses at 02:14:03-02:14:44 and 02:51:10-02:51:52 UTC.", Log,
                                           "load-balancer access log re-export (synthetic)", at (6, 40)))
              step investigator (SupersedeEvidence(e1, e4, "The first export truncated the second burst by three seconds."))
              step investigator (RelateEvidence(Id.create (g "relationship" "r1"), e2, h1, Contradicts, Some "All DNS queries in both burst windows resolved."))
              step investigator (RelateEvidence(Id.create (g "relationship" "r2"), e3, h2, Supports, Some "Rotation starts seconds before each burst."))
              step investigator (RelateEvidence(Id.create (g "relationship" "r3"), e4, h2, Supports, None))
              step investigator (TransitionHypothesis(h1, Falsify, "Resolver logs show every query in both burst windows succeeded.", [ e2 ]))
              step investigator (EstablishNegativeKnowledge(Id.create (g "negative" "not-dns"), "DNS resolution failure is not the cause of the 502 bursts.",
                                                            "Order-status API, 02:00-03:00 UTC on 2026-09-14", [ e2 ]))
              step investigator (TransitionHypothesis(h2, MarkPlausible, "Rotation timing precedes both bursts.", [ e3; e4 ]))
              step investigator (RecordAction(a1, Inspection, "Inspect load-balancer drain settings used during certificate rotation.", [ h2 ]))
              step investigator (TransitionAction(a1, ApproveAction))
              step sre (RecordFinding(Id.create (g "finding" "drain-zero"), a1, "Drain timeout is 0 s; connections close immediately when the certificate reloads.",
                                      Some "Consistent with abrupt connection drops during rotation."))
              step investigator (RecordAction(a2, Query, "Query all edge regions for 502 bursts in the last 24 hours.", [ h2 ]))
              step investigator (TransitionAction(a2, RejectAction))
              step investigator (RecordAction(a3, Experiment, "Trigger a manual certificate rotation in staging and watch for 502s.", [ h2 ]))
              step investigator (TransitionAction(a3, ApproveAction))
              step investigator (TransitionAction(a3, CancelAction))
              // A11: requested as prospective, recorded as retrospective because a finding already exists.
              step investigator (RecordPrediction(Id.create (g "prediction" "every-rotation"), h2,
                                                  "If rotation causes the bursts, every rotation in the scheduler history coincides with a burst.", Expected, Prospective))
              step investigator (IdentifyUnknown(Id.create (g "unknown" "next-rotation"), "Does a burst recur at the next scheduled rotation at 03:28 UTC tomorrow?", Blocking))
              step investigator (TransitionUnknown(Id.create (g "unknown" "next-rotation"), StartInvestigatingUnknown, "Monitoring the next rotation window."))
              step assistant (RecordProposal(Id.create (g "proposal" "shared-lb"), ProposedUnknown, "Are other services behind the same load balancer affected during rotation?", None))
              step investigator (AcceptProposal(Id.create (g "proposal" "shared-lb"), g "unknown" "shared-lb"))
              step investigator (MarkAwaitingEvidence "Waiting for the next scheduled rotation to confirm the burst pattern.") ] }

    let conflictingMetrics =
        let key = "conflicting-metrics"
        let g = guid key
        let inv = Id.create<InvestigationTag> (g "investigation" "root")
        let h1, h2, h3 = Id.create (g "hypothesis" "sampling"), Id.create (g "hypothesis" "canary"), Id.create (g "hypothesis" "clock-skew")
        let e1, e2, e3, e4, e5 =
            Id.create (g "evidence" "dashboard"), Id.create (g "evidence" "log-count"), Id.create (g "evidence" "ntp"), Id.create (g "evidence" "ntp-late"),
            Id.create (g "evidence" "sampler-config")
        let cause = Id.create (g "cause" "under-reporting")
        let start = utc (2026, 9, 21, 11, 0)
        let at (h, m) = Some(utc (2026, 9, 21, h, m))
        { Key = key
          Title = "Error-rate dashboard contradicts application logs"
          Investigation = inv
          Start = start
          ExpectedLifecycle = Active
          Steps =
            [ step investigator (StartInvestigation(inv, "Error-rate dashboard contradicts application logs", Id.create (g "observation" "initial"),
                                                    "The dashboard shows a 0.1% checkout error rate while on-call engineers see frequent errors in the logs."))
              step investigator ActivateInvestigation
              step investigator (RecordHypothesis(h1, "The metrics pipeline samples away error events under load.", Some "Head sampling at 1% under high throughput."))
              step investigator (RecordHypothesis(h2, "A canary deployment produces real errors that the dashboard filters out by version tag.", None))
              step investigator (RecordHypothesis(h3, "Host clock skew places errors outside the dashboard aggregation window.", None))
              step investigator (AdmitEvidence(e1, TextInput "Dashboard: checkout error rate 0.1% in 5-minute buckets, 09:00-10:00 UTC.", Measurement,
                                               "metrics dashboard (synthetic)", at (10, 5)))
              step investigator (AdmitEvidence(e2, TextInput "Log analytics: 4.1% of checkout requests logged at ERROR, 09:00-10:00 UTC.", Measurement,
                                               "log analytics query (synthetic)", at (10, 7)))
              step sre (AdmitEvidence(e3, TextInput "NTP report: all checkout hosts within 4 ms of the reference clock.", Measurement, "NTP monitoring (synthetic)", at (10, 20)))
              step investigator (RecordContradiction(Id.create (g "contradiction" "rates"),
                                                     "The dashboard error rate (0.1%) and the log-derived error rate (4.1%) cannot both be correct for the same window.",
                                                     [ e1; e2 ], Blocking))
              step investigator (TransitionContradiction(Id.create (g "contradiction" "rates"), TriageContradiction,
                                                         "Blocking: no cause can be confirmed until the measurement disagreement is explained."))
              step investigator (IdentifyUnknown(Id.create (g "unknown" "which-rate"), "Which of the two error-rate measurements is correct?", Blocking))
              step investigator (RelateEvidence(Id.create (g "relationship" "r1"), e2, h1, Supports, Some "Errors exist that the dashboard does not show."))
              step investigator (RelateEvidence(Id.create (g "relationship" "r2"), e3, h3, Contradicts, Some "Clocks are within tolerance."))
              step investigator (TransitionHypothesis(h3, Falsify, "All host clocks are within 4 ms.", [ e3 ]))
              step sre (AdmitEvidence(e4, TextInput "Late NTP export: two checkout hosts drifted 9 minutes between 09:20 and 09:35 UTC.", Measurement,
                                      "NTP monitoring late export (synthetic)", at (11, 40)))
              step investigator (TransitionHypothesis(h3, Reopen, "New evidence shows a drift window that the first report averaged away.", [ e4 ]))
              step sre (VerifyEvidence(e4, PartiallyVerified))
              step investigator (TransitionHypothesis(h1, MarkPlausible, "Sampling would explain the gap between the measurements.", [ e2 ]))
              step investigator (TransitionHypothesis(h1, MarkSupported, "Log volume exceeded the sampler's documented capacity.", [ e2 ]))
              step investigator (AdmitEvidence(e5, TextInput "Sampler configuration: events at ERROR level bypass sampling (priority class).", Document,
                                               "metrics pipeline configuration (synthetic)", None))
              step investigator (RelateEvidence(Id.create (g "relationship" "r3"), e5, h1, Contradicts, Some "Errors bypass sampling by configuration."))
              step investigator (TransitionHypothesis(h1, MarkWeakened, "Configuration says error events are never sampled.", [ e5 ]))
              step investigator (RecordAssumption(Id.create (g "assumption" "same-hosts"), "Both measurements cover the same set of checkout hosts.", Material))
              step investigator (TransitionAssumption(Id.create (g "assumption" "same-hosts"), RejectAssumption))
              step investigator (RecordAssumption(Id.create (g "assumption" "log-levels"), "Log levels were not changed during the window.", NonMaterial))
              step investigator (TransitionAssumption(Id.create (g "assumption" "log-levels"), VerifyAssumption))
              step investigator (RecordCausalAssertion(cause, "The dashboard under-reports checkout errors.", [ { Hypothesis = h1; Role = RootCause } ],
                                                       Some "Sampling drops error events before aggregation."))
              // The contradiction is material and unresolved: the cause gate must refuse.
              refuse "CAUSE.MEMBER_NOT_SUPPORTED" investigator (ConfirmCause cause)
              refuse "CONCLUSION.UNSUPPORTED" investigator (RecordConclusion(Id.create (g "conclusion" "forged"), "Sampling caused the under-reporting.", ConfirmedCause, [ cause ], None))
              step investigator (RecordConclusion(Id.create (g "conclusion" "interim"),
                                                  "No cause can be concluded: the blocking contradiction between the two error-rate measurements is unresolved.",
                                                  PlausibleExplanation, [], Some "Which measurement is correct remains a blocking unknown."))
              refuse "RESOLVE.NO_CONFIRMED_CAUSE" investigator ResolveInvestigation ] }

    let apiLatency =
        let key = "api-latency"
        let g = guid key
        let inv = Id.create<InvestigationTag> (g "investigation" "root")
        let h1, h2, h3 = Id.create (g "hypothesis" "n-plus-one"), Id.create (g "hypothesis" "cache-ttl"), Id.create (g "hypothesis" "noisy-neighbour")
        let e1, e2, e3, e4 = Id.create (g "evidence" "trace"), Id.create (g "evidence" "diff"), Id.create (g "evidence" "config-diff"), Id.create (g "evidence" "host-metrics")
        let cause = Id.create (g "cause" "regression")
        let ca1, ca2 = Id.create (g "corrective" "batch-load"), Id.create (g "corrective" "ttl-mitigation")
        let start = utc (2026, 9, 28, 15, 0)
        let at (h, m) = Some(utc (2026, 9, 28, h, m))
        { Key = key
          Title = "p95 latency regression on the search API after release 4.2"
          Investigation = inv
          Start = start
          ExpectedLifecycle = AwaitingVerification
          Steps =
            [ step investigator (StartInvestigation(inv, "p95 latency regression on the search API after release 4.2", Id.create (g "observation" "initial"),
                                                    "Search API p95 latency rose from 180 ms to 640 ms within ten minutes of the 4.2 rollout."))
              step investigator ActivateInvestigation
              step investigator (RecordHypothesis(h1, "Release 4.2 introduced an N+1 query in result hydration.", Some "Each result triggers a separate repository lookup."))
              step investigator (RecordHypothesis(h2, "A reduced cache TTL in 4.2 increases database round trips.", None))
              step investigator (RecordHypothesis(h3, "A noisy neighbour on the database host is consuming capacity.", None))
              step importer (AdmitEvidence(e1, TextInput "Trace sample: 41 SELECT statements per search request in 4.2 versus 2 in 4.1.", Measurement,
                                           "distributed tracing sample (synthetic)", at (14, 40)))
              step investigator (AdmitEvidence(e2, TextInput "Release diff: the hydration loop calls repository.findById once per result.", Document, "release diff review (synthetic)", None))
              step investigator (AdmitEvidence(e3, TextInput "Configuration diff: search cache TTL lowered from 300 s to 30 s in 4.2.", Document, "release configuration diff (synthetic)", None))
              step sre (AdmitEvidence(e4, TextInput "Database host CPU steal below 1% throughout the regression window.", Measurement, "host metrics (synthetic)", at (14, 50)))
              step investigator (RelateEvidence(Id.create (g "relationship" "r1"), e1, h1, Supports, Some "Query count per request multiplied."))
              step investigator (RelateEvidence(Id.create (g "relationship" "r2"), e2, h1, Supports, Some "The code path explains the query count."))
              step investigator (RelateEvidence(Id.create (g "relationship" "r3"), e3, h2, Supports, None))
              step investigator (RelateEvidence(Id.create (g "relationship" "r4"), e4, h3, Discriminates, Some "No contention on the host."))
              step investigator (TransitionHypothesis(h3, Supersede, "Superseded by the N+1 explanation; host metrics show no contention.", []))
              step investigator (TransitionHypothesis(h1, MarkPlausible, "Trace shows multiplied queries.", [ e1 ]))
              step investigator (TransitionHypothesis(h1, MarkSupported, "Trace and code path agree.", [ e1; e2 ]))
              step investigator (TransitionHypothesis(h2, MarkPlausible, "Shorter TTL increases misses.", [ e3 ]))
              step investigator (TransitionHypothesis(h2, MarkSupported, "Configuration diff confirms the TTL change shipped in 4.2.", [ e3 ]))
              step investigator (RecordPrediction(Id.create (g "prediction" "revert"), h1, "Batch-loading hydration results restores p95 below 200 ms.", Necessary, Prospective))
              step investigator (RecordCausalAssertion(cause, "Search API p95 latency regression in release 4.2",
                                                       [ { Hypothesis = h1; Role = RootCause }; { Hypothesis = h2; Role = ContributingFactor } ],
                                                       Some "Per-result queries multiply database round trips; the shorter TTL removes the cache that masked them."))
              step investigator (ConfirmCause cause)
              step investigator (RecordCorrectiveAction(ca1, Correction, "Batch-load hydration results with a single query.", [ cause ]))
              step investigator (RecordCorrectiveAction(ca2, Mitigation, "Temporarily restore the 300 s cache TTL.", [ cause ]))
              step investigator (TransitionCorrection(ca2, ApproveCorrection))
              step investigator (TransitionCorrection(ca2, CancelCorrection))
              step investigator (RecordCriterion(Id.create (g "criterion" "p95"), ca1, "Search p95 stays below 200 ms for 24 hours after deployment.", Some "Latency dashboard p95"))
              step investigator (RecordCriterion(Id.create (g "criterion" "queries"), ca1, "At most 3 queries per search request.", Some "Trace sample"))
              step investigator (TransitionCorrection(ca1, ApproveCorrection))
              step sre (TransitionCorrection(ca1, StartCorrection))
              step sre (TransitionCorrection(ca1, MarkImplemented))
              step investigator (TransitionCorrection(ca1, StartVerificationOfEffect))
              step investigator MarkAwaitingVerification
              step investigator (IdentifyUnknown(Id.create (g "unknown" "ttl-after-fix"), "Does the 30 s TTL need to change once batching ships?", NonMaterial))
              step investigator (TransitionUnknown(Id.create (g "unknown" "ttl-after-fix"), AcceptUnknownAsResidual, "Out of scope for this regression."))
              // Implementation is not verification (R7.4): resolution must be refused.
              refuse "RESOLVE.CORRECTION_NOT_VERIFIED" investigator ResolveInvestigation ] }

    let authenticationOutage =
        let key = "authentication-outage"
        let g = guid key
        let inv = Id.create<InvestigationTag> (g "investigation" "root")
        let h1, h2 = Id.create (g "hypothesis" "stale-key"), Id.create (g "hypothesis" "clock-drift")
        let ev name = Id.create<EvidenceTag> (g "evidence" name)
        let cause = Id.create (g "cause" "stale-key-cache")
        let ca1, ca2 = Id.create (g "corrective" "follow-redirects"), Id.create (g "corrective" "manual-key-load")
        let cr1 = Id.create (g "criterion" "drill")
        let act = Id.create (g "action" "refresh-logs")
        let start = utc (2026, 10, 5, 13, 30)
        let at (h, m) = Some(utc (2026, 10, 5, h, m))
        { Key = key
          Title = "Users unable to sign in via single sign-on"
          Investigation = inv
          Start = start
          ExpectedLifecycle = Resolved
          Steps =
            [ step investigator (StartInvestigation(inv, "Users unable to sign in via single sign-on", Id.create (g "observation" "initial"),
                                                    "From 13:05 UTC all single sign-on logins fail with 'invalid signature'."))
              step investigator ActivateInvestigation
              step investigator (RecordHypothesis(h1, "The identity provider rotated its signing key and the service still trusts only the old key.",
                                                  Some "The service caches identity-provider metadata and verifies assertions against cached keys."))
              step investigator (RecordHypothesis(h2, "System clock drift makes assertions appear expired.", None))
              step importer (AdmitEvidence(ev "auth-log", TextInput "Auth service log: 'signature verification failed (kid=2026-10-b)' for every SSO assertion since 13:05 UTC.", Log,
                                           "auth service log export (synthetic)", at (13, 25)))
              step investigator (AdmitEvidence(ev "idp-metadata", TextInput "Identity-provider metadata: signing key kid=2026-10-b published 13:00 UTC; kid=2026-09-a retired.", Document,
                                               "identity-provider metadata snapshot (synthetic)", at (13, 26)))
              step sre (AdmitEvidence(ev "clocks", TextInput "Host clocks within 20 ms of the reference clock.", Measurement, "NTP monitoring (synthetic)", at (13, 27)))
              step investigator (RelateEvidence(Id.create (g "relationship" "r1"), ev "auth-log", h1, Supports, Some "Failures name the new key id."))
              step investigator (RelateEvidence(Id.create (g "relationship" "r2"), ev "idp-metadata", h1, Supports, Some "The key rotated minutes before the outage."))
              step investigator (RelateEvidence(Id.create (g "relationship" "r3"), ev "clocks", h2, Contradicts, Some "Clocks are within tolerance."))
              step investigator (TransitionHypothesis(h2, Falsify, "Clocks are within 20 ms.", [ ev "clocks" ]))
              step investigator (TransitionHypothesis(h1, MarkPlausible, "Failures name the new key id.", [ ev "auth-log" ]))
              step investigator (TransitionHypothesis(h1, MarkSupported, "Logs and metadata agree on the rotation time.", [ ev "auth-log"; ev "idp-metadata" ]))
              step investigator (RecordAssumption(Id.create (g "assumption" "metadata-authoritative"), "The identity-provider metadata endpoint is authoritative for active signing keys.", Material))
              step investigator (TransitionAssumption(Id.create (g "assumption" "metadata-authoritative"), VerifyAssumption))
              step investigator (IdentifyUnknown(Id.create (g "unknown" "refresh"), "Why did the metadata refresh job not load the new key?", Blocking))
              step investigator (RecordAction(act, Inspection, "Inspect the metadata refresh job logs.", [ h1 ]))
              step investigator (TransitionAction(act, ApproveAction))
              step sre (TransitionAction(act, StartAction))
              step sre (RecordFinding(Id.create (g "finding" "redirect"), act, "The refresh job has failed since 2026-09-30 with HTTP 301; it does not follow redirects.",
                                      Some "A stale key cache explains the outage."))
              step investigator (TransitionUnknown(Id.create (g "unknown" "refresh"), ResolveUnknown, "The refresh job does not follow the identity provider's redirect."))
              step sre (AdmitEvidence(ev "cached-session", TextInput "One SSO login succeeded at 13:20 UTC; it reused a cached session and verified no new assertion.", Log,
                                      "auth service log export (synthetic)", at (13, 40)))
              step investigator (RecordContradiction(Id.create (g "contradiction" "one-success"), "One login succeeded at 13:20 UTC despite the stale key.",
                                                     [ ev "auth-log"; ev "cached-session" ], Informational))
              step investigator (TransitionContradiction(Id.create (g "contradiction" "one-success"), ResolveContradiction,
                                                         "The 13:20 success reused a cached session and did not verify a new assertion."))
              step investigator (RecordCausalAssertion(cause, "Single sign-on outage", [ { Hypothesis = h1; Role = RootCause } ],
                                                       Some "The stale metadata cache lacked the rotated signing key."))
              step investigator (ConfirmCause cause)
              step investigator (AdmitEvidence(ev "idp-notice", TextInput "Identity-provider notice: the metadata URL moved on 2026-09-30 (HTTP 301 redirect).", ExternalReference,
                                               "identity-provider change notice (synthetic)", None))
              step investigator (ReopenCause(cause, "Reassess whether the URL move is part of the explanation before relying on the confirmation."))
              step investigator (ConfirmCause cause)
              step investigator (RecordCorrectiveAction(ca1, Correction, "Make the metadata refresh job follow redirects and alert on refresh failure.", [ cause ]))
              step investigator (RecordCorrectiveAction(ca2, Workaround, "Manually load the new signing key into the cache.", [ cause ]))
              step investigator (TransitionCorrection(ca2, ApproveCorrection))
              step sre (TransitionCorrection(ca2, StartCorrection))
              step sre (TransitionCorrection(ca2, MarkImplemented))
              step investigator (RecordCriterion(cr1, ca1, "A forced key rotation in staging is loaded within 5 minutes.", Some "Staging rotation drill"))
              step investigator (TransitionCorrection(ca1, ApproveCorrection))
              step sre (TransitionCorrection(ca1, StartCorrection))
              step sre (TransitionCorrection(ca1, MarkImplemented))
              step investigator (TransitionCorrection(ca1, StartVerificationOfEffect))
              step investigator MarkAwaitingVerification
              step sre (AdmitEvidence(ev "drill-1", TextInput "Staging drill 1: new key not loaded after 15 minutes; the job does not follow HTTP 308.", ExperimentResult,
                                      "staging rotation drill (synthetic)", at (16, 30)))
              step investigator (RecordVerification(Id.create (g "verification" "drill-1"), cr1, Failed, [ ev "drill-1" ]))
              step investigator (TransitionCorrection(ca1, RequireRework))
              step sre (TransitionCorrection(ca1, StartCorrection))
              step sre (TransitionCorrection(ca1, MarkImplemented))
              step investigator (TransitionCorrection(ca1, StartVerificationOfEffect))
              step investigator MarkAwaitingVerification
              step sre (AdmitEvidence(ev "drill-2", TextInput "Staging drill 2: new key loaded after 2 minutes; production rotation at 18:00 UTC loaded after 3 minutes.",
                                      ExperimentResult, "staging and production rotation drill (synthetic)", at (18, 10)))
              step sre (VerifyEvidence(ev "drill-2", Verified))
              step investigator (RecordVerification(Id.create (g "verification" "drill-2"), cr1, Satisfied, [ ev "drill-2" ]))
              step investigator (RecordConclusion(Id.create (g "conclusion" "final"),
                                                  "The SSO outage was caused by a stale signing-key cache: the metadata refresh job stopped following the identity provider's redirect on 2026-09-30, so the 13:00 UTC key rotation was never loaded.",
                                                  ConfirmedCause, [ cause ],
                                                  Some "The manual key load is not independently verified; the verified correction supersedes it."))
              step investigator ResolveInvestigation
              step investigator CloseInvestigation
              step investigator (ReopenInvestigation "Post-incident review asked for the production rotation evidence to be recorded against the investigation.")
              step investigator ResolveInvestigation ] }

    let all = [ paymentFailure; intermittentOutage; conflictingMetrics; apiLatency; authenticationOutage ]

/// Runs scenarios through the real engine (Decide.execute) with a fixed clock.
module Runner =
    type Run =
        { Scenario: Scenario
          Events: Event list
          Refusals: (int * string * string) list }

    /// Step i executes at Start + 4i minutes (millisecond-exact, UTC).
    let timeOf (s: Scenario) (i: int) = s.Start.AddMinutes(4.0 * float i)

    let commandId (s: Scenario) (i: int) = ScenarioKit.guid s.Key "command" (string i)

    let run (s: Scenario) : Result<Run, string> =
        s.Steps
        |> List.indexed
        |> List.fold
            (fun acc (i, st) ->
                acc
                |> Result.bind (fun (state: Investigation option, events: Event list, refusals) ->
                    let env =
                        { CommandId = commandId s i
                          Investigation = s.Investigation
                          ExpectedVersion = state |> Option.map (fun x -> x.Version)
                          Actor = st.Actor
                          RequestedAt = timeOf s i
                          Command = st.Command }
                    match Decide.execute state env, st.Expect with
                    | Ok produced, Accept ->
                        let next = produced |> List.fold (fun st e -> Some(Evolve.apply st e)) state
                        Ok(next, events @ produced, refusals)
                    | Error errors, Accept ->
                        Error($"{s.Key} step {i} was refused: " + String.Join("; ", errors |> List.map (fun e -> e.Code + " " + e.Message)))
                    | Ok _, Refuse code -> Error($"{s.Key} step {i} was expected to be refused with {code} but was accepted")
                    | Error errors, Refuse code when errors |> List.exists (fun e -> e.Code = code) ->
                        Ok(state, events, refusals @ [ i, code, String.Join("; ", errors |> List.map (fun e -> e.Code)) ])
                    | Error errors, Refuse code ->
                        Error($"{s.Key} step {i} was refused, but not with {code}: " + String.Join("; ", errors |> List.map (fun e -> e.Code)))))
            (Ok(None, [], []))
        |> Result.bind (fun (state, events, refusals) ->
            match state with
            | Some inv when inv.Lifecycle = s.ExpectedLifecycle -> Ok { Scenario = s; Events = events; Refusals = refusals }
            | Some inv -> Error($"{s.Key} ended {inv.Lifecycle}, expected {s.ExpectedLifecycle}")
            | None -> Error(s.Key + " produced no investigation"))
