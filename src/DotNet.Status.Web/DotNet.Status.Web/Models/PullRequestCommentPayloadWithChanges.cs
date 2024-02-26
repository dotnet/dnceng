using Octokit;

namespace DotNet.Status.Web.Models;

public class PullRequestCommentPayloadWithChanges
{
    public IssueOrPullRequestCommentChanges Changes { get; set; }
    public string Action { get; set; }
    public PullRequest PullRequest { get; set; }
    public Repository Repository { get; set; }
    public PullRequestReviewComment Comment { get; set; }
}