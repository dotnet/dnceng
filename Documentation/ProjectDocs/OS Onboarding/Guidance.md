# New Operating System Version Onboarding Guide

New operating system (OS) versions are released [practically every month](https://github.com/dotnet/core/issues/9638). Our users move to those new versions quickly and expect everything to work. Old OS versions go EOL at the same rate. We have security and operational responsibilities to remove our use of those versions. This means that we have a constant and potentially expensive need to address new and old OSes. Do not worry! We have a plan.

## Guidance

We have three bits of guidance that everyone should follow:

- Ensure that `main` references the latest OS versions (choose latest LTS, if applicable). This approach gives us great future-looking coverage and provides us with the longest support period for `release/` branches (avoiding the cost of EOL remediation).
- For Linux, teams should exclusively build with Azure Linux and the Linux distros prescribed by `dotnet/source-build`/`dotnet/dotnet` (VMR).
- Limit your test coverage to OS family (Linux, macOS, Windows). `dotnet/runtime` provides extensive coverage for specific versions and distros. For the most part, we don't need teams testing on multiple OS versions or Linux distros. You are better served by quicker builds.

## Mechanics

We've written up guidance in a few key repos, which should be applicable to all scenarios.

- [General guidance and native code build (dotnet/runtime)](https://github.com/dotnet/runtime/blob/main/docs/project/os-onboarding.md)
- [Managed code build (dotnet/aspnetcore)](https://github.com/dotnet/aspnetcore/blob/main/docs/OnboardingNewOS.md)
- [Guidelines for Platforms Tested in CI (dotnet/source-build)](https://github.com/dotnet/source-build/blob/main/Documentation/ci-platform-coverage-guidelines.md)
- [Add a new Android API Level (dotnet/android)](https://github.com/dotnet/android/blob/main/Documentation/workflow/HowToAddNewApiLevel.md)
- [Prereq container image lifecycle (dotnet/dotnet-buildtools-prereqs-docker)](https://github.com/dotnet/dotnet-buildtools-prereqs-docker/blob/main/lifecycle.md)

Notes:

- If this guidance doesn't fit your needs, please document your process and add a link to this document.

## References

- [.NET OS Support Tracking (dotnet/core)](https://github.com/dotnet/core/issues/9638)
- [.NET Support (dotnet/core)](https://github.com/dotnet/core/blob/main/support.md)
- [Support for Linux Distros (MS internal)](https://dev.azure.com/dnceng/internal/_wiki/wikis/DNCEng%20Services%20Wiki/940/Support-for-Linux-Distros)
- [Support for Apple Operating Systems (MS internal)](https://dev.azure.com/dnceng/internal/_wiki/wikis/DNCEng%20Services%20Wiki/933/Support-for-Apple-Operating-Systems-(macOS-iOS-and-tvOS))
- [Support for Windows Operating Systems (MS internal)](https://dev.azure.com/dnceng/internal/_wiki/wikis/DNCEng%20Services%20Wiki/939/Support-for-Windows-Operating-Systems)
