using Octokit;

namespace DotNet.Status.Web.Models;

public class IssueCommentPayloadWithChanges 
{
    public IssueOrPullRequestCommentChanges Changes { get; set; }
    public string Action { get;  set; }
    public User  User{ get;  set; }
    public Issue Issue { get; set; }
    public Repository Repository { get; set; }
    public IssueComment Comment { get; set; }
}