# DotNet.Status.Web

## Overview

The `DotNet.Status.Web` service is a web application used by the .NET Engineering Services team to monitor and manage the status of engineering workflows. It provides APIs and webhooks for tracking deployments, build notifications, and other engineering processes. The service integrates with tools like Azure DevOps, GitHub, and Grafana to provide real-time status updates and annotations.

Key features include:
- Deployment tracking with start and end annotations in Grafana.
- Integration with Azure DevOps for build and timeline notifications.
- GitHub webhook support for milestone management and issue tracking.

## Automated Azure DevOps work items

Grafana `alerting` and `no_data` notifications and monitored failed builds create
regular `Task` work items in `dnceng/internal`. Both the option defaults and the
explicit `AzureDevOpsAlert:WorkItemType` and `BuildMonitor:AzDoIssues` values in
`DotNet.Status.Web/.config/settings.json` select `Task`. The configured First
Responders area, descriptions, origin URLs, tags and build-monitor assignments
are unchanged.

Existing-item queries remain type-agnostic so historical `DNCENG Task` work items
continue to match without conversion or duplicate filing. Grafana recurrences
reopen the matching item to `Backlog`; recovery marks it `Done`, preserving other
tags. The build monitor adds comments to matching non-`Done` items and retains
its configured project, definition, branch and tag filters. GitHub issue targets
and other Azure DevOps client callers are unaffected.

Source changes do not establish deployed adoption. After an approved Status Web
rollout, verify both effective work-item type settings and the resulting items:
new Grafana and build-monitor items should be `Task` in the configured project
and area, retain their HTML evidence and origin links, and use `Backlog`/`Done`.
Also verify recurrence/recovery and comments on existing historical items.
Do not convert historical work items as part of this change.
