namespace Shared.Models;

public record class AskRequest(
    string Question,
    Approach Approach,
    RequestOverrides? Overrides = null) : ApproachRequest(Approach);
