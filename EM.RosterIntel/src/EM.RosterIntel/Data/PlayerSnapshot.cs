namespace EM.RosterIntel.Data;

public sealed class PlayerSnapshot
{
	public string Nick { get; init; } = "unknown";

	public PlayerRole Role { get; init; }

	public bool IsStarter { get; init; }

	public bool IsCaptain { get; init; }

	public double Skill { get; init; }

	public double AWP { get; init; }

	public double Rifle { get; init; }

	public double Pistol { get; init; }

	public double Grenades { get; init; }

	public double Creativity { get; init; }

	public double Clutch { get; init; }

	public double Tactics { get; init; }

	public double Leadership { get; init; }

	public double Teamwork { get; init; }

	public double Morale { get; init; }

	public double Stress { get; init; }

	public double Loyalty { get; init; }

	public double Productivity { get; init; }

	public double Reaction { get; init; }

	public double Perception { get; init; }

	public double Immunity { get; init; }

	public double Strength { get; init; }

	public double Stamina { get; init; }

	public double Form { get; init; } = 90.0;

	public double Health { get; init; } = 90.0;

	public double SalaryMonthly { get; init; }

	public double? GameRating { get; init; }

	public double? ImpactRating { get; init; }

	public double? KdRatio { get; init; }

	public double? Adr { get; init; }

	public double? KastPercent { get; init; }

	public double? Top1Rating { get; init; }

	public double? Top5Rating { get; init; }

	public double? Top10Rating { get; init; }

	public double? Top20Rating { get; init; }

	public double? Top50Rating { get; init; }

	public string? PerformanceSource { get; init; }

	public int? TierMaps { get; init; }

	public double? Top5Kd { get; init; }

	public double? Top10Kd { get; init; }

	public double? Top20Kd { get; init; }

	public double? Top50Kd { get; init; }
}
