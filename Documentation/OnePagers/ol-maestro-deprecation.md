# Deprecating Ol' Maestro

## Summary

Well, I reckon it's high time we put ol' Maestro out to pasture. Yessir, it's been a good run, but there's a new breed of tech that's fixin' to take its place. This here newfangled contraption ain't just for show; it's built for a purpose, sturdy and true. It'll cut down the heavy load of keepin' things in line and watchin' over our shoulders for trouble. That's right, it'll ease the burden of makin' sure everything's shipshape and Bristol fashion, security-wise.

### Ol' Maestro's current functionality

Fundamentally, all Maestro does is launch build pipelines with prescribed parameters. These build pipelines do a couple things that are important to building and shipping .NET.
- Mirroring code from GitHub to AzDO for a given set of repos and branches.
- Opening PRs that merge from one branch to another.

## Stakeholders

* .NET Core Engineering Services team (contact: @dnceng)
* .NET Product teams
* Go team (mirror only)

## Risks

- Failures of a new mirror could result in lost productivity or delays in releases.
- There is some unknown use of ol' Maestro that we will discover.

## Open Questions

- What will be the UX/onboarding process be for inter-branch merge functionality?

Inter-branch merge functionality will likely move into the repos needing the functionality. How will repo owners indicate a new sequence of merges? How will they remove an old sequence that is no longer needed?

## Components to change, with order/estimates of work to do

### Component: Code mirror

Ol' Maestro keeps track of a list of repos and branches that need mirroring (1:1 mirror and internal/ merge mirroring). When these branches are changed, pipelines are launched which do this mirroring for the repo+branch combo. This has a few downsides:
- Failures to mirror can get buried in a list of failed pipelines.
- Recovery from failures (e.g. AzDO is down and we can't launch pipelines) requires either manually launching the mirror, or waiting until new commits trigger the mirror.
- With one pipeline invocation per push, VM usage is higher than is probably required.

A new mirror should be developed with the following qualities:
- Should be built and run (vs. built and deployed)
- Should be a standard .NET application that is launched by a pipeline.
- Should deal with *all* branches and repos at the same time.
- Should use a poll/pull model, rather than waiting for events from GitHub. This reduces onboarding cost (no need to create webhooks)
- Should read the list of branches and repos to mirror from a repo in AzDO.
- Should use GH and AzDO APIs to determine the state of repos on either side of the mirror to determine whether repo changes need to happen.
- Should use a MI to do mirror pushes to AzDO and fetches from AZDO.
- Should report changes as well as failures to mirror.
- Should not abort if a single mirror operation fails (complete and report at end).
- Should operate in parallel with a throttling mechanism.
- When the current pipeline completes, a new one should launch.

### Component: Inter-branch Merge GitHub Action

A GitHub Action should be developed that replaces the inter-branch merge functionality. It should have the following qualities:
- Should open a PR in specified 'downstream' branches when a commit to a branch is made.
- Should should follow the similar conventions and UX as the current inter-branch merge PRs.
- Should be reusable in any .NET repository.

## Serviceability

* How will the components that make up this epic be tested?

The code mirror should have unit tests written for it. These should follow a standard mockup model to avoid actual git operations. The inter-branch merge functionality may be tested in the maestro-auth-test org if necessary.

* How will we have confidence in the deployments/shipping of the components of this work?

We can operate the new components and ol' Maestro in parallel for a time to verify that everything operates as expected.

## Rollout and Deployment

* How will we roll this out safely into production?

We will first enable the code mirror in parallel with the existing mirror. When we are satisfied it works as expected, we will remove the old mirror triggers from the versions repo. The inter-branch merge action should be rolled out to a few repos first (in parallel to existing flows), then when we are satisfied with it, remove the versions repo triggers.

Once ol' Maestro is no longer providing functionality, it will be shut down and the code removed.

## FR Handoff

* What documantion/information needs to be provided to FR so the team as a whole is successful in maintaining these changes?

- Code mirror documentation will need an update/refresh.
- New documetation for inter-branch merge functionality should be generated.

