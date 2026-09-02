# Customer First Responder channel rollout

**Related work:** [AB#11409 - IcM Intake, Routing & Escalation](https://dev.azure.com/dnceng/internal/_workitems/edit/11409) | [AB#12383 - Teams-to-IcM pilot](https://dev.azure.com/dnceng/internal/_workitems/edit/12383)

## Recommendation

Create a dedicated customer intake channel in the `Partners` Team where each new root thread
automatically creates a Sev4 IcM and receives an in-thread link to the incident. Keep the existing
First Responders channel unchanged as the place for discussion, questions, and collaborative
troubleshooting.

This model gives customers a low-friction way to request tracked First Responder (FR) help without
requiring them to navigate the IcM portal. It also avoids turning every conversation in the
existing channel into an incident.

## Evidence

Customer research conducted August 27, 2026 received responses from 13 of 14 stakeholders.
Preferences varied, but the recurring requirements were consistent:

- Do not automatically convert every existing FR discussion into an incident.
- Keep the Teams conversation and resulting IcM visibly connected.
- Do not require customers to use the IcM portal to request help.
- Preserve a low-friction place for questions and collaborative troubleshooting.

The September 1, 2026 proof of concept demonstrated that the proposed intake workflow can:

- create exactly one IcM from a new root thread;
- route the incident to DDFun Customer Requests;
- post the incident link back to the originating thread; and
- prevent duplicate incidents when polling windows overlap.

The proof of concept established technical feasibility. Production rollout remains gated on an
approved non-personal Teams identity, deployment of the required code and authorization changes,
and confirmation that FR/Ops can support the expected intake volume.

## Channel operating model

| Channel | Customer use | FR behavior |
| --- | --- | --- |
| Existing First Responders channel | Questions, discussion, and collaborative troubleshooting | No automatic incident creation; current experience remains unchanged |
| New FR intake channel | Requests that need tracked FR ownership | Each new root thread creates one Sev4 IcM and receives one in-thread incident link |

Replies do not create additional incidents. FR/Ops triages the resulting incidents through the
existing DDFun Customer Requests process. Final production severity, routing, response
expectations, and escalation guidance must be confirmed before the customer pilot.

## Rollout

| Phase | Activities | Exit criteria |
| --- | --- | --- |
| **1. Production hardening** | Confirm an approved non-personal Teams identity; merge and deploy the provider authorization and dnceng deployment changes; confirm routing, monitoring, and rollback | Identity and permissions approved; dependencies deployed; end-to-end production test succeeds |
| **2. Internal readiness** | Create the intake channel in a restricted state; brief FR/Ops; test success, failure, duplicate-prevention, monitoring, and rollback paths | Each test thread creates one IcM and one reply; no incidents are created from replies; FR/Ops confirms readiness |
| **3. Customer pilot** | Invite a subset of the original research participants; monitor reliability, volume, latency, and feedback during an agreed pilot window | No unresolved automation failures; support volume is sustainable; pilot feedback supports broader rollout |
| **4. Broad launch** | Open the intake channel to the intended customer audience; publish and pin usage guidance; monitor early adoption closely | Success measures remain healthy through the agreed observation period |
| **5. Steady state** | Review usage, reliability, routing, and customer feedback with FR/Ops | Improvements are tracked through the normal FR/Ops backlog |

## Announcement plan

| Stage | Audience and channel | Message and feedback |
| --- | --- | --- |
| **Internal readiness** | Leadership and FR/Ops through a team sync and written briefing | Explain the two-channel model, operational ownership, launch gates, monitoring, rollback, and expected customer guidance. Resolve readiness questions before internal testing. |
| **Customer pilot notice** | Selected research participants through a targeted Teams message | Explain the purpose of the intake channel, what creates an IcM, when to use the discussion channel instead, and how pilot feedback will be collected. |
| **Broad customer launch** | All intended FR customers through a pinned post in the existing channel and updated guidance | Lead with the customer benefit: tracked help can be requested directly from Teams. Include simple examples, response expectations, and reassurance that the existing discussion channel is not being retired. |
| **Post-launch follow-up** | Customers and FR/Ops through a dedicated feedback thread and the regular operational review | Collect friction points, communicate material changes, and adjust guidance or routing based on observed usage and feedback. |

The rollout lead owns the internal briefing. A named communications owner must prepare the pilot
and launch messages, coordinate timing with FR/Ops, and publish the final guidance. These owners
and dates must be assigned before Phase 2 begins.

## Success measures

- Every eligible intake thread creates exactly one IcM and one in-thread incident link.
- No incidents are created from replies or duplicate polling of the same root thread.
- Creation latency remains within a target agreed before the customer pilot.
- FR/Ops confirms that intake volume and routing are sustainable.
- Pilot customers report that the new path is clear and easier than the current process.
- Existing discussion-channel participation does not materially decline after launch.

## Decisions needed

1. Approve the separate discussion-and-intake channel model.
2. Assign the rollout lead, communications owner, and FR/Ops operational owner.
3. Confirm the production identity, severity, routing, response expectations, and escalation path.
4. Select the customer pilot participants and define the pilot and observation periods.
5. Set pilot and broad-launch dates after the production gates are satisfied.
