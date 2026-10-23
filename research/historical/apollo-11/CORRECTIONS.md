# Apollo 11: corrections log

This log records every change made in response to `adversarial-review.md` (2026-10-23).

- Claim ids are stable.
- Existing claims were changed only where the review identified a defect.
- New sources are A11-S24 to A11-S27.
- New claims are A11-C101 to A11-C120.

## Verification

- Every new or changed excerpt was re-checked against freshly extracted source text.
- Mission Report PDF pages 46, 164, 180, 181, 188 and 189 were rendered as images and read visually.
- The fuel figures were re-checked against those rendered pages. A11-C058 is corrected to 116 / 45 / 20 / 0 s.
- These fuel claims were confirmed unchanged: A11-C057 (102:44:28, Table 5-I), A11-C059 (45 s vs 50 s), A11-C060 (251 + 519 = 770 lb; 957 lb planned) and A11-C061.

## Sources added

| source id | review id | change |
|---|---|---|
| A11-S24 | A11-R09 | New **secondary** record for ALSJ *The First Lunar Landing* editorial and contributor commentary (same URL as A11-S06). |
| A11-S25 | A11-R09 | New **secondary** record for ALSJ *The Return to Orbit* commentary (same URL as A11-S09). |
| A11-S26 | A11-R09 | New **secondary** record for ALSJ *One Small Step* commentary (same URL as A11-S07). |
| A11-S27 | A11-R08 | Press kit part 2 (primary). It was searched for LM descent-abort content and none was found. |

Real-time transcript lines stay on the primary records A11-S04, A11-S06, A11-S09, A11-S10, A11-S13 and A11-S15.

## Claim changes

| claim id | review id | field | before → after summary |
|---|---|---|---|
| A11-C058 | A11-R01/A11-R19 | statement | engine cutoff '5' -> '45' s remaining (rendered page), go/no-go 20 s, depletion 0 s |
| A11-C058 | A11-R19 | locator | 'Section 9 (descent propulsion) pp. 9-23 to 9-24' -> '§9.8.3 Gaging System Performance, p. 9-23, figure p. 9-24' |
| A11-C058 | A11-R01 | notes | removed 'column alignment approximate' caveat; added rendered-image confirmation and arithmetic check |
| A11-C022 | A11-R06 | statement | 'Duke acknowledged' (counted among Go calls) -> explicitly an acknowledgment, not a Go |
| A11-C022 | A11-R06 | notes | removed claim that 'You're GO' preceded it as part of the alarm response; added pairing by timing |
| A11-C100 | A11-R06 | statement | open pairing question -> pairing largely settled by timing; fifth alarm unacknowledged |
| A11-C100 | A11-R06 | sourceId | A11-S06 (ALSJ) -> A11-S04 (1969 technical transcript) |
| A11-C100 | A11-R06 | locator | GET 102:42:41 (ALSJ) -> TEC page/GET range |
| A11-C100 | A11-R06 | excerpt | ALSJ combined line -> TEC line |
| A11-C100 | A11-R06 | historicalTime | 102:42:41-derived -> 04 06 42 58-derived |
| A11-C100 | A11-R06 | timeBasis | updated to TEC GET |
| A11-C100 | A11-R06 | notes | rewritten for settled pairing and residual uncertainty |
| A11-C093 | A11-R07 | statement | 'Martin frames crew checklist error' -> Martin = procedure/training framing; 'checklist error' = Eyles's earlier wording; Fjeld = only crew-mistake framing |
| A11-C093 | A11-R07 | notes | updated sides; separated crew-deviation from procedure-configuration |
| A11-C042 | A11-R07 | notes | added: procedure/training framing, not crew error |
| A11-C043 | A11-R07 | notes | added: only crew-mistake framing found |
| A11-C094 | A11-R05 | statement | Eyles no longer described as recalling an instruction minutes before lift-off; MR p. 4-17 added as third side |
| A11-C094 | A11-R05 | notes | rewritten: Eyles timing treated as evidence the fix preceded the diagnosis |
| A11-C091 | A11-R11 | notes | 'Martin's 80% is a real-time impression' -> post-landing ground-analysis impression (1994 recollection) |
| A11-C048 | A11-R12 | statement | 'the Lab had never seen' -> 'Martin personally had never seen' |
| A11-C048 | A11-R12 | notes | added first-person scope and Silver/LM-3 counterpoint |
| A11-C018 | A11-R18 | statement | 'about 31 seconds after the computer-determined alarm time' -> 'about half a minute' (mixed clock bases) |
| A11-C075 | A11-R23 | historicalTime | null -> 1969-08-10T00:00:00.000Z |
| A11-C075 | A11-R23 | timeBasis | null -> 'date only' |
| A11-C027 | A11-R09 | sourceId | A11-S06 (primary journal record) -> A11-S24 (secondary commentary record) |
| A11-C043 | A11-R09 | sourceId | A11-S06 (primary journal record) -> A11-S24 (secondary commentary record) |
| A11-C063 | A11-R09 | sourceId | A11-S06 (primary journal record) -> A11-S24 (secondary commentary record) |
| A11-C064 | A11-R09 | sourceId | A11-S06 (primary journal record) -> A11-S24 (secondary commentary record) |
| A11-C087 | A11-R09 | sourceId | A11-S06 (primary journal record) -> A11-S24 (secondary commentary record) |
| A11-C089 | A11-R09 | sourceId | A11-S06 (primary journal record) -> A11-S24 (secondary commentary record) |
| A11-C095 | A11-R09 | sourceId | A11-S06 (primary journal record) -> A11-S24 (secondary commentary record) |
| A11-C097 | A11-R09 | sourceId | A11-S06 (primary journal record) -> A11-S24 (secondary commentary record) |
| A11-C098 | A11-R09 | sourceId | A11-S07 -> A11-S26 (secondary commentary record) |
| A11-C053 | A11-R09 | sourceId | A11-S06 (ALSJ commentary quoting debrief) -> A11-S16 (the 1969 debrief itself) |
| A11-C053 | A11-R09 | locator | ALSJ commentary locator -> debrief page |
| A11-C053 | A11-R09 | notes | re-pointed to primary debrief |
| A11-C097 | A11-R02 | notes | added: slosh/hidden-margin framing is commentary; MR agreement within 5 s (A11-C115) |
| A11-C083 | A11-R03 | notes | added better evidence and 'consistent with' framing |
| A11-C090 | A11-R06 | notes | added pointer to new P64 'each case' contradiction |
| A11-C101 | A11-R(new) | (new claim) | Duke told the crew they were looking great and Go, shortly after the 1201 and before the two late 1202 alarms. |
| A11-C102 | A11-R(new) | (new claim) | Evans framed the ascent radar-off change as a hedged working hypothesis: 'we think' it would take care of 'som |
| A11-C103 | A11-R(new) | (new claim) | Aldrin pushed back that the rendezvous radar had been in Slew during descent. |
| A11-C104 | A11-R(new) | (new claim) | Aldrin later characterized his 121:42:05 reply as arguing with the CAPCOM. |
| A11-C105 | A11-R(new) | (new claim) | Eyles states that George Silver's diagnosis reached the MIT representatives in Houston on the morning of July  |
| A11-C106 | A11-R(new) | (new claim) | The pilots' report states that, per ground instructions, the rendezvous radar was placed in the antenna SLEW p |
| A11-C107 | A11-R(new) | (new claim) | The Mission Report states the descent Executive overflow alarms are now known to be normal for the existing si |
| A11-C108 | A11-R(new) | (new claim) | An unexpected 520 (RADAR RUPT) alarm occurred at LGC power-up; it was reproduced on the ground, caused by a ra |
| A11-C109 | A11-R(new) | (new claim) | Two altitude-difference transients during the computer alarms were apparently associated with incomplete radar |
| A11-C110 | A11-R(new) | (new claim) | During descent and ascent the Abort Guidance System ran in monitor mode while the primary guidance system cont |
| A11-C111 | A11-R(new) | (new claim) | No lunar-module powered-descent abort description was found in either part of the retrieved press kit; its Abo |
| A11-C112 | A11-R(new) | (new claim) | Armstrong later wrote that near touchdown he would rather accept a drop to the surface than attempt the abort  |
| A11-C113 | A11-R(new) | (new claim) | ALSJ commentary states the powered descent burn lasted 36 s longer than planned, almost all due to flying beyo |
| A11-C114 | A11-R(new) | (new claim) | ALSJ commentary states that of about 770 lb remaining at landing about 100 lb would have been unusable, and th |
| A11-C115 | A11-R(new) | (new claim) | CONTRADICTION (size of slosh effect): the Mission Report says the indicated 45 s to depletion differed from th |
| A11-C116 | A11-R(new) | (new claim) | Eyles states a second, less well-known descent problem: erroneous data made the descent engine thrust fluctuat |
| A11-C117 | A11-R(new) | (new claim) | At docking, LM thrusting continued after capture until a misalignment excursion had developed because the Comm |
| A11-C118 | A11-R(new) | (new claim) | Eyles defines the alarm codes: 1202 when no core sets were available to schedule a job, 1201 when no VAC areas |
| A11-C119 | A11-R(new) | (new claim) | Eyles cites a post-flight Grumman (Clint Tillman, 9 August 1969) test that gave 13.36% maximum TLOSS and repro |
| A11-C120 | A11-R(new) | (new claim) | CONTRADICTION (P64 Go calls): Eyles writes that Mission Control 'called a go in each case' for the P64 alarms, |

## Not changed / notes

Some review findings target `tools/src/Causa.Tool/Historical/Apollo11.fs` and were left untouched. This workstream may not touch that file. They are:

- A11-R02, A11-R03, A11-R05 and A11-R06: the hypothesis and conclusion wording.
- A11-R08: the `hAbort` role.
- A11-R13: `hMonitor`.
- A11-R16: the C053 relation.
- A11-R20: the roles.
- A11-R22: the prediction outcome.

The research-side evidence those fixes need has been added (A11-C101 to A11-C120), together with the corrected notes.

A11-R07 asked for a Fjeld crew-mistake claim if none existed. One already exists: A11-C043 is the only crew-mistake framing found, and its notes now say so. No duplicate was added.

A11-C065's locator still reads "PDF p. 165 region". The review confirms the passage is on debrief p. 9-25 (PDF p. 164). It was not flagged as a defect, so it is left as is.
