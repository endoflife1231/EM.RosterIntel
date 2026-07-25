using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using BepInEx.Logging;
using EM.RosterIntel.Data;

namespace EM.RosterIntel.Stats;

internal sealed class OpponentTierStatsLogReader
{
	private sealed class TierStats
	{
		public string Nick { get; init; } = string.Empty;

		public int TotalMaps { get; init; }

		public Tier Top1 { get; init; } = new Tier();

		public Tier Top5 { get; init; } = new Tier();

		public Tier Top10 { get; init; } = new Tier();

		public Tier Top20 { get; init; } = new Tier();

		public Tier Top50 { get; init; } = new Tier();
	}

	private sealed class Tier
	{
		public int Maps { get; init; }

		public double? Rating { get; init; }

		public double? Kd { get; init; }
	}

	private readonly ManualLogSource _log;

	private readonly ModConfig _config;

	private readonly Dictionary<string, TierStats> _cache = new Dictionary<string, TierStats>(StringComparer.OrdinalIgnoreCase);

	private DateTime _lastReadUtc = DateTime.MinValue;

	private bool _loggedOnce;

	private static readonly Regex PlayerLine = new Regex("选手\\s+(?<nick>\\S+)\\s+对阵档位统计：总对阵图\\s+(?<maps>\\d+).*?vs top1:\\s+(?<top1>[^|]+)\\|\\s+vs top5:\\s+(?<top5>[^|]+)\\|\\s+vs top10:\\s+(?<top10>[^|]+)\\|\\s+vs top20:\\s+(?<top20>[^|]+)\\|\\s+vs top50:\\s+(?<top50>.+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private static readonly Regex TierPart = new Regex("(?<maps>\\d+)图(?:\\s+rating=(?<rating>[0-9]+[\\.,]?[0-9]*)\\s+K/D=(?<kd>[0-9]+[\\.,]?[0-9]*))?", RegexOptions.Compiled | RegexOptions.CultureInvariant);

	public OpponentTierStatsLogReader(ManualLogSource log, ModConfig config)
	{
		_log = log;
		_config = config;
	}

	public PlayerSnapshot Enrich(PlayerSnapshot p)
	{
		if (!_config.EnableOpponentTierStatsLogImport.Value)
		{
			return p;
		}
		RefreshIfNeeded();
		if (_cache.Count == 0)
		{
			return p;
		}
		TierStats tierStats = FindForNick(p.Nick);
		if (tierStats == null)
		{
			return p;
		}
		return CopyWithStats(p, tierStats);
	}

	private TierStats? FindForNick(string nick)
	{
		string text = NormalizeNick(nick);
		if (_cache.TryGetValue(text, out TierStats value))
		{
			return value;
		}
		foreach (KeyValuePair<string, TierStats> item in _cache)
		{
			if (item.Key.Contains(text, StringComparison.OrdinalIgnoreCase) || text.Contains(item.Key, StringComparison.OrdinalIgnoreCase))
			{
				return item.Value;
			}
		}
		return null;
	}

	private void RefreshIfNeeded()
	{
		if ((DateTime.UtcNow - _lastReadUtc).TotalSeconds < 6.0)
		{
			return;
		}
		_lastReadUtc = DateTime.UtcNow;
		string text = ResolveLogPath();
		if (string.IsNullOrWhiteSpace(text) || !File.Exists(text))
		{
			return;
		}
		try
		{
			FileInfo fileInfo = new FileInfo(text);
			int num = Math.Max(50000, _config.OpponentTierStatsMaxLogBytes.Value);
			string text2;
			using (FileStream fileStream = new FileStream(text, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
			{
				if (fileStream.Length > num)
				{
					fileStream.Seek(-num, SeekOrigin.End);
				}
				using StreamReader streamReader = new StreamReader(fileStream);
				text2 = streamReader.ReadToEnd();
			}
			int num2 = 0;
			string[] array = text2.Split(new char[2] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
			for (int i = 0; i < array.Length; i++)
			{
				string text3 = array[i].Trim();
				if (!text3.Contains("OpponentTierStats") || !text3.Contains("对阵档位统计"))
				{
					continue;
				}
				Match match = PlayerLine.Match(text3);
				if (match.Success)
				{
					TierStats tierStats = new TierStats
					{
						Nick = match.Groups["nick"].Value.Trim(),
						TotalMaps = ToInt(match.Groups["maps"].Value),
						Top1 = ParseTier(match.Groups["top1"].Value),
						Top5 = ParseTier(match.Groups["top5"].Value),
						Top10 = ParseTier(match.Groups["top10"].Value),
						Top20 = ParseTier(match.Groups["top20"].Value),
						Top50 = ParseTier(match.Groups["top50"].Value)
					};
					if (HasUsableTierStats(tierStats))
					{
						_cache[NormalizeNick(tierStats.Nick)] = tierStats;
						num2++;
					}
				}
			}
			if (!_loggedOnce && num2 > 0)
			{
				_loggedOnce = true;
				ManualLogSource log = _log;
				log.LogInfo($"RosterIntel release fallback-imported OpponentTierStats from log: players={_cache.Count}, file={fileInfo.Name}");
			}
		}
		catch (Exception ex)
		{
			if (_config.LogLiveFailures.Value)
			{
				_log.LogWarning((object)("OpponentTierStats log import failed: " + ex.GetType().Name + " " + ex.Message));
			}
		}
	}

	private string ResolveLogPath()
	{
		if (!string.IsNullOrWhiteSpace(_config.OpponentTierStatsLogPath.Value))
		{
			return _config.OpponentTierStatsLogPath.Value;
		}
		string baseDirectory = AppContext.BaseDirectory;
		string[] array = new string[4]
		{
			Path.Combine(baseDirectory, "BepInEx", "LogOutput.log"),
			Path.Combine(baseDirectory, "BepInEx", "LogOutput.txt"),
			Path.Combine(baseDirectory, "LogOutput.log"),
			Path.Combine(baseDirectory, "LogOutput.txt")
		};
		foreach (string text in array)
		{
			if (File.Exists(text))
			{
				return text;
			}
		}
		return string.Empty;
	}

	private static bool HasUsableTierStats(TierStats s)
	{
		if (s.TotalMaps <= 0)
		{
			return false;
		}
		if (!HasSignal(s.Top1) && !HasSignal(s.Top5) && !HasSignal(s.Top10) && !HasSignal(s.Top20))
		{
			return HasSignal(s.Top50);
		}
		return true;
	}

	private static bool HasSignal(Tier tier)
	{
		if (tier.Maps > 0)
		{
			if (!tier.Rating.HasValue)
			{
				return tier.Kd.HasValue;
			}
			return true;
		}
		return false;
	}

	private static Tier ParseTier(string text)
	{
		Match match = TierPart.Match(text ?? string.Empty);
		if (!match.Success)
		{
			return new Tier();
		}
		return new Tier
		{
			Maps = ToInt(match.Groups["maps"].Value),
			Rating = ToDouble(match.Groups["rating"].Value),
			Kd = ToDouble(match.Groups["kd"].Value)
		};
	}

	private static int ToInt(string? s)
	{
		if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
		{
			return 0;
		}
		return result;
	}

	private static double? ToDouble(string? s)
	{
		if (string.IsNullOrWhiteSpace(s))
		{
			return null;
		}
		s = s.Replace(',', '.');
		if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
		{
			return null;
		}
		return result;
	}

	private static string NormalizeNick(string? nick)
	{
		if (string.IsNullOrWhiteSpace(nick))
		{
			return string.Empty;
		}
		return nick.Trim().Replace("\"", "").Replace("_kovac", "", StringComparison.OrdinalIgnoreCase)
			.ToLowerInvariant();
	}

	private static PlayerSnapshot CopyWithStats(PlayerSnapshot p, TierStats s)
	{
		double? num = s.Top10.Kd ?? s.Top20.Kd ?? s.Top50.Kd ?? s.Top5.Kd ?? p.KdRatio;
		double? num2 = s.Top10.Rating ?? s.Top20.Rating ?? s.Top50.Rating ?? s.Top5.Rating ?? p.ImpactRating;
		return new PlayerSnapshot
		{
			Nick = p.Nick,
			Role = p.Role,
			IsStarter = p.IsStarter,
			IsCaptain = p.IsCaptain,
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
			ImpactRating = (p.ImpactRating ?? num2),
			KdRatio = (p.KdRatio ?? num),
			Adr = p.Adr,
			KastPercent = p.KastPercent,
			Top1Rating = (p.Top1Rating ?? s.Top1.Rating),
			Top5Rating = (p.Top5Rating ?? s.Top5.Rating),
			Top10Rating = (p.Top10Rating ?? s.Top10.Rating),
			Top20Rating = (p.Top20Rating ?? s.Top20.Rating),
			Top50Rating = (p.Top50Rating ?? s.Top50.Rating),
			PerformanceSource = ((p.PerformanceSource == null) ? "OpponentTierStats.log" : (p.PerformanceSource + "+OpponentTierStats.log")),
			TierMaps = (p.TierMaps ?? ((s.TotalMaps > 0) ? new int?(s.TotalMaps) : ((int?)null))),
			Top5Kd = (p.Top5Kd ?? s.Top5.Kd),
			Top10Kd = (p.Top10Kd ?? s.Top10.Kd),
			Top20Kd = (p.Top20Kd ?? s.Top20.Kd),
			Top50Kd = (p.Top50Kd ?? s.Top50.Kd)
		};
	}
}
