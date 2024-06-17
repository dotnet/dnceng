# Component: Inter-branch Merge GitHub Action

## Requirements

A GitHub Action should be developed that replaces the inter-branch merge functionality. It should have the following qualities

- Should open a PR in specified 'downstream' branches when a commit to a branch is made.
- Should should follow the similar conventions and UX as the current inter-branch merge PRs.
- Should be reusable in any .NET repository.

### The UX of the functionality

Users (owners) of the repository will need to configure the workflow by extending(shared workflow) and provide the configuration data, within the the workflow file

Approximate usage of the shared workflow
```YML
on:
  push:
    branches:
      - 'releases/8.0'
jobs:
  call-workflow:
    uses: dotnet/arcade/.github/workflows/reusable-workflow.yml@main
    with:
      base-branch: 'main'
```

### Implementation
Copy paste already existing powershel [script](https://github.com/dotnet/arcade/blob/main/scripts/GitHubMergeBranches.ps1) from arcade with reduced functionalities of forking.
Github Action will call powershell script.
```YML
name: Example workflow
on: [push]
jobs:
  check-script:
    runs-on: windows-latest
    permissions:
      contents: write
      pull-requests: write
    env: 
      GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0
      - run: .\.github\workflows\scripts\GitHubMergeBranches.ps1
```

This approach allows us to reuse already imlpemented functionality hence less error prone.
The work will require to
- Implement the workflow which will execute the ps script
- Provide proper documentation for users to onboard

### Additionally considered implementation
Rewrite the functionality of the ps script into more reusable code blocks within the actions and use github-script action which will allow to implement the functionality using node.js (https://github.com/actions/github-script?tab=readme-ov-file#actionsgithub-script)
Work to do:
- Implement the workflow Rewrite ps script to the actions
- Provide proper documentation for users to onboard 

This approach will require more time on implementation, however not exluding this right now in case some blockers will be met during the journey of reusing ps script.

## Update (13  June 2024)
In order to provide better UX to onboard and making changes into the configuration module. 
The changes include: 
- Introduce the step to read the configuration from the json file which should be presented in repository.
- The name and the path of the cofiguration file is configurable
- The workflow parameters changed to have configuration_file_path and configuration_file_branch

### Configuration example file
```JS
{
    "merge-flow-configurations": {
        // The key of the object is source branch
        "release/8.0.3xx": {
           // The branch to which PR should be created
            "MergeToBranch": "release/8.0.4xx",
            // extra switches similar to the https://github.com/dotnet/versions/blob/main/Maestro/subscriptions.json file
            "ExtraSwitches": "-QuietComments"
        },
        "release/8.0.4xx": {
            "MergeToBranch": "main",
            "ExtraSwitches": "-QuietComments"
        }
    }
}
```

### Workflow file example
```YML
name: Usage of Inter-branch merge workflow
on: 
  push:
    branches:
      - 'main'
      - 'releases/**'

permissions:
  contents: write
  pull-requests: write

jobs:
  check-script:
    uses: dotnet/arcade/.github/workflows/inter-branch-merge-base.yml@main
    with:
      configuration_file_path: '.config/merge-flow.json'
```

### POC
Proof of Concept in progress, links to the runs will be provided