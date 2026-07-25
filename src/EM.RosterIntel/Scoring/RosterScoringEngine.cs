using System;
using System.Collections.Generic;
using System.Linq;
using EM.RosterIntel.Data;

namespace EM.RosterIntel.Scoring;

public sealed class RosterScoringEngine
{
	private sealed class CachedTransferCandidate
	{
		public PlayerSnapshot Player { get; init; } = new PlayerSnapshot();

		public string TeamName { get; init; } = "Unknown";

		public DateTime SeenAt { get; init; } = DateTime.UtcNow;
	}

	private readonly double _minimumSwapTeamFitDelta;

	private readonly double _minimumSwapRoleDelta;

	private readonly bool _managedTeamFocus;

	private readonly string _managedTeamName;

	private readonly int _autoManagedTeamMinBench;

	private readonly bool _enableTransferRadar;

	private readonly int _transferRadarMaxRows;

	private static string? s_autoManagedTeamName;

	private static string? s_transferPoolManagedTeamKey;

	private static readonly Dictionary<string, CachedTransferCandidate> s_transferPool = new Dictionary<string, CachedTransferCandidate>(StringComparer.OrdinalIgnoreCase);

	public RosterScoringEngine()
		: this(null)
	{
	}

	public RosterScoringEngine(ModConfig? config)
	{
		_minimumSwapTeamFitDelta = Math.Clamp(config?.MinimumSwapTeamFitDelta.Value ?? 0.35f, 0.05, 2.5);
		_minimumSwapRoleDelta = Math.Clamp(config?.MinimumSwapRoleDelta.Value ?? 0.35f, 0.0, 2.5);
		_managedTeamFocus = config?.ManagedTeamFocus.Value ?? true;
		_managedTeamName = config?.ManagedTeamName.Value ?? "auto";
		_autoManagedTeamMinBench = Math.Clamp(config?.AutoManagedTeamMinBench.Value ?? 1, 0, 5);
		_enableTransferRadar = config?.EnableTransferRadar.Value ?? true;
		_transferRadarMaxRows = Math.Clamp(config?.TransferRadarMaxRows.Value ?? 3, 0, 8);
	}

	public RosterReport Analyze(RosterSnapshot roster)
	{
		return BuildReport(roster, includeBestSwap: true);
	}

	private RosterReport BuildReport(RosterSnapshot roster, bool includeBestSwap)
	{
		List<PlayerSnapshot> list = roster.Starters.ToList();
		if (list.Count == 0)
		{
			return new RosterReport
			{
				TeamName = roster.TeamName,
				Verdict = "No starters found",
				StarterCount = 0,
				BenchCount = roster.Bench.Count
			};
		}
		double avgFp = ((IEnumerable<PlayerSnapshot>)list).Average((Func<PlayerSnapshot, double>)Firepower);
		List<PlayerScores> list2 = list.Select((PlayerSnapshot p) => ScorePlayer(p, avgFp)).ToList();
		List<PlayerScores> list3 = roster.Bench.Select((PlayerSnapshot p) => ScorePlayer(p, avgFp)).ToList();
		int num = list2.Count((PlayerScores p) => p.StatsSignals > 0);
		int directStatsPlayers = list2.Count(HasDirectStatsSource);
		int logStatsPlayers = list2.Count(HasLogStatsSource);
		int attributeOnlyPlayers = list2.Count((PlayerScores p) => p.StatsSignals <= 0);
		double statsCoverage = ((list.Count == 0) ? 0.0 : ((double)num * 100.0 / (double)list.Count));
		double num2 = ((list2.Count == 0) ? 0.0 : list2.Average((PlayerScores p) => p.StatsSignals));
		int tierStatsPlayers = list.Count((PlayerSnapshot p) => p.TierMaps.GetValueOrDefault() > 0);
		int totalTierMaps = list.Sum((PlayerSnapshot p) => p.TierMaps.GetValueOrDefault());
		string text = ResolveManagedTeamSource(roster);
		bool isManagedTeamFocus = text.StartsWith("managed", StringComparison.OrdinalIgnoreCase);
		double objectiveConfidence = EstimateObjectiveConfidence(num, list.Count, num2, directStatsPlayers, logStatsPlayers, tierStatsPlayers, totalTierMaps, isManagedTeamFocus);
		string dataConfidence = BuildDataConfidence(num, list.Count, list2, objectiveConfidence, isManagedTeamFocus);
		string matchHistoryStatus = BuildMatchHistoryStatus(num, list.Count, directStatsPlayers, logStatsPlayers, tierStatsPlayers, totalTierMaps, isManagedTeamFocus);
		string objectiveAudit = BuildObjectiveAudit(num, list.Count, num2, directStatsPlayers, logStatsPlayers, tierStatsPlayers, totalTierMaps, isManagedTeamFocus, text);
		string missingStatsPlayers = BuildMissingStatsPlayers(list2);
		string precisionChecklist = BuildPrecisionChecklist(num, list.Count, num2, directStatsPlayers, logStatsPlayers, tierStatsPlayers, totalTierMaps, isManagedTeamFocus, text, missingStatsPlayers);
		double num3 = ((IEnumerable<PlayerSnapshot>)list).Average((Func<PlayerSnapshot, double>)Firepower);
		double num4 = list.Where((PlayerSnapshot p) => p.Role == PlayerRole.AWPer).Select(AWPerFit).DefaultIfEmpty(((IEnumerable<PlayerSnapshot>)list).Max((Func<PlayerSnapshot, double>)AWPScore))
			.Max();
		double num5 = list2.Max((PlayerScores p) => p.IGLImpact);
		double utility = ((IEnumerable<PlayerSnapshot>)list).Average((Func<PlayerSnapshot, double>)Utility);
		double clutch = list.Average((PlayerSnapshot p) => p.Clutch);
		double teamwork = list.Average((PlayerSnapshot p) => p.Teamwork);
		double form = list.Average((PlayerSnapshot p) => NormalizeGauge(p.Form));
		double morale = list.Average((PlayerSnapshot p) => p.Morale);
		double performance = ((IEnumerable<PlayerSnapshot>)list).Average((Func<PlayerSnapshot, double>)PerformanceScore);
		double num6 = CalculateTeamFit(num3, num5, num4, utility, clutch, teamwork, form, morale, performance);
		PlayerScores playerScores = list2.OrderBy((PlayerScores p) => p.RoleAdjusted).FirstOrDefault();
		string bestSwap = (includeBestSwap ? FindBestSwap(roster, list, num6) : "Not calculated");
		string recommendationAudit = BuildRecommendationAudit(bestSwap, playerScores?.Nick ?? "N/A", list2, list3, num6, isManagedTeamFocus);
		SyncTransferRadarScope(roster, isManagedTeamFocus);
		UpdateTransferRadarPool(roster, isManagedTeamFocus);
		List<TransferRadarEntry> list4 = BuildTransferRadar(roster, list, list2, num6, isManagedTeamFocus);
		string transferRadarStatus = BuildTransferRadarStatus(isManagedTeamFocus, list4.Count);
		string releaseAuditStatus = BuildReleaseAuditStatus(roster, isManagedTeamFocus, objectiveConfidence, num, list.Count, totalTierMaps, list4.Count);
		return new RosterReport
		{
			TeamName = roster.TeamName,
			TeamFit = num6,
			Firepower = num3,
			AWP = num4,
			IGL = num5,
			Utility = utility,
			Clutch = clutch,
			Teamwork = teamwork,
			Form = form,
			Morale = morale,
			Performance = performance,
			StatsCoverage = statsCoverage,
			ObjectiveConfidence = objectiveConfidence,
			AverageStatsSignals = num2,
			DirectStatsPlayers = directStatsPlayers,
			LogStatsPlayers = logStatsPlayers,
			AttributeOnlyPlayers = attributeOnlyPlayers,
			TierStatsPlayers = tierStatsPlayers,
			TotalTierMaps = totalTierMaps,
			MatchHistoryStatus = matchHistoryStatus,
			IsManagedTeamFocus = isManagedTeamFocus,
			ManagedTeamSource = text,
			DataConfidence = dataConfidence,
			ObjectiveAudit = objectiveAudit,
			RecommendationAudit = recommendationAudit,
			PrecisionChecklist = precisionChecklist,
			TransferRadar = list4,
			TransferRadarStatus = transferRadarStatus,
			ReleaseAuditStatus = releaseAuditStatus,
			MissingStatsPlayers = missingStatsPlayers,
			WeakestLink = (playerScores?.Nick ?? "N/A"),
			BestSwap = bestSwap,
			Verdict = BuildTeamVerdict(num6, num5, num3, num4, performance),
			StarterCount = list.Count,
			BenchCount = roster.Bench.Count,
			Players = list2,
			BenchPlayers = list3
		};
	}

	public double EvaluateTeamFitOnly(RosterSnapshot roster)
	{
		List<PlayerSnapshot> list = roster.Starters.ToList();
		if (list.Count == 0)
		{
			return 0.0;
		}
		double avgFp = ((IEnumerable<PlayerSnapshot>)list).Average((Func<PlayerSnapshot, double>)Firepower);
		List<PlayerScores> source = list.Select((PlayerSnapshot p) => ScorePlayer(p, avgFp)).ToList();
		double firepower = ((IEnumerable<PlayerSnapshot>)list).Average((Func<PlayerSnapshot, double>)Firepower);
		double awp = list.Where((PlayerSnapshot p) => p.Role == PlayerRole.AWPer).Select(AWPerFit).DefaultIfEmpty(((IEnumerable<PlayerSnapshot>)list).Max((Func<PlayerSnapshot, double>)AWPScore))
			.Max();
		double igl = source.Max((PlayerScores p) => p.IGLImpact);
		double utility = ((IEnumerable<PlayerSnapshot>)list).Average((Func<PlayerSnapshot, double>)Utility);
		double clutch = list.Average((PlayerSnapshot p) => p.Clutch);
		double teamwork = list.Average((PlayerSnapshot p) => p.Teamwork);
		double form = list.Average((PlayerSnapshot p) => NormalizeGauge(p.Form));
		double morale = list.Average((PlayerSnapshot p) => p.Morale);
		double performance = ((IEnumerable<PlayerSnapshot>)list).Average((Func<PlayerSnapshot, double>)PerformanceScore);
		return CalculateTeamFit(firepower, igl, awp, utility, clutch, teamwork, form, morale, performance);
	}

	public PlayerScores ScorePlayer(PlayerSnapshot p, double teamAverageFirepower)
	{
		double num = Firepower(p);
		double num2 = Math.Max(0.0, teamAverageFirepower - num);
		double num3 = IGLCore(p);
		double num4 = num3 - num2 * 0.55;
		double num5 = Utility(p);
		double num6 = AWPScore(p);
		double num7 = PerformanceScore(p);
		double stability = StabilityScore(p);
		int num8 = CountPerformanceSignals(p);
		string performanceSource = BuildPerformanceSource(p, num8);
		string dataQuality = ((num8 >= 4) ? "high" : ((num8 >= 2) ? "medium" : ((num8 == 1) ? "partial" : "attr-only")));
		string evidenceTag = BuildEvidenceTag(p, performanceSource, num8);
		string evidenceSummary = BuildEvidenceSummary(p, performanceSource, num8);
		double num9 = RoleAdjustedFit(p, num, num6, num5, num4, num7, stability);
		num9 += (NormalizeGauge(p.Form) - 15.0) * 0.07 + (p.Morale - 15.0) * 0.04;
		num9 = Clamp20(num9);
		return new PlayerScores
		{
			Nick = p.Nick,
			Role = (p.IsCaptain ? "IGL" : p.Role.ToString()),
			Firepower = num,
			AWP = num6,
			Utility = num5,
			IGLCore = num3,
			IGLImpact = num4,
			Clutch = p.Clutch,
			Teamwork = p.Teamwork,
			Performance = num7,
			Stability = stability,
			RoleAdjusted = num9,
			FirepowerTax = num2,
			StatsSignals = num8,
			PerformanceSource = performanceSource,
			DataQuality = dataQuality,
			Verdict = RoleVerdict(p, num9, num7, num4, num2),
			Reason = RoleReason(p, num9, num7, num, num6, num5, num4),
			EvidenceTag = evidenceTag,
			EvidenceSummary = evidenceSummary
		};
	}

	private string ResolveManagedTeamSource(RosterSnapshot roster)
	{
		if (!_managedTeamFocus)
		{
			return "scouting";
		}
		if (string.IsNullOrWhiteSpace(roster.TeamName))
		{
			return "scouting";
		}
		if (!IsAutoManagedTeamMode())
		{
			if (!MatchesManagedTeamAlias(roster.TeamName))
			{
				return "scouting";
			}
			return "managed:manual";
		}
		string text = NormalizeTeamName(roster.TeamName);
		if (text.Length == 0)
		{
			return "scouting:auto-unresolved";
		}
		if (roster.IsLikelyHumanManagedTeam == true)
		{
			string value = s_autoManagedTeamName;
			s_autoManagedTeamName = roster.TeamName.Trim();
			if (!string.IsNullOrWhiteSpace(value) && !string.Equals(NormalizeTeamName(value), text, StringComparison.OrdinalIgnoreCase))
			{
				return "managed:auto-relearned:" + SafeSignal(roster.ManagedTeamSignal);
			}
			return "managed:auto-live:" + SafeSignal(roster.ManagedTeamSignal);
		}
		if (!string.IsNullOrWhiteSpace(s_autoManagedTeamName))
		{
			string text2 = NormalizeTeamName(s_autoManagedTeamName);
			if (text == text2 || text.Contains(text2, StringComparison.OrdinalIgnoreCase) || text2.Contains(text, StringComparison.OrdinalIgnoreCase))
			{
				if (roster.IsLikelyHumanManagedTeam != false)
				{
					return "managed:auto-memory";
				}
				return "managed:auto-memory/no-live-flag";
			}
			return "scouting:auto=" + s_autoManagedTeamName;
		}
		if (roster.IsLikelyHumanManagedTeam == false)
		{
			return "scouting:auto-live-flag";
		}
		if (roster.Starters.Count >= 5 && roster.Bench.Count >= _autoManagedTeamMinBench)
		{
			s_autoManagedTeamName = roster.TeamName.Trim();
			return "managed:auto-learned-fallback";
		}
		if (_autoManagedTeamMinBench <= 0 && roster.Starters.Count >= 5)
		{
			s_autoManagedTeamName = roster.TeamName.Trim();
			return "managed:auto-learned-fallback";
		}
		return "scouting:auto-waiting";
	}

	private bool IsAutoManagedTeamMode()
	{
		string text = (_managedTeamName ?? string.Empty).Trim();
		if (text.Length != 0 && !text.Equals("auto", StringComparison.OrdinalIgnoreCase) && !text.Equals("*", StringComparison.OrdinalIgnoreCase))
		{
			return text.Equals("detect", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private bool MatchesManagedTeamAlias(string teamName)
	{
		if (string.IsNullOrWhiteSpace(teamName) || string.IsNullOrWhiteSpace(_managedTeamName))
		{
			return false;
		}
		string text = NormalizeTeamName(teamName);
		string[] array = _managedTeamName.Split(new char[3] { ';', ',', '|' }, StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < array.Length; i++)
		{
			string text2 = NormalizeTeamName(array[i]);
			if (text2.Length != 0 && (text == text2 || text.Contains(text2, StringComparison.OrdinalIgnoreCase) || text2.Contains(text, StringComparison.OrdinalIgnoreCase)))
			{
				return true;
			}
		}
		return false;
	}

	private static string NormalizeTeamName(string value)
	{
		return new string(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
	}

	private static string SafeSignal(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return "unknown";
		}
		return value.Replace(" ", "-", StringComparison.OrdinalIgnoreCase).Replace("/", "-", StringComparison.OrdinalIgnoreCase).Replace(":", "-", StringComparison.OrdinalIgnoreCase);
	}

	private static double RoleAdjustedFit(PlayerSnapshot p, double fp, double awp, double util, double iglImpact, double perf, double stability)
	{
		if (p.IsCaptain || p.Role == PlayerRole.IGL)
		{
			return Clamp20(iglImpact * 0.48 + util * 0.22 + fp * 0.13 + perf * 0.1 + stability * 0.07);
		}
		return p.Role switch
		{
			PlayerRole.AWPer => Clamp20(AWPerFit(p) * 0.72 + perf * 0.18 + stability * 0.1), 
			PlayerRole.Support => Clamp20(util * 0.48 + p.Teamwork * 0.18 + stability * 0.14 + perf * 0.12 + fp * 0.08), 
			PlayerRole.Lurker => Clamp20(fp * 0.42 + p.Clutch * 0.2 + perf * 0.2 + stability * 0.1 + util * 0.08), 
			_ => Clamp20(fp * 0.48 + perf * 0.22 + p.Clutch * 0.12 + stability * 0.1 + util * 0.08), 
		};
	}

	public static double Firepower(PlayerSnapshot p)
	{
		return Clamp20(p.Rifle * 0.28 + p.Skill * 0.2 + p.Reaction * 0.18 + p.Pistol * 0.08 + p.Clutch * 0.13 + p.Perception * 0.13);
	}

	public static double AWPScore(PlayerSnapshot p)
	{
		return Clamp20(p.AWP * 0.44 + p.Reaction * 0.18 + p.Perception * 0.16 + p.Stress * 0.08 + p.Clutch * 0.1 + p.Pistol * 0.04);
	}

	public static double AWPerFit(PlayerSnapshot p)
	{
		double num = OpponentTierBonus(p);
		return Clamp20(AWPScore(p) * 0.56 + PerformanceScore(p) * 0.25 + StabilityScore(p) * 0.09 + p.Clutch * 0.06 + Firepower(p) * 0.04 + num);
	}

	public static double Utility(PlayerSnapshot p)
	{
		return Clamp20(p.Grenades * 0.33 + p.Tactics * 0.25 + p.Teamwork * 0.2 + p.Creativity * 0.1 + p.Stress * 0.12);
	}

	public static double IGLCore(PlayerSnapshot p)
	{
		return Clamp20(p.Leadership * 0.24 + p.Tactics * 0.2 + p.Teamwork * 0.15 + p.Grenades * 0.1 + p.Creativity * 0.09 + p.Stress * 0.1 + p.Morale * 0.06 + NormalizeGauge(p.Form) * 0.06);
	}

	public static double PerformanceScore(PlayerSnapshot p)
	{
		List<(double, double)> list = new List<(double, double)>();
		Add(list, p.GameRating, 0.12, 0.0, 20.0);
		Add(list, Rating21To20(p.ImpactRating), 0.18, 0.0, 20.0);
		Add(list, KdTo20(p.KdRatio), 0.12, 0.0, 20.0);
		Add(list, AdrTo20(p.Adr), 0.08, 0.0, 20.0);
		Add(list, KastTo20(p.KastPercent), 0.06, 0.0, 20.0);
		Add(list, Rating21To20(p.Top1Rating), 0.08, 0.0, 20.0);
		Add(list, Rating21To20(p.Top5Rating), 0.16, 0.0, 20.0);
		Add(list, Rating21To20(p.Top10Rating), 0.14, 0.0, 20.0);
		Add(list, Rating21To20(p.Top20Rating), 0.1, 0.0, 20.0);
		Add(list, Rating21To20(p.Top50Rating), 0.06, 0.0, 20.0);
		double num = NeutralPerformanceFromAttributes(p);
		if (list.Count == 0)
		{
			return num;
		}
		double num2 = list.Sum<(double, double)>(((double value, double weight) part) => part.value * part.weight);
		double val = list.Sum<(double, double)>(((double value, double weight) part) => part.weight);
		double num3 = Clamp20(num2 / Math.Max(0.001, val));
		double num4 = StatsEvidenceBlend(CountPerformanceSignals(p));
		return Clamp20(num3 * num4 + num * (1.0 - num4));
	}

	private static double StatsEvidenceBlend(int signals)
	{
		if (signals >= 5)
		{
			return 0.82;
		}
		if (signals >= 4)
		{
			return 0.74;
		}
		if (signals >= 3)
		{
			return 0.64;
		}
		if (signals >= 2)
		{
			return 0.54;
		}
		if (signals == 1)
		{
			return 0.38;
		}
		return 0.0;
	}

	private static double EstimateObjectiveConfidence(int statsCovered, int starterCount, double averageSignals, int directStatsPlayers, int logStatsPlayers, int tierStatsPlayers, int totalTierMaps, bool isManagedTeamFocus)
	{
		if (starterCount <= 0)
		{
			return 0.0;
		}
		double num = ((starterCount >= 5) ? 66.0 : Math.Clamp((double)starterCount / 5.0 * 66.0, 20.0, 66.0));
		double num2 = (double)statsCovered * 100.0 / (double)starterCount * 0.16;
		double num3 = Math.Clamp(averageSignals, 0.0, 5.0) / 5.0 * 18.0;
		double val = Math.Clamp(num + num2 + num3, 0.0, 100.0);
		bool flag = statsCovered >= starterCount;
		bool flag2 = statsCovered >= Math.Max(1, starterCount - 1);
		bool flag3 = averageSignals >= 5.0;
		bool flag4 = flag && (averageSignals >= 4.0 || directStatsPlayers >= starterCount || logStatsPlayers >= starterCount || tierStatsPlayers >= starterCount || totalTierMaps >= Math.Max(10, starterCount * 5));
		if (isManagedTeamFocus && starterCount >= 5)
		{
			if (flag4)
			{
				return 100.0;
			}
			if (flag)
			{
				return Math.Max(Math.Min(val, 96.0), 92.0);
			}
			if (flag2)
			{
				return Math.Max(Math.Min(val, 94.0), 90.0);
			}
			if (statsCovered > 0)
			{
				return Math.Max(Math.Min(val, 91.0), 86.0);
			}
			return Math.Min(val, 84.0);
		}
		if (flag && flag3 && directStatsPlayers >= Math.Max(1, starterCount - 1))
		{
			return 100.0;
		}
		if (flag && flag3)
		{
			return Math.Min(val, 96.0);
		}
		if (flag && averageSignals >= 4.0)
		{
			return Math.Min(val, 94.0);
		}
		if (flag2)
		{
			return Math.Min(val, 91.0);
		}
		return Math.Min(val, 86.0);
	}

	public static double StabilityScore(PlayerSnapshot p)
	{
		return Clamp20(p.Stress * 0.22 + p.Morale * 0.18 + NormalizeGauge(p.Form) * 0.22 + NormalizeGauge(p.Health) * 0.18 + p.Stamina * 0.1 + p.Immunity * 0.1);
	}

	private static double OpponentTierBonus(PlayerSnapshot p)
	{
		double? num = p.Top5Rating ?? p.Top10Rating ?? p.Top20Rating;
		if (!num.HasValue)
		{
			return 0.0;
		}
		if (num.Value >= 1.35)
		{
			return 0.35;
		}
		if (num.Value >= 1.25)
		{
			return 0.2;
		}
		if (num.Value >= 1.15)
		{
			return 0.08;
		}
		if (num.Value < 0.95)
		{
			return -0.15;
		}
		return 0.0;
	}

	private static double NeutralPerformanceFromAttributes(PlayerSnapshot p)
	{
		return p.Role switch
		{
			PlayerRole.AWPer => Clamp20(AWPScore(p) * 0.62 + p.Clutch * 0.18 + p.Stress * 0.1 + NormalizeGauge(p.Form) * 0.1), 
			PlayerRole.IGL => Clamp20(IGLCore(p) * 0.55 + Utility(p) * 0.25 + Firepower(p) * 0.1 + NormalizeGauge(p.Form) * 0.1), 
			PlayerRole.Support => Clamp20(Utility(p) * 0.55 + p.Teamwork * 0.2 + p.Stress * 0.1 + NormalizeGauge(p.Form) * 0.15), 
			_ => Clamp20(Firepower(p) * 0.62 + p.Clutch * 0.18 + p.Stress * 0.08 + NormalizeGauge(p.Form) * 0.12), 
		};
	}

	private static void Add(List<(double value, double weight)> parts, double? value, double weight, double min, double max)
	{
		if (value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value))
		{
			parts.Add((Math.Clamp(value.Value, min, max), weight));
		}
	}

	private static double? Rating21To20(double? rating)
	{
		if (!rating.HasValue)
		{
			return null;
		}
		return Clamp20(14.0 + (rating.Value - 1.0) * 13.0);
	}

	private static double? KdTo20(double? kd)
	{
		if (!kd.HasValue)
		{
			return null;
		}
		return Clamp20(14.0 + (kd.Value - 1.0) * 10.0);
	}

	private static double? AdrTo20(double? adr)
	{
		if (!adr.HasValue)
		{
			return null;
		}
		return Clamp20(14.0 + (adr.Value - 75.0) / 5.0);
	}

	private static double? KastTo20(double? kast)
	{
		if (!kast.HasValue)
		{
			return null;
		}
		double num = ((kast.Value > 1.0) ? kast.Value : (kast.Value * 100.0));
		return Clamp20(14.0 + (num - 70.0) / 3.5);
	}

	public static double NormalizeGauge(double gauge0To100)
	{
		return Math.Clamp(gauge0To100 / 5.0, 0.0, 20.0);
	}

	private static double Clamp20(double v)
	{
		return Math.Clamp(v, 0.0, 20.0);
	}

	private static double CalculateTeamFit(double firepower, double igl, double awp, double utility, double clutch, double teamwork, double form, double morale, double performance)
	{
		return Clamp20(firepower * 0.23 + igl * 0.16 + awp * 0.16 + utility * 0.12 + performance * 0.14 + clutch * 0.07 + teamwork * 0.05 + form * 0.04 + morale * 0.03);
	}

	private static string RoleVerdict(PlayerSnapshot p, double roleFit, double performance, double iglImpact, double firepowerTax)
	{
		double num = Firepower(p);
		double num2 = Utility(p);
		if (p.IsCaptain || p.Role == PlayerRole.IGL)
		{
			if (iglImpact >= 17.5 && firepowerTax <= 1.5)
			{
				return "Elite tactical IGL";
			}
			if (iglImpact >= 17.0)
			{
				return "Strong IGL, acceptable firepower tax";
			}
			if (iglImpact >= 15.5)
			{
				return "Good IGL, watch firepower tax";
			}
			if (iglImpact >= 14.0)
			{
				return "Situational IGL";
			}
			return "IGL tax concern";
		}
		if (p.Role == PlayerRole.AWPer)
		{
			if (HasEliteTierStats(p) || roleFit >= 18.5 || performance >= 18.0)
			{
				return "Elite AWPer - keep";
			}
			if (roleFit >= 17.2)
			{
				return "Strong AWPer - keep unless superstar upgrade";
			}
			if (roleFit >= 15.8)
			{
				return "Solid AWPer";
			}
			return "AWP upgrade target";
		}
		if (p.Role == PlayerRole.Support)
		{
			if (roleFit >= 17.0 && num2 >= 17.5)
			{
				return "High-value support";
			}
			if (roleFit >= 15.5)
			{
				return "Stable support";
			}
			return "Support upgrade target";
		}
		if (p.Role == PlayerRole.Lurker)
		{
			if (roleFit >= 17.0 && p.Clutch >= 16.5)
			{
				return "Strong lurker";
			}
			if (roleFit >= 15.5)
			{
				return "Useful lurker";
			}
			return "Lurker upgrade target";
		}
		if (roleFit >= 18.0 && num >= 16.8)
		{
			return "Star rifler - primary carry";
		}
		if (roleFit >= 17.3 && num >= 16.2 && p.Rifle >= 19.0)
		{
			return "Damage rifler - main gun";
		}
		if (roleFit >= 16.5 && num2 >= 18.2 && num < 16.0)
		{
			return "Utility rifler - role balance";
		}
		if (roleFit >= 16.8 && num2 >= 18.0 && p.Tactics >= 17.5)
		{
			return "Structure rifler - utility brain";
		}
		if (roleFit >= 16.6 && p.Clutch >= 17.0)
		{
			return "Clutch rifler - late rounds";
		}
		if (roleFit >= 16.5 && num >= 15.8 && num2 >= 16.5)
		{
			return "Two-way rifler - fire+utility";
		}
		if (roleFit >= 16.5)
		{
			return "Strong rifler - stable core";
		}
		if (roleFit >= 15.0 && num2 >= 16.0)
		{
			return "Role rifler - useful system piece";
		}
		if (roleFit >= 15.0)
		{
			return "Solid starter";
		}
		return "Upgrade candidate";
	}

	private static string RoleReason(PlayerSnapshot p, double roleFit, double performance, double fp, double awp, double util, double iglImpact)
	{
		if (p.IsCaptain || p.Role == PlayerRole.IGL)
		{
			return $"IGL {iglImpact:0.0}, util {util:0.0}, firepower {fp:0.0}";
		}
		return p.Role switch
		{
			PlayerRole.AWPer => $"AWP {awp:0.0}, perf {performance:0.0}" + TierText(p) + ", rifle secondary", 
			PlayerRole.Support => $"util {util:0.0}, teamwork {p.Teamwork:0.0}, perf {performance:0.0}", 
			PlayerRole.Lurker => $"firepower {fp:0.0}, clutch {p.Clutch:0.0}, perf {performance:0.0}", 
			_ => $"firepower {fp:0.0}, perf {performance:0.0}, clutch {p.Clutch:0.0}", 
		};
	}

	private static bool HasEliteTierStats(PlayerSnapshot p)
	{
		if ((!p.Top5Rating.HasValue || !(p.Top5Rating.Value >= 1.3) || !((p.Top5Kd ?? 1.0) >= 1.25)) && (!p.Top10Rating.HasValue || !(p.Top10Rating.Value >= 1.28) || !((p.Top10Kd ?? 1.0) >= 1.25)))
		{
			if (p.Top20Rating.HasValue && p.Top20Rating.Value >= 1.3)
			{
				return (p.Top20Kd ?? 1.0) >= 1.3;
			}
			return false;
		}
		return true;
	}

	private static string TierText(PlayerSnapshot p)
	{
		if (p.Top5Rating.HasValue)
		{
			return $", vsT5 {p.Top5Rating.Value:0.00}";
		}
		if (p.Top10Rating.HasValue)
		{
			return $", vsT10 {p.Top10Rating.Value:0.00}";
		}
		if (p.Top20Rating.HasValue)
		{
			return $", vsT20 {p.Top20Rating.Value:0.00}";
		}
		return string.Empty;
	}

	private static int CountPerformanceSignals(PlayerSnapshot p)
	{
		int num = 0;
		if (p.GameRating.HasValue)
		{
			num++;
		}
		if (p.ImpactRating.HasValue)
		{
			num++;
		}
		if (p.KdRatio.HasValue)
		{
			num++;
		}
		if (p.Adr.HasValue)
		{
			num++;
		}
		if (p.KastPercent.HasValue)
		{
			num++;
		}
		if (p.Top1Rating.HasValue)
		{
			num++;
		}
		if (p.Top5Rating.HasValue)
		{
			num++;
		}
		if (p.Top10Rating.HasValue)
		{
			num++;
		}
		if (p.Top20Rating.HasValue)
		{
			num++;
		}
		if (p.Top50Rating.HasValue)
		{
			num++;
		}
		return num;
	}

	private static string BuildPerformanceSource(PlayerSnapshot p, int statsSignals)
	{
		if (!string.IsNullOrWhiteSpace(p.PerformanceSource))
		{
			return p.PerformanceSource;
		}
		if (p.Top5Rating.HasValue || p.Top10Rating.HasValue || p.Top20Rating.HasValue || p.Top50Rating.HasValue)
		{
			return "tier-stats";
		}
		if (p.ImpactRating.HasValue || p.KdRatio.HasValue || p.Adr.HasValue || p.KastPercent.HasValue)
		{
			return "direct-stats";
		}
		if (statsSignals > 0)
		{
			return "mixed-stats";
		}
		return "attributes";
	}

	private static string BuildEvidenceTag(PlayerSnapshot p, string performanceSource, int statsSignals)
	{
		bool flag = !string.IsNullOrWhiteSpace(performanceSource) && performanceSource.Contains("direct-stats", StringComparison.OrdinalIgnoreCase);
		bool flag2 = (!string.IsNullOrWhiteSpace(performanceSource) && performanceSource.Contains("OpponentTierStats.log", StringComparison.OrdinalIgnoreCase)) || p.TierMaps.GetValueOrDefault() > 0 || p.Top5Rating.HasValue || p.Top10Rating.HasValue || p.Top20Rating.HasValue || p.Top50Rating.HasValue;
		if (flag && flag2)
		{
			return "D+T";
		}
		if (flag)
		{
			return "D";
		}
		if (flag2)
		{
			return "T";
		}
		if (statsSignals > 0)
		{
			return "S";
		}
		return "A";
	}

	private static string BuildEvidenceSummary(PlayerSnapshot p, string performanceSource, int statsSignals)
	{
		return BuildEvidenceTag(p, performanceSource, statsSignals) switch
		{
			"D+T" => $"profile+tier, signals {statsSignals}", 
			"D" => $"profile/direct, signals {statsSignals}", 
			"T" => $"tier/log, maps {p.TierMaps.GetValueOrDefault()}", 
			"S" => $"stats, signals {statsSignals}", 
			_ => "attrs-only", 
		};
	}

	private static bool HasDirectStatsSource(PlayerScores p)
	{
		if (!string.IsNullOrWhiteSpace(p.PerformanceSource))
		{
			return p.PerformanceSource.Contains("direct-stats", StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}

	private static bool HasLogStatsSource(PlayerScores p)
	{
		if (!string.IsNullOrWhiteSpace(p.PerformanceSource))
		{
			return p.PerformanceSource.Contains("OpponentTierStats.log", StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}

	private static string BuildMissingStatsPlayers(IReadOnlyList<PlayerScores> players)
	{
		List<string> list = (from p in players
			where p.StatsSignals <= 0
			select p.Nick into n
			where !string.IsNullOrWhiteSpace(n)
			select n).Take(5).ToList();
		if (list.Count != 0)
		{
			return string.Join(", ", list);
		}
		return string.Empty;
	}

	private static string BuildPrecisionChecklist(int statsCovered, int starterCount, double averageSignals, int directStatsPlayers, int logStatsPlayers, int tierStatsPlayers, int totalTierMaps, bool isManagedTeamFocus, string managedTeamSource, string missingStatsPlayers)
	{
		if (starterCount <= 0)
		{
			return "BLOCK: нет live-состава";
		}
		List<string> list = new List<string>();
		list.Add(isManagedTeamFocus ? "OK own-team" : "SCOUTING cap");
		if (!string.IsNullOrWhiteSpace(managedTeamSource))
		{
			list.Add(managedTeamSource);
		}
		list.Add((starterCount >= 5) ? "OK roster 5/5" : $"BLOCK roster {starterCount}/5");
		list.Add((statsCovered >= starterCount) ? $"OK stats {statsCovered}/{starterCount}" : $"BLOCK stats {statsCovered}/{starterCount}");
		if (!string.IsNullOrWhiteSpace(missingStatsPlayers))
		{
			list.Add("missing: " + missingStatsPlayers);
		}
		if (directStatsPlayers > 0)
		{
			list.Add($"direct {directStatsPlayers}");
		}
		if (logStatsPlayers > 0)
		{
			list.Add($"tier/log {logStatsPlayers}");
		}
		if (totalTierMaps > 0)
		{
			list.Add($"history maps {totalTierMaps}");
		}
		else
		{
			list.Add("history maps 0");
		}
		list.Add($"avg signals {averageSignals:0.0}");
		if (isManagedTeamFocus && starterCount >= 5 && statsCovered >= starterCount && (averageSignals >= 4.0 || directStatsPlayers >= starterCount || logStatsPlayers >= starterCount || tierStatsPlayers >= starterCount || totalTierMaps >= Math.Max(10, starterCount * 5)))
		{
			list.Add("READY 100");
		}
		else if (isManagedTeamFocus && starterCount >= 5 && statsCovered <= 0)
		{
			list.Add("FOCUS OK / ANALYTICS WAITING");
		}
		return string.Join("; ", list);
	}

	private static string BuildObjectiveAudit(int statsCovered, int starterCount, double averageSignals, int directStatsPlayers, int logStatsPlayers, int tierStatsPlayers, int totalTierMaps, bool isManagedTeamFocus, string managedTeamSource)
	{
		if (starterCount <= 0)
		{
			return "нет live-состава";
		}
		List<string> list = new List<string>();
		list.Add(isManagedTeamFocus ? "моя команда" : "scouting");
		if (!string.IsNullOrWhiteSpace(managedTeamSource))
		{
			list.Add(managedTeamSource);
		}
		list.Add($"статы {statsCovered}/{starterCount}");
		list.Add($"ср.сигналы {averageSignals:0.0}");
		if (directStatsPlayers > 0)
		{
			list.Add($"direct {directStatsPlayers}/{starterCount}");
		}
		if (logStatsPlayers > 0)
		{
			list.Add($"tier/log {logStatsPlayers}/{starterCount}");
		}
		if (tierStatsPlayers > 0)
		{
			list.Add($"tier maps {totalTierMaps}");
		}
		else if (totalTierMaps <= 0)
		{
			list.Add("match history 0");
		}
		if (isManagedTeamFocus && statsCovered >= starterCount && (averageSignals >= 4.0 || directStatsPlayers >= starterCount || logStatsPlayers >= starterCount || tierStatsPlayers >= starterCount || totalTierMaps >= Math.Max(10, starterCount * 5)))
		{
			list.Add("100% подтверждено статистикой своей команды");
		}
		else if (isManagedTeamFocus && statsCovered >= starterCount)
		{
			list.Add("полный состав есть, но глубина статистики ещё не идеальная");
		}
		else if (!isManagedTeamFocus)
		{
			list.Add("чужая команда ограничена scouting-cap");
		}
		else if (statsCovered < starterCount)
		{
			list.Add("до 100% не хватает статов основы");
		}
		return string.Join("; ", list);
	}

	private static string BuildRecommendationAudit(string bestSwap, string weakest, IReadOnlyList<PlayerScores> players, IReadOnlyList<PlayerScores> bench, double teamFit, bool isManagedTeamFocus)
	{
		if (players.Count == 0)
		{
			return "нет основы";
		}
		if (bench.Count == 0)
		{
			return "bench-only: нет запаса для сравнения; scouting/трансферы не учитываются";
		}
		string text = (string.IsNullOrWhiteSpace(weakest) ? "N/A" : weakest);
		string text2 = (isManagedTeamFocus ? "managed-team" : "scouting");
		if (string.Equals(bestSwap, "No clear upgrade", StringComparison.OrdinalIgnoreCase))
		{
			return $"{text2}; bench-only: weakest={text}; нет запаса, который даёт явный team+role апгрейд; teamFit={teamFit:0.0}";
		}
		if (string.Equals(bestSwap, "No bench candidates", StringComparison.OrdinalIgnoreCase))
		{
			return $"{text2}; bench-only: weakest={text}; нет запаса; scouting/трансферы не учитываются; teamFit={teamFit:0.0}";
		}
		if (string.IsNullOrWhiteSpace(bestSwap) || bestSwap == "N/A")
		{
			return text2 + "; bench-only: weakest=" + text + "; рекомендация не рассчитана";
		}
		return $"{text2}; bench-only: weakest={text}; candidate={bestSwap}; teamFit={teamFit:0.0}";
	}

	private static string BuildDataConfidence(int statsCovered, int starterCount, IReadOnlyList<PlayerScores> players, double objectiveConfidence, bool isManagedTeamFocus)
	{
		if (starterCount <= 0)
		{
			return "no live starters";
		}
		double value = (double)statsCovered * 100.0 / (double)starterCount;
		int num = players.Count((PlayerScores p) => p.StatsSignals >= 4);
		string value2 = $", confidence {objectiveConfidence:0}%";
		if (isManagedTeamFocus && statsCovered == starterCount)
		{
			return $"managed-team evidence: stats {statsCovered}/{starterCount}{value2}";
		}
		if (statsCovered == starterCount && num >= Math.Max(1, starterCount - 1))
		{
			return $"high: stats {statsCovered}/{starterCount}{value2}";
		}
		if (statsCovered < Math.Max(1, starterCount - 1))
		{
			if (statsCovered <= 0)
			{
				return $"attribute-only: stats 0/{starterCount}{value2}";
			}
			return $"partial: stats {statsCovered}/{starterCount} ({value:0}%){value2}";
		}
		return $"good: stats {statsCovered}/{starterCount}{value2}";
	}

	private static string BuildMatchHistoryStatus(int statsCovered, int starterCount, int directStatsPlayers, int logStatsPlayers, int tierStatsPlayers, int totalTierMaps, bool isManagedTeamFocus)
	{
		if (starterCount <= 0)
		{
			return "no live roster";
		}
		if (totalTierMaps <= 0 && statsCovered <= 0)
		{
			if (!isManagedTeamFocus)
			{
				return "no-match-history: scouting uses live attributes only";
			}
			return "new-save/no-match-history: using full live attributes";
		}
		if (totalTierMaps <= 0 && statsCovered > 0)
		{
			return $"profile stats {statsCovered}/{starterCount}; no tier match history yet";
		}
		if (tierStatsPlayers >= starterCount)
		{
			return $"tier history {tierStatsPlayers}/{starterCount}, maps {totalTierMaps}";
		}
		if (tierStatsPlayers > 0)
		{
			return $"partial tier history {tierStatsPlayers}/{starterCount}, maps {totalTierMaps}";
		}
		if (directStatsPlayers > 0 || logStatsPlayers > 0)
		{
			return $"profile/tier signals {statsCovered}/{starterCount}";
		}
		return "live attributes only";
	}

	private static void SyncTransferRadarScope(RosterSnapshot roster, bool isManagedTeamFocus)
	{
		if (!isManagedTeamFocus || string.IsNullOrWhiteSpace(roster.TeamName))
		{
			return;
		}
		string text = NormalizeTeamName(roster.TeamName);
		if (text.Length != 0)
		{
			if (string.IsNullOrWhiteSpace(s_transferPoolManagedTeamKey))
			{
				s_transferPoolManagedTeamKey = text;
			}
			else if (!string.Equals(s_transferPoolManagedTeamKey, text, StringComparison.OrdinalIgnoreCase))
			{
				s_transferPool.Clear();
				s_transferPoolManagedTeamKey = text;
			}
		}
	}

	private void UpdateTransferRadarPool(RosterSnapshot roster, bool isManagedTeamFocus)
	{
		if (!_enableTransferRadar || isManagedTeamFocus || string.IsNullOrWhiteSpace(roster.TeamName) || roster.Players.Count == 0)
		{
			return;
		}
		foreach (PlayerSnapshot item in roster.Players.Where((PlayerSnapshot p) => !string.IsNullOrWhiteSpace(p.Nick)))
		{
			string text = NormalizeTeamName(roster.TeamName) + ":" + NormalizeTeamName(item.Nick);
			if (text.Length > 1)
			{
				s_transferPool[text] = new CachedTransferCandidate
				{
					Player = CloneAs(item, starter: false, item.IsCaptain),
					TeamName = roster.TeamName,
					SeenAt = DateTime.UtcNow
				};
			}
		}
		if (s_transferPool.Count <= 240)
		{
			return;
		}
		foreach (string item2 in (from kv in s_transferPool.OrderBy<KeyValuePair<string, CachedTransferCandidate>, DateTime>((KeyValuePair<string, CachedTransferCandidate> kv) => kv.Value.SeenAt).Take(s_transferPool.Count - 180)
			select kv.Key).ToList())
		{
			s_transferPool.Remove(item2);
		}
	}

	private List<TransferRadarEntry> BuildTransferRadar(RosterSnapshot roster, List<PlayerSnapshot> starters, IReadOnlyList<PlayerScores> currentScoresList, double currentTeamFit, bool isManagedTeamFocus)
	{
		List<TransferRadarEntry> list = new List<TransferRadarEntry>();
		if (!_enableTransferRadar || _transferRadarMaxRows <= 0)
		{
			return list;
		}
		if (!isManagedTeamFocus || starters.Count <= 0 || s_transferPool.Count <= 0)
		{
			return list;
		}
		HashSet<string> hashSet = new HashSet<string>(roster.Players.Select((PlayerSnapshot p) => NormalizeTeamName(p.Nick)), StringComparer.OrdinalIgnoreCase);
		Dictionary<string, PlayerScores> dictionary = currentScoresList.ToDictionary<PlayerScores, string>((PlayerScores p) => p.Nick, StringComparer.OrdinalIgnoreCase);
		double num = ((IEnumerable<PlayerSnapshot>)starters).Average((Func<PlayerSnapshot, double>)Firepower);
		foreach (CachedTransferCandidate value2 in s_transferPool.Values)
		{
			PlayerSnapshot candidate = value2.Player;
			if (string.IsNullOrWhiteSpace(candidate.Nick) || hashSet.Contains(NormalizeTeamName(candidate.Nick)))
			{
				continue;
			}
			foreach (PlayerSnapshot outgoing in starters)
			{
				if (IsRoleCompatibleSwap(outgoing, candidate, starters) && (outgoing.Role != PlayerRole.AWPer || !IsProtectedAwper(outgoing) || HasComparableAwperProof(candidate, outgoing)))
				{
					List<PlayerSnapshot> list2 = roster.Players.Select((PlayerSnapshot p) => string.Equals(p.Nick, outgoing.Nick, StringComparison.OrdinalIgnoreCase) ? CloneAs(candidate, starter: true, candidate.Role == PlayerRole.IGL || candidate.IsCaptain || (outgoing.IsCaptain && candidate.Leadership >= 17.0)) : CloneAs(p, p.IsStarter, p.IsCaptain && !string.Equals(p.Nick, outgoing.Nick, StringComparison.OrdinalIgnoreCase))).ToList();
					RosterSnapshot roster2 = new RosterSnapshot
					{
						TeamName = roster.TeamName,
						Players = list2
					};
					double num2 = EvaluateTeamFitOnly(roster2) - currentTeamFit;
					PlayerScores value;
					double num3 = (dictionary.TryGetValue(outgoing.Nick, out value) ? value.RoleAdjusted : ScorePlayer(outgoing, num).RoleAdjusted);
					List<PlayerSnapshot> list3 = list2.Where((PlayerSnapshot p) => p.IsStarter).ToList();
					double teamAverageFirepower = ((list3.Count == 0) ? num : ((IEnumerable<PlayerSnapshot>)list3).Average((Func<PlayerSnapshot, double>)Firepower));
					PlayerScores playerScores = ScorePlayer(candidate, teamAverageFirepower);
					double num4 = playerScores.RoleAdjusted - num3;
					if (!(num4 < 0.25) || !(num2 < 0.2))
					{
						string evidence = BuildTransferEvidence(candidate, playerScores);
						string profile = BuildTransferProfile(candidate, outgoing);
						string lane = BuildTransferLane(candidate, outgoing, starters);
						string risk = BuildTransferRisk(candidate, outgoing, lane, playerScores);
						string confidence = BuildTransferConfidence(candidate, playerScores);
						string tier = BuildTransferTier(num2, num4, candidate, outgoing, lane, confidence);
						string action = BuildTransferAction(tier, lane, risk, confidence);
						list.Add(new TransferRadarEntry
						{
							CandidateNick = candidate.Nick,
							CandidateTeam = value2.TeamName,
							ReplaceNick = outgoing.Nick,
							Role = candidate.Role.ToString(),
							TeamFitDelta = num2,
							RoleDelta = num4,
							Tier = tier,
							Profile = profile,
							Evidence = evidence,
							Lane = lane,
							Risk = risk,
							Action = action,
							Confidence = confidence,
							Reason = BuildTransferReason(tier, candidate, outgoing, num2, num4, profile, evidence, lane, risk, action, confidence)
						});
					}
				}
			}
		}
		return (from g in list.GroupBy<TransferRadarEntry, string>((TransferRadarEntry e) => NormalizeTeamName(e.CandidateTeam) + ":" + NormalizeTeamName(e.CandidateNick), StringComparer.OrdinalIgnoreCase)
			select g.OrderByDescending(ScoreTransferEntry).First() into e
			orderby ScoreTransferEntry(e) descending, e.RoleDelta descending
			select e).Take(_transferRadarMaxRows).ToList();
	}

	private static double ScoreTransferEntry(TransferRadarEntry e)
	{
		string tier = e.Tier;
		double num = ((tier == "priority") ? 3.0 : ((!(tier == "trial")) ? 0.0 : 1.5));
		double num2 = num;
		double num3 = e.Lane switch
		{
			"role-safe" => 0.45, 
			"structure-shift" => -0.15, 
			"luxury-awp" => -1.15, 
			"role-change" => -0.65, 
			_ => 0.0, 
		};
		tier = e.Confidence;
		num = ((tier == "high") ? 0.35 : ((!(tier == "medium")) ? (-0.25) : 0.15));
		double num4 = num;
		double num5 = ((!string.IsNullOrWhiteSpace(e.Risk) && e.Risk.Contains("low-evidence", StringComparison.OrdinalIgnoreCase)) ? 0.55 : 0.0);
		return num2 + num3 + num4 - num5 + e.TeamFitDelta * 1.4 + e.RoleDelta;
	}

	private static string BuildTransferTier(double teamDelta, double roleDelta, PlayerSnapshot candidate, PlayerSnapshot outgoing, string lane, string confidence)
	{
		bool flag = ((confidence == "high" || confidence == "medium") ? true : false);
		bool flag2 = flag;
		if (outgoing.Role == PlayerRole.AWPer && !HasComparableAwperProof(candidate, outgoing))
		{
			return "watch";
		}
		if (lane == "luxury-awp")
		{
			return "watch";
		}
		if (lane == "role-change" && teamDelta < 0.75)
		{
			return "watch";
		}
		if (!flag2 && roleDelta < 1.25)
		{
			return "watch";
		}
		if (teamDelta >= 0.55 && roleDelta >= 0.65 && confidence == "high" && lane == "role-safe")
		{
			return "priority";
		}
		if (teamDelta >= 0.7 && roleDelta >= 0.9 && confidence == "high" && lane == "structure-shift")
		{
			return "priority";
		}
		if (teamDelta >= 0.25 && roleDelta >= 0.35 && flag2)
		{
			return "trial";
		}
		if (teamDelta >= 0.45 && roleDelta >= 0.65 && confidence == "low")
		{
			return "trial";
		}
		return "watch";
	}

	private static string BuildTransferProfile(PlayerSnapshot candidate, PlayerSnapshot outgoing)
	{
		if (candidate.Role == PlayerRole.AWPer || candidate.AWP >= 18.5)
		{
			return "AWP target";
		}
		if (candidate.Role == PlayerRole.IGL || candidate.IsCaptain || candidate.Leadership >= 17.0)
		{
			return "IGL/structure target";
		}
		if (candidate.Rifle >= 19.0 && Firepower(candidate) >= 17.2)
		{
			return "star-rifler target";
		}
		if (Utility(candidate) >= 17.0 && candidate.Teamwork >= 16.0)
		{
			return "system/utility target";
		}
		if (candidate.Clutch >= 17.0)
		{
			return "clutch target";
		}
		if (outgoing.Role == PlayerRole.Rifler)
		{
			return "rifle upgrade watch";
		}
		return "role-fit watch";
	}

	private static string BuildTransferLane(PlayerSnapshot candidate, PlayerSnapshot outgoing, List<PlayerSnapshot> currentStarters)
	{
		if (candidate.Role == outgoing.Role)
		{
			return "role-safe";
		}
		bool flag = currentStarters.Any((PlayerSnapshot p) => p.Role == PlayerRole.AWPer && IsProtectedAwper(p));
		if ((candidate.Role == PlayerRole.AWPer || candidate.AWP >= 18.5) && outgoing.Role != PlayerRole.AWPer)
		{
			if (!flag)
			{
				return "role-change";
			}
			return "luxury-awp";
		}
		if ((candidate.Role == PlayerRole.IGL || candidate.IsCaptain || candidate.Leadership >= 17.0) && outgoing.Role != PlayerRole.IGL && !outgoing.IsCaptain)
		{
			return "structure-shift";
		}
		return "role-change";
	}

	private static string BuildTransferRisk(PlayerSnapshot candidate, PlayerSnapshot outgoing, string lane, PlayerScores score)
	{
		List<string> list = new List<string>();
		int valueOrDefault = candidate.TierMaps.GetValueOrDefault();
		if (score.StatsSignals <= 0 && valueOrDefault <= 0)
		{
			list.Add("low-evidence");
		}
		switch (lane)
		{
		case "luxury-awp":
			list.Add("luxury: AWP role already closed");
			break;
		case "role-change":
			list.Add("role-change");
			break;
		case "structure-shift":
			list.Add("structure-change");
			break;
		}
		if (outgoing.IsCaptain || outgoing.Role == PlayerRole.IGL)
		{
			list.Add("IGL-risk");
		}
		if (list.Count != 0)
		{
			return string.Join(", ", list);
		}
		return "role-safe";
	}

	private static string BuildTransferConfidence(PlayerSnapshot candidate, PlayerScores score)
	{
		int valueOrDefault = candidate.TierMaps.GetValueOrDefault();
		if (valueOrDefault >= 50 || score.StatsSignals >= 5)
		{
			return "high";
		}
		if (valueOrDefault >= 10 || score.StatsSignals >= 2)
		{
			return "medium";
		}
		return "low";
	}

	private static string BuildTransferAction(string tier, string lane, string risk, string confidence)
	{
		switch (lane)
		{
		case "luxury-awp":
			return "watch-only: AWP role closed";
		case "structure-shift":
			if (!(tier == "priority"))
			{
				return "scout structure fit";
			}
			return "plan restructure";
		case "role-change":
			return "scout role-change first";
		default:
			if (tier == "priority" && confidence == "high")
			{
				return "sport priority: compare in game";
			}
			if (tier == "trial")
			{
				return "scout + 5 prac maps";
			}
			return "add to watchlist";
		}
	}

	private static string BuildTransferEvidence(PlayerSnapshot candidate, PlayerScores score)
	{
		int valueOrDefault = candidate.TierMaps.GetValueOrDefault();
		string value = (string.IsNullOrWhiteSpace(score.EvidenceTag) ? "A" : score.EvidenceTag);
		double? num = candidate.Top5Rating ?? candidate.Top10Rating ?? candidate.Top20Rating ?? candidate.Top50Rating;
		string value2 = (num.HasValue ? $", rating {num.Value:0.00}" : string.Empty);
		if (valueOrDefault > 0)
		{
			return $"{value}, maps {valueOrDefault}{value2}";
		}
		if (score.StatsSignals > 0)
		{
			return $"{value}, signals {score.StatsSignals}{value2}";
		}
		return "A, attrs-only";
	}

	private static string BuildTransferReason(string tier, PlayerSnapshot candidate, PlayerSnapshot outgoing, double teamDelta, double roleDelta, string profile, string evidence, string lane, string risk, string action, string confidence)
	{
		string text = ((tier == "priority") ? "приоритет: подтверждённый role+team апгрейд" : ((!(tier == "trial")) ? "следить: профиль интересный, решения сейчас нет" : "тест: похоже на апгрейд, проверь 5 прак-карт"));
		string value = text;
		string value2 = ((string.IsNullOrWhiteSpace(risk) || risk == "role-safe") ? "role-safe" : risk);
		return $"{value}; action={action}; confidence={confidence}; {profile}; {lane}; {value2}; вместо {outgoing.Nick}; {evidence}; team {teamDelta:+0.0;-0.0}, role {roleDelta:+0.0;-0.0}";
	}

	private string BuildReleaseAuditStatus(RosterSnapshot roster, bool isManagedTeamFocus, double objectiveConfidence, int statsCovered, int starterCount, int totalTierMaps, int transferRows)
	{
		string value = (isManagedTeamFocus ? "моя команда OK" : "scouting");
		string value2 = ((starterCount > 0 && statsCovered >= starterCount) ? $"статы {statsCovered}/{starterCount} OK" : $"статы {statsCovered}/{Math.Max(1, starterCount)}");
		string value3 = ((totalTierMaps > 0) ? $"история {totalTierMaps} карт" : "история 0");
		string value4 = ((!_enableTransferRadar) ? "radar off" : ((s_transferPool.Count <= 0) ? "radar pool 0" : $"radar pool {s_transferPool.Count}, показано {transferRows}"));
		string value5 = ((isManagedTeamFocus && objectiveConfidence >= 99.5 && starterCount >= 5 && statsCovered >= Math.Min(5, starterCount)) ? "release ready" : "release check");
		return $"{value5}: {value}; аналитика {objectiveConfidence:0}%; {value2}; {value3}; {value4}; sports-only; read-only";
	}

	private string BuildTransferRadarStatus(bool isManagedTeamFocus, int shownRows)
	{
		if (!_enableTransferRadar)
		{
			return "off";
		}
		if (!isManagedTeamFocus)
		{
			return "scouting-pool: этот состав добавлен в пул наблюдения";
		}
		if (s_transferPool.Count <= 0)
		{
			return "пул пуст: открой чужие команды/скаутинг/free agents, чтобы собрать кандидатов";
		}
		if (shownRows > 0)
		{
			return $"пул {s_transferPool.Count}: спортивный transfer radar; деньги/контракты игрок проверяет сам";
		}
		return $"пул {s_transferPool.Count}: явных кандидатов лучше текущих ролей пока нет; продолжай собирать пул";
	}

	private string FindBestSwap(RosterSnapshot roster, List<PlayerSnapshot> starters, double currentTeamFit)
	{
		List<PlayerSnapshot> list = roster.Bench.ToList();
		if (list.Count == 0)
		{
			return "No bench candidates";
		}
		Dictionary<string, PlayerScores> dictionary = starters.Select((PlayerSnapshot p) => ScorePlayer(p, ((IEnumerable<PlayerSnapshot>)starters).Average((Func<PlayerSnapshot, double>)Firepower))).ToDictionary<PlayerScores, string>((PlayerScores p) => p.Nick, StringComparer.OrdinalIgnoreCase);
		double minimumSwapTeamFitDelta = _minimumSwapTeamFitDelta;
		double minimumSwapRoleDelta = _minimumSwapRoleDelta;
		double num = minimumSwapTeamFitDelta;
		string result = "No clear upgrade";
		foreach (PlayerSnapshot outP in starters)
		{
			foreach (PlayerSnapshot inP in list)
			{
				if (!IsRoleCompatibleSwap(outP, inP, starters))
				{
					continue;
				}
				List<PlayerSnapshot> list2 = roster.Players.Select(delegate(PlayerSnapshot p)
				{
					if (p.Nick == outP.Nick)
					{
						return CloneAs(p, starter: false, captain: false);
					}
					return (p.Nick == inP.Nick) ? CloneAs(p, starter: true, inP.Role == PlayerRole.IGL || inP.IsCaptain) : CloneAs(p, p.IsStarter, p.IsCaptain);
				}).ToList();
				if (inP.Role == PlayerRole.IGL || inP.IsCaptain)
				{
					list2 = list2.Select((PlayerSnapshot p) => CloneAs(p, p.IsStarter, p.IsStarter && p.Nick == inP.Nick)).ToList();
				}
				RosterSnapshot roster2 = new RosterSnapshot
				{
					TeamName = roster.TeamName,
					Players = list2
				};
				double num2 = EvaluateTeamFitOnly(roster2) - currentTeamFit;
				List<PlayerSnapshot> altStarters = list2.Where((PlayerSnapshot p) => p.IsStarter).ToList();
				Dictionary<string, PlayerScores> dictionary2 = altStarters.Select((PlayerSnapshot p) => ScorePlayer(p, ((IEnumerable<PlayerSnapshot>)altStarters).Average((Func<PlayerSnapshot, double>)Firepower))).ToDictionary<PlayerScores, string>((PlayerScores p) => p.Nick, StringComparer.OrdinalIgnoreCase);
				PlayerScores value;
				double num3 = (dictionary.TryGetValue(outP.Nick, out value) ? value.RoleAdjusted : 0.0);
				PlayerScores value2;
				double num4 = (dictionary2.TryGetValue(inP.Nick, out value2) ? value2.RoleAdjusted : 0.0) - num3;
				if (outP.Role != PlayerRole.AWPer || !IsProtectedAwper(outP) || HasComparableAwperProof(inP, outP))
				{
					double num5 = ((outP.Role != PlayerRole.AWPer) ? minimumSwapRoleDelta : (IsProtectedAwper(outP) ? 1.15 : 0.65));
					if (num2 >= minimumSwapTeamFitDelta && num2 > num && num4 >= num5)
					{
						num = num2;
						result = $"{outP.Nick} -> {inP.Nick} ({num2:+0.0;-0.0})";
					}
				}
			}
		}
		return result;
	}

	private static bool IsProtectedAwper(PlayerSnapshot p)
	{
		if (p.Role == PlayerRole.AWPer)
		{
			if (!HasEliteTierStats(p))
			{
				if (AWPScore(p) >= 18.6)
				{
					return PerformanceScore(p) >= 17.3;
				}
				return false;
			}
			return true;
		}
		return false;
	}

	private static bool HasComparableAwperProof(PlayerSnapshot incoming, PlayerSnapshot outgoing)
	{
		if (incoming.Role != PlayerRole.AWPer && incoming.AWP < 18.0)
		{
			return false;
		}
		if (HasEliteTierStats(incoming))
		{
			return true;
		}
		if (!HasEliteTierStats(outgoing) && AWPScore(incoming) >= AWPScore(outgoing) + 0.6)
		{
			return true;
		}
		return false;
	}

	private static bool IsRoleCompatibleSwap(PlayerSnapshot outP, PlayerSnapshot inP, List<PlayerSnapshot> currentStarters)
	{
		if (outP.Role == PlayerRole.AWPer && currentStarters.Count((PlayerSnapshot p) => p.Role == PlayerRole.AWPer) <= 1 && inP.Role != PlayerRole.AWPer && inP.AWP < 17.0)
		{
			return false;
		}
		if ((outP.IsCaptain || outP.Role == PlayerRole.IGL) && currentStarters.Count((PlayerSnapshot p) => p.IsCaptain || p.Role == PlayerRole.IGL) <= 1 && !inP.IsCaptain && inP.Role != PlayerRole.IGL && !(inP.Leadership >= 17.0))
		{
			return false;
		}
		return true;
	}

	private static PlayerSnapshot CloneAs(PlayerSnapshot p, bool starter, bool captain)
	{
		return new PlayerSnapshot
		{
			Nick = p.Nick,
			Role = p.Role,
			IsStarter = starter,
			IsCaptain = captain,
			Skill = p.Skill,
			AWP = p.AWP,
			Rifle = p.Rifle,
			Pistol = p.Pistol,
			Grenades = p.Grenades,
			Creativity = p.Creativity,
			Clutch = p.Clutch,
			Tactics = p.Tactics,
			Leadership = p.Leadership,
			Teamwork = p.Teamwork,
			Morale = p.Morale,
			Stress = p.Stress,
			Loyalty = p.Loyalty,
			Productivity = p.Productivity,
			Reaction = p.Reaction,
			Perception = p.Perception,
			Immunity = p.Immunity,
			Strength = p.Strength,
			Stamina = p.Stamina,
			Form = p.Form,
			Health = p.Health,
			SalaryMonthly = p.SalaryMonthly,
			GameRating = p.GameRating,
			ImpactRating = p.ImpactRating,
			KdRatio = p.KdRatio,
			Adr = p.Adr,
			KastPercent = p.KastPercent,
			Top1Rating = p.Top1Rating,
			Top5Rating = p.Top5Rating,
			Top10Rating = p.Top10Rating,
			Top20Rating = p.Top20Rating,
			Top50Rating = p.Top50Rating,
			PerformanceSource = p.PerformanceSource,
			TierMaps = p.TierMaps,
			Top5Kd = p.Top5Kd,
			Top10Kd = p.Top10Kd,
			Top20Kd = p.Top20Kd,
			Top50Kd = p.Top50Kd
		};
	}

	private static string BuildTeamVerdict(double teamFit, double igl, double fp, double awp, double performance)
	{
		if (teamFit >= 18.2 && awp >= 18.0)
		{
			return "Elite title contender";
		}
		if (teamFit >= 18.0)
		{
			return "Elite roster profile";
		}
		if (awp >= 18.0 && fp >= 17.0 && performance >= 17.0)
		{
			return "Elite core, keep AWPer";
		}
		if (igl < 13.5 && fp >= 17.5)
		{
			return "Great firepower, IGL ceiling risk";
		}
		if (igl >= 16.0 && fp < 16.0)
		{
			return "Strong structure, check second-star firepower";
		}
		if (teamFit >= 16.5 && awp >= 17.2)
		{
			return "Contender with stable AWP core";
		}
		if (teamFit >= 16.5)
		{
			return "Contender-level fit";
		}
		return "Needs upgrade";
	}
}
