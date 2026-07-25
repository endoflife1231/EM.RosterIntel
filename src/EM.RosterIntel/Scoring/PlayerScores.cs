namespace EM.RosterIntel.Scoring;

public sealed class PlayerScores
{
	public string Nick { get; init; } = "";

	public string Role { get; init; } = "";

	public double Firepower { get; init; }

	public double AWP { get; init; }

	public double Utility { get; init; }

	public double IGLCore { get; init; }

	public double IGLImpact { get; init; }

	public double Clutch { get; init; }

	public double Teamwork { get; init; }

	public double Performance { get; init; }

	public double Stability { get; init; }

	public double RoleAdjusted { get; init; }

	public double FirepowerTax { get; init; }

	public int StatsSignals { get; init; }

	public string PerformanceSource { get; init; } = "attributes";

	public string DataQuality { get; init; } = "attr-only";

	public string Verdict { get; init; } = "";

	public string Reason { get; init; } = "";

	public string EvidenceTag { get; init; } = "A";

	public string EvidenceSummary { get; init; } = "attrs-only";
}
