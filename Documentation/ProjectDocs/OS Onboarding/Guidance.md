# New Operating System Version Onboarding Guide

New operating system (OS) versions are released [practically every month](https://github.com/dotnet/core/issues/9638). Our users move to those new versions quickly and expect everything to work. Old OS versions go EOL at the same rate. We have security and operational responsibilities to remove our use of those versions. This means that we have a constant and potentially expensive need to address new and old OSes. Do not worry! We have a plan.

## Guidance

We have three bits of guidance that everyone should follow:

- Ensure that `main` references the latest LTS (if applicable) OS versions. This approach gives us great future-looking coverage and provides us with the longest support period for `release/` branches (avoiding the cost of EOL remediation).
- For Linux, teams should exclusively build with Azure Linux and the Linux distros prescribed by `dotnet/source-build`/`dotnet/dotnet` (VMR).
- Limit your test coverage to OS family (Linux, macOS, Windows). `dotnet/runtime` provides extensive coverage for specific versions and distros. For the most part, we don't need teams testing on multiple OS versions or Linux distros. You are better served by quicker builds.

## Mechanics

We've written up guidance in a few key repos, which should be applicable to all scenarios.

- [General guidance and native code build (dotnet/runtime)](https://github.com/dotnet/runtime/blob/main/docs/project/os-onboarding.md)
- [Managed code build (dotnet/aspnetcore)](https://github.com/dotnet/aspnetcore/issues/60281)

Notes:

- If this guidance doesn't fit your needs, please document your process and add a link to this document.
