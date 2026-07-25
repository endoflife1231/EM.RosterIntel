using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using BepInEx.Logging;
using EM.RosterIntel.Data;

namespace EM.RosterIntel.Stats;

internal sealed class DirectPlayerStatsCache
{
	private sealed class ScanContext
	{
		public string Source { get; }

		public int MaxDepth { get; }

		public int MaxObjects { get; }

		public int ScannedObjects { get; set; }

		public int NewValues { get; set; }

		public HashSet<int> Seen { get; } = new HashSet<int>();

		public ScanContext(string source, int maxDepth, int maxObjects)
		{
			Source = source;
			MaxDepth = maxDepth;
			MaxObjects = maxObjects;
		}
	}

	private sealed class DirectStats
	{
		public string Nick { get; init; } = string.Empty;

		public string Source { get; set; } = string.Empty;

		public DateTime LastUpdateUtc { get; set; }

		public int Maps { get; set; }

		public int TierMaps { get; set; }

		public double Kills { get; set; }

		public double Deaths { get; set; }

		public double Damage { get; set; }

		public double Rounds { get; set; }

		public double? Rating { get; set; }

		public double? Kd { get; set; }

		public double? Adr { get; set; }

		public double? Kast { get; set; }

		public double? Top1Rating { get; set; }

		public double? Top5Rating { get; set; }

		public double? Top10Rating { get; set; }

		public double? Top20Rating { get; set; }

		public double? Top50Rating { get; set; }

		public double? Top5Kd { get; set; }

		public double? Top10Kd { get; set; }

		public double? Top20Kd { get; set; }

		public double? Top50Kd { get; set; }

		public bool HasAnySignal => SignalCount > 0;

		public int SignalCount => new double?[13]
		{
			Rating, Kd, Adr, Kast, Top1Rating, Top5Rating, Top10Rating, Top20Rating, Top50Rating, Top5Kd,
			Top10Kd, Top20Kd, Top50Kd
		}.Count((double? v) => v.HasValue) + ((Maps > 0) ? 1 : 0) + ((Kills > 0.0 || Deaths > 0.0 || Damage > 0.0 || Rounds > 0.0) ? 1 : 0);

		public void Merge(DirectStats other)
		{
			Source = (string.IsNullOrWhiteSpace(Source) ? other.Source : Source);
			LastUpdateUtc = ((other.LastUpdateUtc > LastUpdateUtc) ? other.LastUpdateUtc : LastUpdateUtc);
			Maps = Math.Max(Maps, other.Maps);
			TierMaps = Math.Max(TierMaps, other.TierMaps);
			Kills = Math.Max(Kills, other.Kills);
			Deaths = Math.Max(Deaths, other.Deaths);
			Damage = Math.Max(Damage, other.Damage);
			Rounds = Math.Max(Rounds, other.Rounds);
			if (!Rating.HasValue)
			{
				double? num = (Rating = other.Rating);
			}
			if (!Kd.HasValue)
			{
				double? num = (Kd = other.Kd);
			}
			if (!Adr.HasValue)
			{
				double? num = (Adr = other.Adr);
			}
			if (!Kast.HasValue)
			{
				double? num = (Kast = other.Kast);
			}
			if (!Top1Rating.HasValue)
			{
				double? num = (Top1Rating = other.Top1Rating);
			}
			if (!Top5Rating.HasValue)
			{
				double? num = (Top5Rating = other.Top5Rating);
			}
			if (!Top10Rating.HasValue)
			{
				double? num = (Top10Rating = other.Top10Rating);
			}
			if (!Top20Rating.HasValue)
			{
				double? num = (Top20Rating = other.Top20Rating);
			}
			if (!Top50Rating.HasValue)
			{
				double? num = (Top50Rating = other.Top50Rating);
			}
			if (!Top5Kd.HasValue)
			{
				double? num = (Top5Kd = other.Top5Kd);
			}
			if (!Top10Kd.HasValue)
			{
				double? num = (Top10Kd = other.Top10Kd);
			}
			if (!Top20Kd.HasValue)
			{
				double? num = (Top20Kd = other.Top20Kd);
			}
			if (!Top50Kd.HasValue)
			{
				double? num = (Top50Kd = other.Top50Kd);
			}
			if (!Kd.HasValue && Kills > 0.0 && Deaths > 0.0)
			{
				Kd = Kills / Math.Max(1.0, Deaths);
			}
			if (!Adr.HasValue && Damage > 0.0 && Rounds > 0.0)
			{
				Adr = Damage / Math.Max(1.0, Rounds);
			}
		}

		public DirectStats Clone()
		{
			return (DirectStats)MemberwiseClone();
		}
	}

	private readonly ManualLogSource _log;

	private readonly ModConfig _config;

	private readonly Dictionary<string, DirectStats> _cache = new Dictionary<string, DirectStats>(StringComparer.OrdinalIgnoreCase);

	private readonly object _lock = new object();

	private bool _loggedFirstCapture;

	private int _captureHit;

	private static readonly Regex TierTextRegex = new Regex("vs\\s*top\\s*(?<tier>1|5|10|20|50)\\s*[:：]?\\s*(?<maps>\\d+)[^|\\r\\n]*?(?:rating\\s*=\\s*(?<rating>[0-9]+[\\.,]?[0-9]*))?[^|\\r\\n]*?(?:K/D\\s*=\\s*(?<kd>[0-9]+[\\.,]?[0-9]*))?", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private static readonly Regex ChineseTierTextRegex = new Regex("vs\\s*top(?<tier>1|5|10|20|50)\\s*:\\s*(?<maps>\\d+)图(?:\\s+rating=(?<rating>[0-9]+[\\.,]?[0-9]*)\\s+K/D=(?<kd>[0-9]+[\\.,]?[0-9]*))?", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

	public int CachedPlayers
	{
		get
		{
			lock (_lock)
			{
				return _cache.Count;
			}
		}
	}

	public DirectPlayerStatsCache(ManualLogSource log, ModConfig config)
	{
		_log = log;
		_config = config;
	}

	public void Capture(string source, object? instance, object[]? args)
	{
		if (!_config.EnableDirectPlayerStatsImport.Value)
		{
			return;
		}
		try
		{
			ScanContext scanContext = new ScanContext(source, Math.Clamp(_config.DirectStatsScanDepth.Value, 2, 8), Math.Clamp(_config.DirectStatsMaxObjectsPerCapture.Value, 120, 3000));
			ScanObject(instance, null, scanContext, 0);
			if (args != null)
			{
				foreach (object value in args)
				{
					ScanObject(value, null, scanContext, 0);
				}
			}
			if (scanContext.NewValues <= 0)
			{
				return;
			}
			_captureHit++;
			bool flag = default(bool);
			if (_config.DirectStatsLogFirstCapture.Value && !_loggedFirstCapture)
			{
				_loggedFirstCapture = true;
				ManualLogSource log = _log;
				log.LogInfo($"RosterIntel release direct stats capture OK: source={source}, players={CachedPlayers}, values={scanContext.NewValues}, scanned={scanContext.ScannedObjects}");
			}
			else if (_captureHit <= 3 && _config.LogLiveFailures.Value)
			{
				ManualLogSource log2 = _log;
				log2.LogInfo($"RosterIntel release direct stats capture: source={source}, values={scanContext.NewValues}, scanned={scanContext.ScannedObjects}");
			}
		}
		catch (Exception ex)
		{
			if (_config.LogLiveFailures.Value)
			{
				_log.LogWarning((object)("RosterIntel release direct stats capture failed: " + ex.GetType().Name + " " + ex.Message));
			}
		}
	}

	public PlayerSnapshot Enrich(PlayerSnapshot p)
	{
		if (!_config.EnableDirectPlayerStatsImport.Value)
		{
			return p;
		}
		DirectStats directStats;
		lock (_lock)
		{
			directStats = FindLocked(p.Nick);
			if (directStats == null)
			{
				return p;
			}
			directStats = directStats.Clone();
		}
		return CopyWithStats(p, directStats);
	}

	private DirectStats? FindLocked(string nick)
	{
		string text = NormalizeNick(nick);
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		if (_cache.TryGetValue(text, out DirectStats value))
		{
			return value;
		}
		foreach (KeyValuePair<string, DirectStats> item in _cache)
		{
			if (item.Key.Contains(text, StringComparison.OrdinalIgnoreCase) || text.Contains(item.Key, StringComparison.OrdinalIgnoreCase))
			{
				return item.Value;
			}
		}
		return null;
	}

	private void ScanObject(object? value, string? inheritedNick, ScanContext ctx, int depth)
	{
		if (value == null || depth > ctx.MaxDepth || ctx.ScannedObjects >= ctx.MaxObjects)
		{
			return;
		}
		if (IsSimple(value.GetType()))
		{
			if (value is string text && !string.IsNullOrWhiteSpace(inheritedNick))
			{
				CaptureTierText(inheritedNick, text, ctx);
			}
			return;
		}
		int item = SafeIdentityHash(value);
		if (!ctx.Seen.Add(item))
		{
			return;
		}
		ctx.ScannedObjects++;
		string text2 = ExtractNick(value) ?? inheritedNick;
		if (!string.IsNullOrWhiteSpace(text2))
		{
			DirectStats directStats = ExtractStats(value, text2, ctx.Source);
			if (directStats.HasAnySignal)
			{
				MergeStats(directStats, ctx);
			}
			foreach (string item2 in ExtractStrings(value).Take(12))
			{
				CaptureTierText(text2, item2, ctx);
			}
		}
		if (depth >= ctx.MaxDepth)
		{
			return;
		}
		foreach (object item3 in EnumerateChildren(value))
		{
			if (item3 != null)
			{
				ScanObject(item3, text2, ctx, depth + 1);
				if (ctx.ScannedObjects >= ctx.MaxObjects)
				{
					break;
				}
			}
		}
	}

	private static string? ExtractNick(object obj)
	{
		string[] array = new string[8] { "Nick", "nick", "Nickname", "PlayerNick", "PlayerName", "Name", "name", "_nick" };
		foreach (string name in array)
		{
			string text = ReadString(obj, name);
			if (LooksLikeNick(text))
			{
				return text.Trim();
			}
		}
		array = new string[7] { "Player", "player", "_player", "DataPlayer", "dataPlayer", "Owner", "owner" };
		foreach (string name2 in array)
		{
			object obj2 = ReadMember(obj, name2);
			if (obj2 != null && obj2 != obj)
			{
				string text2 = ExtractNickFromPlayerLike(obj2);
				if (LooksLikeNick(text2))
				{
					return text2.Trim();
				}
			}
		}
		return null;
	}

	private static string? ExtractNickFromPlayerLike(object obj)
	{
		string[] array = new string[7] { "Nick", "nick", "Nickname", "PlayerNick", "Name", "name", "_nick" };
		foreach (string name in array)
		{
			string text = ReadString(obj, name);
			if (LooksLikeNick(text))
			{
				return text.Trim();
			}
		}
		return null;
	}

	private static bool LooksLikeNick(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}
		value = value.Trim();
		int length = value.Length;
		if ((length < 2 || length > 32) ? true : false)
		{
			return false;
		}
		if (value.Contains(' ') && value.Length > 20)
		{
			return false;
		}
		bool flag;
		switch (value.ToLowerInvariant())
		{
		case "player":
		case "name":
		case "unknown":
		case "current team":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (flag)
		{
			return false;
		}
		return value.Any(char.IsLetterOrDigit);
	}

	private DirectStats ExtractStats(object obj, string nick, string source)
	{
		DirectStats directStats = new DirectStats
		{
			Nick = nick,
			Source = source,
			LastUpdateUtc = DateTime.UtcNow
		};
		directStats.Maps = ReadIntAny(obj, "Maps", "MapCount", "PlayedMaps", "TotalMaps", "Games", "Matches").GetValueOrDefault();
		directStats.Rounds = ReadDoubleAny(obj, "Rounds", "RoundCount", "RoundsPlayed", "PlayedRounds").GetValueOrDefault();
		directStats.Kills = ReadDoubleAny(obj, "Kills", "Kill", "Frags", "TotalKills").GetValueOrDefault();
		directStats.Deaths = ReadDoubleAny(obj, "Deaths", "Death", "TotalDeaths").GetValueOrDefault();
		directStats.Damage = ReadDoubleAny(obj, "Damage", "TotalDamage", "Dmg").GetValueOrDefault();
		directStats.Rating = NormalizeImpactRating(ReadDoubleAny(obj, "ImpactRating", "IR", "Rating", "rating", "HltvRating", "HLTVRating", "MapRating", "PlayerRating", "OverallRating"));
		directStats.Kd = NormalizeKd(ReadDoubleAny(obj, "KD", "KDR", "KdRatio", "KDRatio", "KillDeathRatio"));
		directStats.Adr = NormalizeAdr(ReadDoubleAny(obj, "ADR", "Adr", "AverageDamage", "DamagePerRound"));
		directStats.Kast = NormalizeKast(ReadDoubleAny(obj, "KAST", "Kast", "KastPercent", "KastPercentage"));
		if (!directStats.Kd.HasValue && directStats.Kills > 0.0 && directStats.Deaths > 0.0)
		{
			directStats.Kd = directStats.Kills / Math.Max(1.0, directStats.Deaths);
		}
		if (!directStats.Adr.HasValue && directStats.Damage > 0.0 && directStats.Rounds > 0.0)
		{
			directStats.Adr = directStats.Damage / Math.Max(1.0, directStats.Rounds);
		}
		directStats.Top1Rating = NormalizeImpactRating(ReadDoubleAny(obj, "Top1Rating", "VsTop1Rating", "RatingVsTop1"));
		directStats.Top5Rating = NormalizeImpactRating(ReadDoubleAny(obj, "Top5Rating", "VsTop5Rating", "RatingVsTop5"));
		directStats.Top10Rating = NormalizeImpactRating(ReadDoubleAny(obj, "Top10Rating", "VsTop10Rating", "RatingVsTop10"));
		directStats.Top20Rating = NormalizeImpactRating(ReadDoubleAny(obj, "Top20Rating", "VsTop20Rating", "RatingVsTop20"));
		directStats.Top50Rating = NormalizeImpactRating(ReadDoubleAny(obj, "Top50Rating", "VsTop50Rating", "RatingVsTop50"));
		directStats.Top5Kd = NormalizeKd(ReadDoubleAny(obj, "Top5Kd", "VsTop5Kd", "KdVsTop5", "Top5KD", "VsTop5KDRatio"));
		directStats.Top10Kd = NormalizeKd(ReadDoubleAny(obj, "Top10Kd", "VsTop10Kd", "KdVsTop10", "Top10KD", "VsTop10KDRatio"));
		directStats.Top20Kd = NormalizeKd(ReadDoubleAny(obj, "Top20Kd", "VsTop20Kd", "KdVsTop20", "Top20KD", "VsTop20KDRatio"));
		directStats.Top50Kd = NormalizeKd(ReadDoubleAny(obj, "Top50Kd", "VsTop50Kd", "KdVsTop50", "Top50KD", "VsTop50KDRatio"));
		return directStats;
	}

	private void MergeStats(DirectStats incoming, ScanContext ctx)
	{
		string text = NormalizeNick(incoming.Nick);
		if (string.IsNullOrWhiteSpace(text))
		{
			return;
		}
		lock (_lock)
		{
			if (!_cache.TryGetValue(text, out DirectStats value))
			{
				_cache[text] = incoming.Clone();
				ctx.NewValues++;
				return;
			}
			int signalCount = value.SignalCount;
			value.Merge(incoming);
			if (value.SignalCount > signalCount || incoming.HasAnySignal)
			{
				ctx.NewValues++;
			}
		}
	}

	private void CaptureTierText(string nick, string text, ScanContext ctx)
	{
		if (!string.IsNullOrWhiteSpace(text) && (text.Contains("top", StringComparison.OrdinalIgnoreCase) || text.Contains("对阵档位统计", StringComparison.OrdinalIgnoreCase)))
		{
			DirectStats directStats = new DirectStats
			{
				Nick = nick,
				Source = ctx.Source + ":tier-ui",
				LastUpdateUtc = DateTime.UtcNow
			};
			ParseTierMatches(TierTextRegex.Matches(text), directStats);
			ParseTierMatches(ChineseTierTextRegex.Matches(text), directStats);
			if (directStats.HasAnySignal)
			{
				MergeStats(directStats, ctx);
			}
		}
	}

	private static void ParseTierMatches(MatchCollection matches, DirectStats target)
	{
		foreach (Match match in matches)
		{
			if (!match.Success)
			{
				continue;
			}
			int valueOrDefault = ToInt(match.Groups["tier"].Value).GetValueOrDefault();
			int valueOrDefault2 = ToInt(match.Groups["maps"].Value).GetValueOrDefault();
			double? value = ToDouble(match.Groups["rating"].Value);
			double? value2 = ToDouble(match.Groups["kd"].Value);
			target.TierMaps = Math.Max(target.TierMaps, valueOrDefault2);
			switch (valueOrDefault)
			{
			case 1:
			{
				DirectStats directStats = target;
				if (!directStats.Top1Rating.HasValue)
				{
					double? num = (directStats.Top1Rating = NormalizeImpactRating(value));
				}
				break;
			}
			case 5:
			{
				DirectStats directStats = target;
				if (!directStats.Top5Rating.HasValue)
				{
					double? num = (directStats.Top5Rating = NormalizeImpactRating(value));
				}
				directStats = target;
				if (!directStats.Top5Kd.HasValue)
				{
					double? num = (directStats.Top5Kd = NormalizeKd(value2));
				}
				break;
			}
			case 10:
			{
				DirectStats directStats = target;
				if (!directStats.Top10Rating.HasValue)
				{
					double? num = (directStats.Top10Rating = NormalizeImpactRating(value));
				}
				directStats = target;
				if (!directStats.Top10Kd.HasValue)
				{
					double? num = (directStats.Top10Kd = NormalizeKd(value2));
				}
				break;
			}
			case 20:
			{
				DirectStats directStats = target;
				if (!directStats.Top20Rating.HasValue)
				{
					double? num = (directStats.Top20Rating = NormalizeImpactRating(value));
				}
				directStats = target;
				if (!directStats.Top20Kd.HasValue)
				{
					double? num = (directStats.Top20Kd = NormalizeKd(value2));
				}
				break;
			}
			case 50:
			{
				DirectStats directStats = target;
				if (!directStats.Top50Rating.HasValue)
				{
					double? num = (directStats.Top50Rating = NormalizeImpactRating(value));
				}
				directStats = target;
				if (!directStats.Top50Kd.HasValue)
				{
					double? num = (directStats.Top50Kd = NormalizeKd(value2));
				}
				break;
			}
			}
		}
	}

	private static IEnumerable<object?> EnumerateChildren(object obj)
	{
		if (obj is string)
		{
			yield break;
		}
		if (obj is IEnumerable enumerable)
		{
			int i = 0;
			foreach (object item in enumerable)
			{
				if (i++ >= 80)
				{
					break;
				}
				if (item != null)
				{
					yield return item;
				}
			}
			yield break;
		}
		foreach (FieldInfo item2 in SafeFields(obj.GetType(), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Take(80))
		{
			if (!item2.FieldType.IsPointer && !IsSimple(item2.FieldType))
			{
				object obj2 = null;
				try
				{
					obj2 = item2.GetValue(obj);
				}
				catch
				{
				}
				if (obj2 != null && obj2 != obj)
				{
					yield return obj2;
				}
			}
		}
		foreach (PropertyInfo item3 in SafeProperties(obj.GetType(), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Take(80))
		{
			if (item3.GetIndexParameters().Length == 0 && !IsSimple(item3.PropertyType))
			{
				object obj4 = null;
				try
				{
					obj4 = item3.GetValue(obj, null);
				}
				catch
				{
				}
				if (obj4 != null && obj4 != obj)
				{
					yield return obj4;
				}
			}
		}
	}

	private static IEnumerable<string> ExtractStrings(object obj)
	{
		foreach (FieldInfo item in SafeFields(obj.GetType(), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Take(80))
		{
			if (!(item.FieldType != typeof(string)))
			{
				string text = null;
				try
				{
					text = item.GetValue(obj) as string;
				}
				catch
				{
				}
				if (!string.IsNullOrWhiteSpace(text))
				{
					yield return text;
				}
			}
		}
		foreach (PropertyInfo item2 in SafeProperties(obj.GetType(), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Take(80))
		{
			if (!(item2.PropertyType != typeof(string)) && item2.GetIndexParameters().Length == 0)
			{
				string text2 = null;
				try
				{
					text2 = item2.GetValue(obj, null) as string;
				}
				catch
				{
				}
				if (!string.IsNullOrWhiteSpace(text2))
				{
					yield return text2;
				}
			}
		}
	}

	private static IEnumerable<FieldInfo> SafeFields(Type t, BindingFlags flags)
	{
		try
		{
			return t.GetFields(flags);
		}
		catch
		{
			return Array.Empty<FieldInfo>();
		}
	}

	private static IEnumerable<PropertyInfo> SafeProperties(Type t, BindingFlags flags)
	{
		try
		{
			return t.GetProperties(flags);
		}
		catch
		{
			return Array.Empty<PropertyInfo>();
		}
	}

	private static double? ReadDoubleAny(object obj, params string[] names)
	{
		foreach (string name in names)
		{
			double? result = ToDouble(ReadMember(obj, name));
			if (result.HasValue)
			{
				return result;
			}
		}
		return null;
	}

	private static int? ReadIntAny(object obj, params string[] names)
	{
		foreach (string name in names)
		{
			int? result = ToInt(ReadMember(obj, name));
			if (result.HasValue)
			{
				return result;
			}
		}
		return null;
	}

	private static object? ReadMember(object obj, string name)
	{
		BindingFlags bindingAttr = BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
		try
		{
			PropertyInfo property = obj.GetType().GetProperty(name, bindingAttr);
			if (property != null && property.GetIndexParameters().Length == 0)
			{
				return property.GetValue(obj, null);
			}
		}
		catch
		{
		}
		try
		{
			FieldInfo field = obj.GetType().GetField(name, bindingAttr);
			if (field != null)
			{
				return field.GetValue(obj);
			}
		}
		catch
		{
		}
		return null;
	}

	private static string? ReadString(object obj, string name)
	{
		object obj2 = ReadMember(obj, name);
		if (obj2 == null)
		{
			return null;
		}
		if (obj2 is string result)
		{
			return result;
		}
		return Convert.ToString(obj2, CultureInfo.InvariantCulture);
	}

	private static double? ToDouble(object? value)
	{
		if (value == null)
		{
			return null;
		}
		try
		{
			return (value is byte b) ? new double?((int)b) : ((value is sbyte b2) ? new double?(b2) : ((value is short num) ? new double?(num) : ((value is ushort num2) ? new double?((int)num2) : ((value is int num3) ? new double?(num3) : ((value is uint num4) ? new double?(num4) : ((value is long num5) ? new double?(num5) : ((value is ulong num6) ? new double?(num6) : ((value is float num7) ? new double?(num7) : ((value is double value2) ? new double?(value2) : ((!(value is decimal num8)) ? ToDouble(Convert.ToString(value, CultureInfo.InvariantCulture)) : new double?((double)num8)))))))))));
		}
		catch
		{
			return null;
		}
	}

	private static double? ToDouble(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}
		value = value.Replace(',', '.');
		if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
		{
			return null;
		}
		return result;
	}

	private static int? ToInt(object? value)
	{
		double? num = ToDouble(value);
		if (!num.HasValue)
		{
			return null;
		}
		return (int)Math.Round(num.Value);
	}

	private static int? ToInt(string? value)
	{
		if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
		{
			return null;
		}
		return result;
	}

	private static double? NormalizeImpactRating(double? value)
	{
		if (!value.HasValue)
		{
			return null;
		}
		double value2 = value.Value;
		if (double.IsNaN(value2) || double.IsInfinity(value2))
		{
			return null;
		}
		if (!(value2 >= 0.2) || !(value2 <= 2.5))
		{
			return null;
		}
		return value2;
	}

	private static double? NormalizeKd(double? value)
	{
		if (!value.HasValue)
		{
			return null;
		}
		double value2 = value.Value;
		if (double.IsNaN(value2) || double.IsInfinity(value2))
		{
			return null;
		}
		if (!(value2 >= 0.0) || !(value2 <= 5.0))
		{
			return null;
		}
		return value2;
	}

	private static double? NormalizeAdr(double? value)
	{
		if (!value.HasValue)
		{
			return null;
		}
		double value2 = value.Value;
		if (double.IsNaN(value2) || double.IsInfinity(value2))
		{
			return null;
		}
		if (!(value2 >= 0.0) || !(value2 <= 250.0))
		{
			return null;
		}
		return value2;
	}

	private static double? NormalizeKast(double? value)
	{
		if (!value.HasValue)
		{
			return null;
		}
		double num = ((value.Value <= 1.0) ? (value.Value * 100.0) : value.Value);
		if (double.IsNaN(num) || double.IsInfinity(num))
		{
			return null;
		}
		if (!(num >= 0.0) || !(num <= 100.0))
		{
			return null;
		}
		return num;
	}

	private static bool IsSimple(Type t)
	{
		if (!t.IsPrimitive && !t.IsEnum && !(t == typeof(string)) && !(t == typeof(decimal)))
		{
			return t == typeof(DateTime);
		}
		return true;
	}

	private static int SafeIdentityHash(object obj)
	{
		try
		{
			return RuntimeHelpers.GetHashCode(obj);
		}
		catch
		{
			return obj.GetHashCode();
		}
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

	private static PlayerSnapshot CopyWithStats(PlayerSnapshot p, DirectStats s)
	{
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
			ImpactRating = (p.ImpactRating ?? s.Rating),
			KdRatio = (p.KdRatio ?? s.Kd),
			Adr = (p.Adr ?? s.Adr),
			KastPercent = (p.KastPercent ?? s.Kast),
			Top1Rating = (p.Top1Rating ?? s.Top1Rating),
			Top5Rating = (p.Top5Rating ?? s.Top5Rating),
			Top10Rating = (p.Top10Rating ?? s.Top10Rating),
			Top20Rating = (p.Top20Rating ?? s.Top20Rating),
			Top50Rating = (p.Top50Rating ?? s.Top50Rating),
			PerformanceSource = ((p.PerformanceSource == null) ? "direct-stats" : (p.PerformanceSource + "+direct-stats")),
			TierMaps = (p.TierMaps ?? ResolveTierMaps(s)),
			Top5Kd = (p.Top5Kd ?? s.Top5Kd),
			Top10Kd = (p.Top10Kd ?? s.Top10Kd),
			Top20Kd = (p.Top20Kd ?? s.Top20Kd),
			Top50Kd = (p.Top50Kd ?? s.Top50Kd)
		};
	}

	private static int? ResolveTierMaps(DirectStats s)
	{
		if (s.TierMaps > 0)
		{
			return s.TierMaps;
		}
		if (s.Maps > 0)
		{
			return s.Maps;
		}
		return null;
	}
}
