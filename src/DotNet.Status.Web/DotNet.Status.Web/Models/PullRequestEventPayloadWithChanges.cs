using Octokit;

namespace DotNet.Status.Web.Models;

public class PullRequestEventPayloadWithChanges
{
    public IssueOrPullRequestCommentChanges Changes { get; set; }
    public string Action { get;  set; }
    public Repository Repository { get; set; }
    public PullRequest PullRequest { get; set; }
}
