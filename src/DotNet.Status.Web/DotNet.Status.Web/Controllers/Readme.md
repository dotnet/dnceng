# DotNet Status Web API 

## GitHubHook

### Milestone Management

#### How to Use

Opt-in a repo to use this by: 
1. Activate the DotNet Status Web GitHub app in the repo
2. Include the repo in the MilestoneManagement -> AllowableRepos section of settings.json in the format of `org/repo`. 

#### Logic

This webhook attempts to automatically create and link GitHub issues with the "Epic" label to a GitHub milestone to use to track issues that are associated with the epic issue. It will also automatically close any milestones that are associated with the epic when the epic is closed, and it will prevent the closure or unlabeling of an issue depending on if there are open issues in the milestone (to prevent orphaned issues from occurring). 

Scenarios handled: 
- If an issue with the epic label has its title edited, the webhook will either look at the milestone that's linked to the epic issue already and edit that. If a milestone isn't associated with the epic already, then attempt to find the milestone with the same name, update the milestone name, and link it to the epic issue. If no milestone can be found, it will create one and link it to the epic issue. 
- If the epic label is applied to an issue or a closed epic issue is reopened, the webhook will attempt to find the milestone and reopen it (if it was previously closed) and link it to the epic issue. If no milestone can be found, it will create one and link it to the epic issue. 
- If an issue with the epic label is attempted to be closed or have the epic label removed, the webhook will reopen or reapply the label if there are any open issues associated with the milestone with the epic name. Otherwise, it will close or remove the label as normal (and close the milestone).

It is possible to remove a milestone from an epic issue, but if the issue is edited while the epic label is applied to the issue, it will attempt to reassociate or create a new milestone to associate with that epic issue. 