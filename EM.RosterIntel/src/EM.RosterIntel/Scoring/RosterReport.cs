using System.Collections.Generic;

namespace EM.RosterIntel.Scoring;

public sealed class RosterReport
{
	public string TeamName { get; init; } = "";

	public double TeamFit { get; init; }

	public double Firepower { get; init; }

	public double AWP { get; init; }

	public double IGL { get; init; }

	public double Utility { get; init; }

	public double Clutch { get; init; }

	public double Teamwork { get; init; }

	public double Form { get; init; }

	public double Morale { get; init; }

	public double Performance { get; init; }

	public double StatsCoverage { get; init; }

	public double ObjectiveConfidence { get; init; }

	public double AverageStatsSignals { get; init; }

	public int DirectStatsPlayers { get; init; }

	public int LogStatsPlayers { get; init; }

	public int AttributeOnlyPlayers { get; init; }

	public int TierStatsPlayers { get; init; }

	public int TotalTierMaps { get; init; }

	public string MatchHistoryStatus { get; init; } = "unknown";

	public bool IsManagedTeamFocus { get; init; }

	public string ManagedTeamSource { get; init; } = "scouting";

	public string DataConfidence { get; init; } = "unknown";

	public string ObjectiveAudit { get; init; } = "unknown";

	public string RecommendationAudit { get; init; } = "unknown";

	public string PrecisionChecklist { get; init; } = "unknown";

	public string TransferRadarStatus { get; init; } = "off";

	public string ReleaseAuditStatus { get; init; } = "unknown";

	public IReadOnlyList<TransferRadarEntry> TransferRadar { get; init; } = new List<TransferRadarEntry>();

	public string MissingStatsPlayers { get; init; } = "";

	public int StarterCount { get; init; }

	public int BenchCount { get; init; }

	public string WeakestLink { get; init; } = "N/A";

	public string BestSwap { get; init; } = "N/A";

	public string Verdict { get; init; } = "N/A";

	public IReadOnlyList<PlayerScores> Players { get; init; } = new List<PlayerScores>();

	public IReadOnlyList<PlayerScores> BenchPlayers { get; init; } = new List<PlayerScores>();
}
