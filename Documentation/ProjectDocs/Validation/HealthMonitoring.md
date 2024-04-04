# Health Monitoring

## Features and Processes in Scope

Helix Services
- Helix Client
- Helix API
- Controller
- EventHub
- ServiceBus
- Data Migration Services

Arcade-Services:

- Maestro++
- Darc API
- BAR
- Telemetry Service
- BARViz

## Links to Relevant Pipelines and Builds

MSENG:

- Helix-PR-Master
- Helix-CI
- Helix-Daily
- Helix Agents - CI

DNCENG (internal):

- Arcade-ci
- Arcade-extensions-ci
- Arcade-minimalci-sample-ci
- Arcade-pool-provider-ci
- Dotnet-arcade-service
- Arcade-validation-ci
- Helix-machines
	- Build-and-deploy-production
	- Build-and-deploy-staging
	- Pr
	- Pr-prod-queues

## Existing Functionality and Processes
Availability and reliability monitoring for PROD services already exists in Power BI from data in AppInsights and Kusto. Our focus for Health Reporting will be based on tests and builds in AzDO. 

## Assumptions
All the builds and deployments we care about are either in mseng or dnceng in AzDO

## Concerns
- There may be some "weirdness" with AzDO collecting test results when deploying Helix.
- SSL validation pre and post deployment enough or should we do periodic health checking?
- How to health report for Docker and OnPrem? 

## Use Cases and Solutions
- Build status widgets in AzDO Dashboard using data pulled from Pipelines
- Test Run status widgets in AzDO Dashboard using data pulled from Pipelines
- Deployment status widgets in AzDO Dashboard using data pulled from Pipelines

The above should give the picture needed to know if staging is stable to rollout out to Prod. Health Monitoring of already deployed services in PROD is available in PowerBI and is not covered part of this epic. 
Checks to ensure Service Fabric is reachable, up and running, will not fall over with deployment etc., are validated via tests that will get hooked up to pipelines. 

Sample AzDO Dashboard - https://dev.azure.com/dnceng/internal/_dashboards/dashboard/755b52e7-b7a3-423b-bb60-7a01ff7241b8

## Dependencies
- AzDO (Dashboard, widgets, pipelines)
- Azure


<!-- Begin Generated Content: Doc Feedback -->
<sub>Was this helpful? [![Yes](https://helix.dot.net/f/ip/5?p=Documentation%5CValidation%5CHealthMonitoring.md)](https://helix.dot.net/f/p/5?p=Documentation%5CValidation%5CHealthMonitoring.md) [![No](https://helix.dot.net/f/in)](https://helix.dot.net/f/n/5?p=Documentation%5CValidation%5CHealthMonitoring.md)</sub>
<!-- End Generated Content-->
