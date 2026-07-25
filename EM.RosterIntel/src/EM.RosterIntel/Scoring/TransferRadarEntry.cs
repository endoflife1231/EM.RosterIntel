namespace EM.RosterIntel.Scoring;

public sealed class TransferRadarEntry
{
	public string CandidateNick { get; init; } = "";

	public string CandidateTeam { get; init; } = "";

	public string ReplaceNick { get; init; } = "";

	public string Role { get; init; } = "";

	public double TeamFitDelta { get; init; }

	public double RoleDelta { get; init; }

	public string Tier { get; init; } = "watch";

	public string Profile { get; init; } = "";

	public string Evidence { get; init; } = "";

	public string Lane { get; init; } = "role-safe";

	public string Risk { get; init; } = "";

	public string Action { get; init; } = "watch";

	public string Confidence { get; init; } = "low";

	public string Reason { get; init; } = "";
}
