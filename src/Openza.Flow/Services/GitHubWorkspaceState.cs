using Openza.Flow.Core.Models;

namespace Openza.Flow.Services;

public sealed class GitHubWorkspaceState
{
    private IReadOnlyList<PullRequest> _reviewRequests = [];
    private IReadOnlyList<GithubWorkflowRun> _workflowRuns = [];

    public event EventHandler? Changed;

    public bool IsAuthenticated { get; private set; }

    public IReadOnlyList<PullRequest> ReviewRequests => _reviewRequests;

    public IReadOnlyList<GithubWorkflowRun> WorkflowRuns => _workflowRuns;

    public void SetAuthentication(bool isAuthenticated)
    {
        IsAuthenticated = isAuthenticated;
        if (!isAuthenticated)
        {
            _reviewRequests = [];
            _workflowRuns = [];
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetReviewRequests(IEnumerable<PullRequest> reviewRequests)
    {
        _reviewRequests = reviewRequests.OrderByDescending(item => item.UpdatedAt).ToList();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetWorkflowRuns(IEnumerable<GithubWorkflowRun> workflowRuns)
    {
        _workflowRuns = workflowRuns.OrderByDescending(item => item.UpdatedAt).ToList();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
