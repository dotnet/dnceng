# Image Creation Subsystem — Project One-Pager

**Status:** Updated draft. Supersedes [image-creation-subsystem-dnceng-10788.prior.md](./image-creation-subsystem-dnceng-10788.prior.md). Reflects decisions taken through the May 2026 iteration on schema, promotion gating, `latest` semantics, DR backup, and Matrix-of-Truth carve-out obligations.
**Scope:** Producing baked images and publishing them to a gallery. Plus a constrained internal test-rig VMSS deployer used solely to evaluate test images. Plus continued maintenance of the existing on-prem onboarding bundle as a working assumption (treated as a separable concern).
**Predecessor:** [v1 proposal docs](../) (April 2026 layered-deployment proposal).

**Project framing — greenfield, not migration.** This is a **new replacement system designed in parallel** to today's image-creation logic inside `dotnet-helix-machines`. The existing system is a **reference for requirements and behavior**, not a base to convert. There is no backward-compatibility obligation to the current code, schemas, pipelines, or APIs. Cutover replaces the existing functionality; it does not evolve it. Wherever this doc cites today's code, it is to anchor a requirement, not to imply lift-and-shift.

**Terminology used in these docs:**
- **"Baked in"** — anything (binaries, configuration, *or* scripts) added to a base image to produce a new gallery image. Scripts placed on disk at bake time and executed later are still "baked in."
- **Image Instance Initialization Routine (IIIR)** — code that runs at first boot on each VM instance (today: `first-run.bat` / `first-run.sh`, or the 1ES Hosted Pool `Setup.ps1`). Verified to perform configuration, identity, and activation only — no software installation. ([b5 IIIR addendum](./current-state/b5-addendum-iiir-install-check.md).)
- **Deployment validation** — automated post-bake checks that confirm each artifact installed successfully and at minimum runs ("exists and is running"). Owned by this subsystem and runs on every build.
- **Design validation** — optional, human-driven evaluation of a test-gallery image by a developer who needs hands-on access to a VMSS instance running the image. Performed before approval/promotion, not as part of the deployment validation gate.
- **Mapping file** — the source-controlled `imageName → latestVersion` file this subsystem publishes as its primary consumer contract. Consumers ingest the mapping file as a build artifact, diff it, and explicitly select when to pick up a new image version. There is no auto-updating `latest` alias inside the gallery.
- **Requester** — the team alias or person who originally requested an image be created. Tagged onto every image we publish for accountability and routing. Our team is the *owner* of all images; the requester is *why the image exists*.

---

## Project Goals

Re-architect the image-creation responsibilities currently inside `dotnet-helix-machines` as a **standalone subsystem whose only output is baked, validated, versioned image entries in a gallery, addressable via a published mapping file**. Other subsystems read the mapping file (and optionally the gallery directly) and decide what to do with the images.

The redesign also targets:
- **Easy, fast internal turnaround on new images.** When a customer requests a new image, the team that owns image creation can produce a test-gallery image to evaluate quickly — without a code-change cycle and without exposing image authoring directly to the customer.
- **A visual interface** for image definition, test-gallery generation, **and active-test-VM management** (view active test VMSS instances, see which image generated them and when they expire, expire them early). Used by the image-creation team. Internal-facing, not customer-facing.
- **Known, repeatable validation processes:** **deployment validation** for every build (artifacts installed and minimally run), separate from **design validation** which happens before promotion at human discretion. Plus re-validation of artifacts across every base they are registered against when install logic or parameter schema changes.
- **A constrained internal test-rig VMSS deployer** so test-gallery images can be instantiated and evaluated end-to-end (including IIIR execution) before promotion. This is the only VMSS-related scope this subsystem owns; production VMSS deployment is owned by a separate consumer-side workstream.
- **Human-gated batch promotion.** After cascading rebuilds finish, a human reviews the per-image pass/fail summary and decides whether to promote *all images that passed*, or hold the entire batch because of failures. No automated promotion.

Image creation will continue to be our responsibility. 1ES is unlikely to take it over for us; the realistic win is to **leverage more 1ES tooling than we do today**, particularly newer Image Factory v3 endpoints (we call the HTTP API, not the long-stale SDK 1.0.62 — that framing is misleading).

## What This Subsystem Owns

- **Image definitions** in source control — declarative, reviewable, diffable, JSON. An image definition is `baseImageRef + ordered list of (artifactRef, parameterValues) + target designator (cloud | onprem) + requester + description`, *and nothing else*. It does not know which queue, pool, or consumer might use it. Each image definition lives in its own file; the filename (without extension) is the canonical published name (gallery image name for `cloud`; per-queue blob name for `onprem`). There is no `Name` field inside the file — the filename is authoritative.
- **The artifact catalog** — every artifact is a self-contained install unit, runnable at image build, with a first-class **parameter schema** (named params, type, required/optional, default). Artifacts have **no version field**; if version-like behavior is needed it lives as a parameter value. **No application-mode dial; everything bakes in.** Artifacts that need per-VM behavior ship a baked-in IIIR script (binaries baked in, script baked in). The IIIR script does not install software — it configures, fetches identity-scoped secrets, and activates baked-in services.
- **The compatibility registry** — a separate, first-class, simple data store of `(artifactId, baseImageId)` rows. The *only* source of truth for which artifacts are allowed on which bases. It is **not** defined inside image definitions and **not** inside the artifact catalog. The visual interface and the validation pipeline both read from it. Entries are added or removed only via feature changes that run the artifact's validation against that base; removal is blocked while any image definition references the pair.
- **Cloud gallery images** — versioned entries in an Azure Compute Gallery, produced by one uniform build pipeline (no special cases per queue, per consumer, or per source SKU). This is the primary deliverable.
- **The published mapping file** — a source-controlled `imageName → latestVersion` file checked in as the canonical consumer contract. Updated when an image is promoted. Consumers ingest as a build artifact, diff against their own copy, and pick up changes purposefully. Operational rollback = revert one entry in this file (no PR cycle required for the rollback; one-line update + commit + publish). There is no auto-updating `latest` alias in the gallery itself.
- **On-prem (iron) onboarding bundle** — a separate concern, *not* an OS image. Today's `dotnet-helix-machines` publishes a per-queue config + artifact zips + driver + bootstrap + Arc-onboarding scripts to blob, exercised end-to-end on dedicated CI test machines as part of release. See [b8 on-prem machine handling](./current-state/b8-onprem-machine-handling.md). The new image-creation subsystem inherits the working assumption that this responsibility continues here because no other team is currently positioned to absorb it. Treated as structurally distinct from gallery image creation and a candidate to peel into a separate subsystem or hand off to a partner team over time.
- **Deployment validation** of each image after build — verifies every artifact installed and runs minimally. Plus re-validation when an artifact's install logic or parameter schema changes (driven by the compatibility registry).
- **Publication** of validated images to two galleries:
  - **Test gallery** — frequent; published from the visual interface or pipeline; no code change to the official system required; entries age out automatically (target: ~30 days, or sooner if usage can be determined and an image was never used).
  - **Official gallery** — gated; only fed by human-approved promotion of validated images. Entries can only be removed manually and intentionally, never by automation or rollout.
- **DR backup gallery** in a different Azure region (e.g., `eastus2` while primary is `westus2`/`westus3`). Replicates every official-gallery version. Retention: keep at minimum `N` recent versions and never delete anything under `M` months old (concrete numbers TBD with cost data, but always larger than the rollback window we want to support). Comes with a **documented restore runbook + script** that reconstructs an official gallery in a recovery region from the backup + current mapping file.
- **Cascading rebuilds** when an artifact, base image, or image definition changes — every dependent image gets rebuilt with per-image pass/fail reporting and partial-success tolerance. The cascade ends with a **human review gate**: the operator sees the pass/fail summary and either promotes the passing subset, or holds the entire batch.
- **Safe, incremental propagation** of routine OS / artifact / security updates — same cascading-rebuild pipeline; the "safe" and "incremental" part is the human-gated batch promotion (partial promotion of the passing subset is acceptable; full promotion only with explicit approval).
- **Constrained internal test-rig VMSS deployer** — single-instance, no autoscaler, fixed lifetime, separate templates / scope, identity-based RBAC (no shared keys). Used solely to instantiate test-gallery images for evaluation. Test instances are easily reclaimed, manually or automatically (~30 days), and surfaced through the visual interface's VM-management tab.
- **The IIIR script content** (baked in) and the **IIIR contract** (entry point, arguments, exit semantics).
- **Image cleanup** — orphaned-version detection and operator-approved deletion for the test gallery; manual-only deletion for the official gallery.
- **Image-side static-data catalogs** that today live under `matrix-of-truth/`:
  - `os-definitions.json` — OS lifecycle facts (EOL, supported .NET versions). We pick which OSes to image; we already curate this.
  - `on-prem-hardware-specs.json` — physical hardware inventory for iron pools. Moves with the on-prem bundle scope; there is no other home for it today.
- **Emitting image / definition / artifact / publish data in a stable, consumable shape** for cross-system reporting tools (today's "Matrix of Truth" or a successor) to read. We own the *data we emit*; we do not own the reporting tool.

## What This Subsystem Does *Not* Own

- **Production VMSS deployment, scaling, extensions** — including the Custom Script Extension (today) or the 1ES Hosted Pool provisioning hook (target) that *invokes* the IIIR. We own the script content and the contract; consumers own the trigger.
- **VMSS SKU / `vmSize` / `UpgradePolicy` selection.** The image must work on whatever SKU the consumer picks. SKU choice is a consumer/VMSS concern.
- **Per-VM identity, per-VM secrets, per-VM cert provisioning.** Today every VM in a VMSS shares its scale-set managed identity; this subsystem does not change that and does not introduce a per-VM-uniqueness mechanism. If a consumer needs that in the future, it is consumer-side.
- **Job queue dispatch, retry, redistribution, metrics.**
- **The Helix client** (other than possibly being packaged as an artifact).
- **Physical-machine OS imaging and hardware lifecycle.** External (per current understanding: MLS via BotDeploy / UIP). We do not produce iron OS images today, and the new system does not plan to.
- **Any operational policy about which queue or pool consumes which image.** Consumer concern.
- **No-bake, Marketplace-pass-through images.** Every image we publish has baked-in content; we are a curated gallery of custom images. Consumers that want a stock Marketplace image with no customization reference Marketplace directly — outside our scope.
- **How images are used after publish.** Which queues / pools / customer build pipelines reference an image, how often, with what runtime outcome — all consumer-side telemetry.
- **The pool/MMS-hosted-pool-image catalog (`mms-images.json`).** That documents VMSS / hosted-pool consumption relationships, not image creation. Stays with whoever owns pools after carve-out.
- **The "Matrix of Truth" cross-system reporting tool** (today hosted in `dotnet-helix-machines`). It aggregates many data sources. The image-creation subsystem is one of several data *sources* it reads; the tool itself is not part of image creation. Our obligation is one-way: keep emitting our image-side data in a shape it can ingest.

## Design Requirements

- **Standalone.** No code reference from this subsystem to scaling, queues, production VMSS deployment, or client implementations. (The internal test-rig is the only VMSS code in this subsystem and is intentionally non-reusable as production plumbing.)
- **Stable consumer contract = the published mapping file (plus the gallery it points into).** Consumers ingest `imageName → latestVersion` as a build artifact, diff it, and explicitly pick up changes. No auto-update path; no surprise to a VMSS the next time it scales. Direct gallery reads at specific versions are also supported for consumers that want to pin.
- **Image definitions independent of consumer definitions.** An image definition can be built, validated, and published without any consumer being defined at all. Many consumers can reference one image; one consumer can reference many images.
- **One build pipeline, two deliverable kinds.** Uniform "base + artifact list → published image"; the deliverable kind is determined by target (cloud gallery vs on-prem bundle), not by a parallel pipeline.
- **Test-gallery production needs no code change.** The image-creation team iterates on an image definition (or an artifact) and publishes to the test gallery via the visual interface.
- **Official-gallery production is human-gated.** Cascading rebuilds run end-to-end against the official codebase; the operator then reviews the per-image pass/fail summary and decides whether to promote the passing subset or hold the entire batch. No automated promotion.
- **Operational rollback = mapping-file revert.** Reverting a single line in the mapping file is the global rollback action. Consumers re-pick on their next build cycle. Per-consumer pinning (consumer addresses an explicit gallery version) remains available as the per-consumer escape hatch.
- **Cascading updates are first-class.** Changing an artifact triggers validation against every supported base image and rebuilds every official image that consumes it. Failures are isolated and reported per image; the human gate decides what to do with the result.
- **Image authoring is internal-team-only.** Customers request new images via whatever channel (Teams, email, meeting, work item); they do not author them directly. Any code-change cycle requires an associated work item that captures *why*.
- **JSON-only for subsystem data.** All source-controlled subsystem data &mdash; image definitions, base-image catalog, artifact catalog (including parameter schemas), compatibility registry, mapping file, our image-side static-data files &mdash; is serialized as **JSON**. YAML and XML are not used for these. The only acceptable carve-outs are external-tool config files where the tool dictates the format (e.g., Azure Pipelines `*.yml`, Bicep templates). The query / MCP API also emits and accepts JSON only.
- **Disaster recovery is a first-class capability.** Backup gallery in a different region + documented + periodically dry-run restore runbook.
- **Pipeline security inherits team standards.** SDL, TSA, PoliCheck, Component Governance via 1ES Pipeline Templates — same posture as every other team pipeline.
- **Leverage newer 1ES tooling.** Re-evaluate Image Factory v3 endpoints (we already invoke v3 over HTTP; the "SDK 1.0.62" framing is misleading).

## Data Shapes

**Image definition (JSON, one file per image, filename = canonical published name):**

```json
{
  "target": "cloud",
  "baseImageRef": "Windows-Server2022-21H2-AMD64",
  "requester": "dnceng",
  "description": "Base Windows 2022 build queue image for .NET runtime team.",
  "artifacts": [
    { "ref": "windows-vc-redist", "parameters": { "version": "14.50" } },
    { "ref": "windows-helix-bootstrap", "parameters": {} }
  ]
}
```

**Compatibility registry (one JSON file, or one file per artifact — final form is a small design call):**

```json
[
  { "artifactId": "windows-vc-redist",       "baseImageId": "Windows-Server2022-21H2-AMD64" },
  { "artifactId": "windows-helix-bootstrap", "baseImageId": "Windows-Server2022-21H2-AMD64" }
]
```

**Published mapping file (source-controlled, this is the consumer contract):**

```json
{
  "Windows.10.Amd64.Server22H2":      "2026.0527.143055",
  "Linux.Ubuntu.2204.Amd64":          "2026.0527.143112",
  "Mac.iPhone.17.Perf":               "2026.0525.091020"
}
```

## How This Differs From Today's `dotnet-helix-machines`

| Aspect | Today | Proposed (Image Creation Subsystem) |
|---|---|---|
| Scope | One repo / pipeline does images, artifacts, queue-deploys, scaling | One subsystem does only image creation, gallery publication, mapping-file publish, and a constrained internal test-rig |
| Coupling to scaling / queues / production VMSS | Directly intertwined | Zero direct coupling — the mapping file is the contract |
| Image definitions | Tightly bound to queue/pool definitions; per-queue derivative images proliferate | Image definitions are first-class and independent of any consumer; consumers reference them via the mapping file |
| Data format | YAML (with anchors) mixed with JSON | JSON only for subsystem data |
| Build pipelines | Multiple bespoke paths (cloud-VMSS-custom, hosted-pool, iron, etc.) | One uniform pipeline; two deliverable kinds (cloud gallery image, on-prem bundle) |
| Promotion | Pipeline runs to completion; broken images block the rest | Cascading rebuild → human review → promote passing subset *or* hold all |
| "latest" semantics | Re-running a release picks up newest sources implicitly | Explicit mapping-file entry per image; consumers diff and decide |
| Image rollback | Requires PR + redeploy of the source release | Revert one line in the mapping file; consumers re-pick on their next build cycle |
| Backup posture | Same-region operational buffer with no documented restore path | Different-region DR gallery + documented + dry-run-tested restore runbook |
| 1ES usage | Image Factory v3 HTTP; hosted pools used only for build CI | Re-evaluate; aim to leverage newer Image Factory capabilities |
| Marketplace-pass-through images | Threaded through our codebase as a queue option | Out of scope; consumers reference Marketplace directly if they want that |
| Test images | Require code changes to the official system | Authored via visual interface; published to a separate test gallery; age out after ~30 days |
| Test instance evaluation | Consumer must build their own test plumbing | Internal test-rig deploys test-gallery images on a constrained VMSS for design-validation, including IIIR execution, with a UI for active-VM management |
| Cascading updates | All-or-nothing | Per-image partial-success tolerated, human-gated batch promotion |
| Iron / on-prem hardware | We are middle-man for full lifecycle; OS imaging is external; we publish an onboarding bundle with no explicit update confirmation | Same onboarding bundle continues as a working assumption; explicit update-confirmation signal is a forward-looking goal (best discussed with the partner team) |
| Failure blast radius | One bad image blocks everything | Image failures isolate; passing subset can still ship after human review |
| Customer involvement | Customer-driven changes flow through the official codebase | Customers request images; team produces test images for evaluation; image authoring stays internal |
| Matrix of Truth | Hosted inside `dotnet-helix-machines` alongside image code | Out of scope for image creation; we continue to emit the image-side data + own `os-definitions.json` + `on-prem-hardware-specs.json` |

## Benefits

- **Smaller blast radius.** Image failures isolate; nothing in scaling / queues / client / production VMSS is impacted, and within image creation itself a failed image no longer blocks the rest.
- **Faster internal turnaround on test images.** Image-only and artifact-only changes don't trigger queue or scaler work, and test-gallery production is a visual-interface action rather than a code change.
- **No-surprise consumer pickup.** Consumers diff the mapping file and choose when to absorb a new image version — no auto-update can break a VMSS the next time it scales.
- **Better security posture.** Routine OS / artifact security updates flow through the same cascading-rebuild pipeline, with explicit per-image pass/fail and a human gate.
- **Cleaner accountability.** This subsystem owns its logs, metrics, failure modes, and its rollback story (revert a mapping-file line). Consumers own their own deployment-rollout decisions.
- **Real disaster recovery.** A cross-region backup gallery + documented restore runbook gives us a posture today's same-region backup-with-no-restore-path does not.
- **Reduced bespoke surface.** Adopt newer 1ES capabilities to shrink what we maintain.
- **On-prem responsibilities are explicitly separable.** Today's on-prem onboarding bundle is preserved as a working assumption, but the new architecture treats it as a distinct concern from gallery image creation — opening the door to either a partner hand-off or a successor subsystem with explicit update-confirmation signals.

## Open Items

These drive the [investigations](./initial-investigations.md). External conversations remain; internal research (current-state, schema audit, MoT inputs, backup rationale) is complete.

- **A1a — On-prem partner identity.** Confirm MLS (or actual hardware-owning team) as the operational partner, re-onboarding cadence, willingness to support an explicit update-confirmation signal + take customer intake for iron pools. Greg owns.
- **A1b — DDFUN's actual current iron role.** Definitive yes/no so framing docs stop overstating it.
- **A2 — Announce the output contract** (mapping file + gallery + on-prem bundle) to the VMSS-deploy, scaling, queues, and client workstreams. Announcement, not negotiation.
- **Compatibility-registry storage form.** Single JSON file vs. one file per artifact — small design call before C1 freezes the schema.
- **`mms-images.json` ownership.** Likely stays with whoever owns pools, not us. Confirm with the consumer-side workstream as part of A2.
- **On-prem update-confirmation signal design.** Best discussed with the partner team identified in A1a; fallback if nothing better lands is the existing "update the blob; watch for heartbeats from machines in known pools after reboot" pattern.
- **Backup retention numbers.** `N versions` and `M months` to be filled in once we have cost data; until then the rule is "always larger than the rollback window we want to support."
- **Visual-interface stack choice.** Open (Power Apps, small Blazor app, ADO pipeline with runtime parameters as a v0). Decide at implementation time.

---

**See also:** the supporting documents for this proposal should be attached as resources to work item [DNCENG #10788](https://dnceng.visualstudio.com/internal/_workitems/edit/10788):

- Technical Objectives
- Known Challenges
- Initial Investigations
- Current-state review — factual baseline (B1–B8 + addenda)
- Diagrams — image-creation flows, existing-system flows, side-by-side comparison
- Option 1 vs Option 2 comparison
- v1 proposal documents — prior layered-deployment design
- Prior one-pager — superseded
