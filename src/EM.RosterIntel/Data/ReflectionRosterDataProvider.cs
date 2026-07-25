using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Logging;
using EM.RosterIntel.Stats;
using EM.RosterIntel.Util;
using UnityEngine;

namespace EM.RosterIntel.Data;

public sealed class ReflectionRosterDataProvider : IRosterDataProvider, IDiagnosticRosterProvider
{
	private readonly ManualLogSource _log;

	private readonly ModConfig _config;

	private bool _probed;

	private bool _loggedLiveOnce;

	private bool _loggedFallbackOnce;

	private bool _loggedFailureOnce;

	private Dictionary<string, object>? _attributeTypeCache;

	private readonly OpponentTierStatsLogReader _tierStatsLogReader;

	private readonly DirectPlayerStatsCache _directStatsCache;

	private readonly object _captureLock = new object();

	private object? _capturedSquadWindow;

	private object? _capturedTeam;

	private readonly List<object> _capturedMainPlayers = new List<object>();

	private readonly List<object> _capturedBenchPlayers = new List<object>();

	private readonly List<object> _capturedCards = new List<object>();

	private string _captureSource = "none";

	private int _captureRevision;

	private bool? _capturedIsOurTeam;

	private string _lastLoggedFailureKey = string.Empty;

	private string _lastLoggedLiveRosterKey = string.Empty;

	private static readonly string[] AllowedAssemblyNames = new string[5] { "Assembly-CSharp", "EsportsManager", "EM.Core", "EM.Data", "EM.Sdk" };

	private static readonly string[] KeyTypeNames = new string[24]
	{
		"DataPlayer", "DataPlayers", "DataTeam", "DataTeams", "Attributes", "AttributeType", "GameManager+Player", "GameManager+PlayerList", "GameManager+Team", "GameManager+TeamList",
		"Player", "PlayerOverview", "PlayerAttributesDefenition", "PlayerMainRole", "PlayerSecondaryRole", "SquadWindow", "SquadList", "PlayerSquadCard", "SquadPlayerRow", "HomeTeamMember",
		"HomePlayerRow", "PlayerScouting", "PlayerInScouting", "DynamicScrollPlayerView"
	};

	public string Name => "ReflectionRosterDataProvider.release";

	public bool IsLive => true;

	public string Status { get; private set; } = "not started";

	public string LastSource { get; private set; } = "none";

	public int LastStarterCount { get; private set; }

	public int LastBenchCount { get; private set; }

	public bool LastReadWasLive { get; private set; }

	public ReflectionRosterDataProvider(ManualLogSource log, ModConfig config)
	{
		_log = log;
		_config = config;
		_tierStatsLogReader = new OpponentTierStatsLogReader(log, config);
		_directStatsCache = new DirectPlayerStatsCache(log, config);
	}

	public void CaptureStatsContext(string source, object? instance, object[]? args)
	{
		_directStatsCache.Capture(source, instance, args);
	}

	public void CaptureSquadWindow(object? instance)
	{
		if (instance == null)
		{
			return;
		}
		try
		{
			object obj = ReadMember(instance, "_team") ?? ReadMember(instance, "team") ?? ReadMember(instance, "Team");
			lock (_captureLock)
			{
				_capturedSquadWindow = instance;
				SetCapturedTeamLocked(obj);
				_captureSource = ((obj != null) ? "hook:SquadWindow._team" : "hook:SquadWindow(no team yet)");
				_captureRevision++;
			}
			CaptureSquadList(ReadMember(instance, "_mainSquadViews"));
			CaptureSquadList(ReadMember(instance, "_benchSquadViews"));
		}
		catch (Exception ex)
		{
			if (_config.LogLiveFailures.Value)
			{
				_log.LogWarning((object)("CaptureSquadWindow failed: " + ex.GetType().Name + " " + ex.Message));
			}
		}
	}

	public void CaptureSquadList(object? instance)
	{
		if (instance == null)
		{
			return;
		}
		try
		{
			bool flag = ReadBool(instance, "_isBenchSquad") ?? (ReadBool(instance, "IsBenchSquad") == true);
			List<object> list = ToObjectList(ReadMember(instance, "_views"));
			List<object> list2 = new List<object>();
			foreach (object item in list)
			{
				object obj = ReadMember(item, "_player") ?? ReadMember(item, "Player") ?? ReadMember(item, "player");
				if (obj != null)
				{
					list2.Add(obj);
				}
				CapturePlayerSquadCard(item);
			}
			object obj2 = ReadMember(instance, "_squadWindow");
			object capturedTeamLocked = ((obj2 == null) ? null : (ReadMember(obj2, "_team") ?? ReadMember(obj2, "team") ?? ReadMember(obj2, "Team")));
			lock (_captureLock)
			{
				SetCapturedTeamLocked(capturedTeamLocked);
				if (list2.Count > 0)
				{
					ReplaceOrAppendPlayers(flag ? _capturedBenchPlayers : _capturedMainPlayers, list2);
				}
				_captureSource = (flag ? "hook:SquadList.Init(bench)" : "hook:SquadList.Init(main)");
				_captureRevision++;
			}
		}
		catch (Exception ex)
		{
			if (_config.LogLiveFailures.Value)
			{
				_log.LogWarning((object)("CaptureSquadList failed: " + ex.GetType().Name + " " + ex.Message));
			}
		}
	}

	public void CapturePlayerSquadCard(object? card)
	{
		if (card == null)
		{
			return;
		}
		try
		{
			object obj = ReadMember(card, "_player") ?? ReadMember(card, "Player") ?? ReadMember(card, "player");
			if (obj == null)
			{
				return;
			}
			bool flag = ReadBool(card, "IsMainSquad") ?? (ReadBool(card, "_IsMainSquad") == true);
			lock (_captureLock)
			{
				if (!_capturedCards.Any((object c) => c == card))
				{
					_capturedCards.Add(card);
				}
				ReplaceOrAppendPlayer(flag ? _capturedMainPlayers : _capturedBenchPlayers, obj);
				_captureSource = (flag ? "hook:PlayerSquadCard.Init(main)" : "hook:PlayerSquadCard.Init(bench)");
				_captureRevision++;
			}
		}
		catch (Exception ex)
		{
			if (_config.LogLiveFailures.Value)
			{
				_log.LogWarning((object)("CapturePlayerSquadCard failed: " + ex.GetType().Name + " " + ex.Message));
			}
		}
	}

	public void CaptureSquadListArgs(object? instance, object[]? args)
	{
		if (args == null || args.Length == 0)
		{
			return;
		}
		try
		{
			List<object> list = ToObjectList(args[0]);
			if (list.Count == 0)
			{
				return;
			}
			bool flag = false;
			bool? flag2 = null;
			if (args.Length > 1 && args[1] is bool value)
			{
				flag2 = value;
			}
			object capturedTeamLocked = null;
			if (instance != null)
			{
				flag = ReadBool(instance, "_isBenchSquad") ?? (ReadBool(instance, "IsBenchSquad") == true);
				object obj = ReadMember(instance, "_squadWindow");
				capturedTeamLocked = ((obj == null) ? null : (ReadMember(obj, "_team") ?? ReadMember(obj, "team") ?? ReadMember(obj, "Team")));
			}
			lock (_captureLock)
			{
				SetCapturedTeamLocked(capturedTeamLocked);
				if (flag2.HasValue)
				{
					if (flag2.Value)
					{
						_capturedIsOurTeam = true;
					}
					else if (!_capturedIsOurTeam.HasValue)
					{
						_capturedIsOurTeam = false;
					}
				}
				ReplaceOrAppendPlayers(flag ? _capturedBenchPlayers : _capturedMainPlayers, list);
				_captureSource = (flag ? "hook:SquadList.Init(args:bench)" : "hook:SquadList.Init(args:main)");
				_captureRevision++;
			}
			if (_config.LogLiveRosterOnce.Value && list.Count > 0 && !_loggedLiveOnce)
			{
				ManualLogSource log = _log;
				bool flag3;
				log.LogInfo($"RosterIntel captured SquadList.Init args: players={list.Count}, isBench={flag}, isOurTeam={(flag2.HasValue ? flag2.Value.ToString() : "unknown")}");
			}
		}
		catch (Exception ex)
		{
			if (_config.LogLiveFailures.Value)
			{
				_log.LogWarning((object)("CaptureSquadListArgs failed: " + ex.GetType().Name + " " + ex.Message));
			}
		}
	}

	public void CapturePlayerSquadCardArgs(object? card, object[]? args)
	{
		if (args == null || args.Length == 0)
		{
			return;
		}
		try
		{
			object obj = args[0];
			if (obj == null)
			{
				return;
			}
			bool flag = false;
			if (args.Length > 1 && args[1] is bool flag2)
			{
				flag = flag2;
			}
			else if (card != null)
			{
				flag = ReadBool(card, "IsMainSquad") ?? (ReadBool(card, "_IsMainSquad") == true);
			}
			lock (_captureLock)
			{
				if (card != null && !_capturedCards.Any((object c) => c == card))
				{
					_capturedCards.Add(card);
				}
				ReplaceOrAppendPlayer(flag ? _capturedMainPlayers : _capturedBenchPlayers, obj);
				_captureSource = (flag ? "hook:PlayerSquadCard.Init(args:main)" : "hook:PlayerSquadCard.Init(args:bench)");
				_captureRevision++;
			}
		}
		catch (Exception ex)
		{
			if (_config.LogLiveFailures.Value)
			{
				_log.LogWarning((object)("CapturePlayerSquadCardArgs failed: " + ex.GetType().Name + " " + ex.Message));
			}
		}
	}

	private static void ReplaceOrAppendPlayers(List<object> dest, IEnumerable<object> players)
	{
		foreach (object player in players)
		{
			ReplaceOrAppendPlayer(dest, player);
		}
	}

	private static void ReplaceOrAppendPlayer(List<object> dest, object player)
	{
		string key = PlayerKey(player);
		if (!string.IsNullOrWhiteSpace(key))
		{
			int num = dest.FindIndex((object p) => string.Equals(PlayerKey(p), key, StringComparison.OrdinalIgnoreCase));
			if (num >= 0)
			{
				dest[num] = player;
				return;
			}
		}
		if (!dest.Any((object p) => p == player))
		{
			dest.Add(player);
		}
	}

	private void SetCapturedTeamLocked(object? team)
	{
		if (team != null)
		{
			string text = TeamKey(_capturedTeam);
			string text2 = TeamKey(team);
			if (!string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(text2) && !string.Equals(text, text2, StringComparison.OrdinalIgnoreCase))
			{
				_capturedMainPlayers.Clear();
				_capturedBenchPlayers.Clear();
				_capturedCards.Clear();
				_capturedIsOurTeam = null;
			}
			_capturedTeam = team;
		}
	}

	private static string TeamKey(object? team)
	{
		if (team == null)
		{
			return string.Empty;
		}
		return NormalizeName(ReadString(team, "Name") ?? ReadString(team, "Nick") ?? string.Empty);
	}

	public RosterSnapshot GetRosterSnapshot()
	{
		if (!_probed && _config.ProbeGameTypes.Value)
		{
			_probed = true;
			ProbeCandidateTypes();
		}
		RosterSnapshot rosterSnapshot = TryReadLiveRoster();
		if (IsUsable(rosterSnapshot))
		{
			LastReadWasLive = true;
			LastStarterCount = rosterSnapshot.Starters.Count;
			LastBenchCount = rosterSnapshot.Bench.Count;
			Status = "live OK via " + LastSource;
			if (_config.LogLiveRosterOnce.Value)
			{
				string text = BuildLiveRosterLogKey(rosterSnapshot);
				if (!_loggedLiveOnce || !string.Equals(_lastLoggedLiveRosterKey, text, StringComparison.Ordinal))
				{
					_loggedLiveOnce = true;
					_lastLoggedLiveRosterKey = text;
					ManualLogSource log = _log;
					bool flag = default(bool);
					log.LogInfo($"Live roster read succeeded via {LastSource}: team={rosterSnapshot.TeamName}, starters={LastStarterCount}, bench={LastBenchCount}");
					foreach (PlayerSnapshot player in rosterSnapshot.Players)
					{
						ManualLogSource log2 = _log;
						log2.LogInfo($"  LIVE {(player.IsStarter ? "S" : "B")} {(player.IsCaptain ? "IGL" : "---")} {player.Nick} role={player.Role} skill={player.Skill:0.0} rifle={player.Rifle:0.0} awp={player.AWP:0.0} tactic={player.Tactics:0.0} lead={player.Leadership:0.0} form={player.Form:0}");
					}
				}
			}
			return rosterSnapshot;
		}
		LastReadWasLive = false;
		LastStarterCount = 0;
		LastBenchCount = 0;
		Status = "live not available";
		LastSource = "fallback";
		if (_config.FallbackToSampleOnLiveFailure.Value)
		{
			if (!_loggedFallbackOnce)
			{
				_loggedFallbackOnce = true;
				_log.LogWarning((object)"Live roster was not available yet; falling back to sample roster. Open the Squad screen or keep FallbackToSampleOnLiveFailure=true.");
			}
			return new SampleRosterProvider().GetRosterSnapshot();
		}
		return new RosterSnapshot
		{
			TeamName = "Live roster unavailable",
			Players = Array.Empty<PlayerSnapshot>()
		};
	}

	private static string BuildLiveRosterLogKey(RosterSnapshot roster)
	{
		string text = string.Join(",", roster.Starters.Select((PlayerSnapshot p) => p.Nick));
		string text2 = string.Join(",", roster.Bench.Select((PlayerSnapshot p) => p.Nick));
		return roster.TeamName + "|S:" + text + "|B:" + text2;
	}

	private RosterSnapshot? TryReadLiveRoster()
	{
		List<string> list = new List<string>();
		try
		{
			string reason;
			RosterSnapshot rosterSnapshot = TryReadFromCapturedState(out reason);
			if (IsUsable(rosterSnapshot))
			{
				LastSource = "captured:" + _captureSource;
				return rosterSnapshot;
			}
			list.Add("Captured: " + reason);
		}
		catch (Exception ex)
		{
			list.Add("Captured exception: " + ex.GetType().Name + " " + ex.Message);
		}
		if (_config.PreferSquadWindowTeam.Value)
		{
			try
			{
				string reason2;
				RosterSnapshot rosterSnapshot2 = TryReadFromSquadWindowTeam(out reason2);
				if (IsUsable(rosterSnapshot2))
				{
					LastSource = "SquadWindow._team";
					return rosterSnapshot2;
				}
				list.Add("SquadWindow: " + reason2);
			}
			catch (Exception ex2)
			{
				list.Add("SquadWindow exception: " + ex2.GetType().Name + " " + ex2.Message);
			}
		}
		if (_config.UseSquadCardsFallback.Value)
		{
			try
			{
				string reason3;
				RosterSnapshot rosterSnapshot3 = TryReadFromVisibleSquadCards(out reason3);
				if (IsUsable(rosterSnapshot3))
				{
					LastSource = "PlayerSquadCard UI";
					return rosterSnapshot3;
				}
				list.Add("SquadCards: " + reason3);
			}
			catch (Exception ex3)
			{
				list.Add("SquadCards exception: " + ex3.GetType().Name + " " + ex3.Message);
			}
		}
		string text = string.Join(" | ", list);
		if (_config.LogLiveFailures.Value)
		{
			string text2 = NormalizeFailureForLog(text);
			if (!_loggedFailureOnce || !string.Equals(_lastLoggedFailureKey, text2, StringComparison.Ordinal))
			{
				_loggedFailureOnce = true;
				_lastLoggedFailureKey = text2;
				if (IsNormalWarmupFailure(text2))
				{
					_log.LogInfo((object)"RosterIntel waiting for live SquadList.Init data; open the Squad screen or wait for UI cards to initialize.");
				}
				else
				{
					_log.LogWarning((object)("Live roster extraction did not pass validation: " + text));
				}
			}
		}
		return null;
	}

	private static string NormalizeFailureForLog(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return string.Empty;
		}
		string text = value;
		for (int i = 0; i < 100; i++)
		{
			string text2 = "rev=";
			int num = text.IndexOf(text2, StringComparison.OrdinalIgnoreCase);
			if (num < 0)
			{
				break;
			}
			int num2 = num + text2.Length;
			int j;
			for (j = num2; j < text.Length && char.IsDigit(text[j]); j++)
			{
			}
			if (j == num2)
			{
				break;
			}
			text = text.Substring(0, num2) + "*" + text.Substring(j);
		}
		return text;
	}

	private static bool IsNormalWarmupFailure(string normalizedFailure)
	{
		if (string.IsNullOrWhiteSpace(normalizedFailure))
		{
			return false;
		}
		bool flag = normalizedFailure.Contains("players=0", StringComparison.OrdinalIgnoreCase) || normalizedFailure.Contains("no PlayerSquadCard components found", StringComparison.OrdinalIgnoreCase);
		if (normalizedFailure.Contains("no captured live data yet", StringComparison.OrdinalIgnoreCase) && flag)
		{
			if (!normalizedFailure.Contains("no SquadWindow object found", StringComparison.OrdinalIgnoreCase))
			{
				return normalizedFailure.Contains("SquadWindow exists but _team is null", StringComparison.OrdinalIgnoreCase);
			}
			return true;
		}
		return false;
	}

	private bool IsUsable(RosterSnapshot? roster)
	{
		if (roster == null)
		{
			return false;
		}
		int num = Math.Max(1, _config.MinimumStarterCountForLive.Value);
		return roster.Starters.Count >= num;
	}

	private RosterSnapshot? TryReadFromCapturedState(out string reason)
	{
		object capturedTeam;
		List<object> list;
		List<object> list2;
		string captureSource;
		int captureRevision;
		bool? capturedIsOurTeam;
		lock (_captureLock)
		{
			capturedTeam = _capturedTeam;
			list = _capturedMainPlayers.ToList();
			list2 = _capturedBenchPlayers.ToList();
			captureSource = _captureSource;
			captureRevision = _captureRevision;
			capturedIsOurTeam = _capturedIsOurTeam;
		}
		if (capturedTeam != null)
		{
			RosterSnapshot rosterSnapshot = BuildRosterFromTeam(capturedTeam, out reason, capturedIsOurTeam, "SquadList.Init arg");
			if (IsUsable(rosterSnapshot))
			{
				reason = "captured team OK: " + reason;
				return rosterSnapshot;
			}
		}
		if (list.Count > 0)
		{
			if (!CapturedPlayersBelongToTeam(capturedTeam, list, list2))
			{
				reason = $"captured players rejected as stale for team={TryReadCapturedTeamName(capturedTeam) ?? "unknown"}, source={captureSource}, rev={captureRevision}";
				return null;
			}
			List<PlayerSnapshot> list3 = new List<PlayerSnapshot>();
			foreach (object item in list.Take(5))
			{
				list3.Add(ToSnapshot(item, isStarter: true));
			}
			HashSet<string> hashSet = new HashSet<string>(from x in list.Select(PlayerKey)
				where !string.IsNullOrWhiteSpace(x)
				select x, StringComparer.OrdinalIgnoreCase);
			foreach (object item2 in list2)
			{
				string text = PlayerKey(item2);
				if (string.IsNullOrWhiteSpace(text) || !hashSet.Contains(text))
				{
					PlayerSnapshot snap = ToSnapshot(item2, isStarter: false);
					if (!list3.Any((PlayerSnapshot x) => string.Equals(x.Nick, snap.Nick, StringComparison.OrdinalIgnoreCase)))
					{
						list3.Add(snap);
					}
				}
			}
			reason = $"captured players source={captureSource}, rev={captureRevision}, starters={list3.Count((PlayerSnapshot x) => x.IsStarter)}, bench={list3.Count((PlayerSnapshot x) => !x.IsStarter)}";
			return new RosterSnapshot
			{
				TeamName = (TryReadCapturedTeamName(capturedTeam) ?? "Current Team"),
				Players = list3,
				IsLikelyHumanManagedTeam = capturedIsOurTeam,
				ManagedTeamSignal = (capturedIsOurTeam.HasValue ? "SquadList.Init arg" : "captured/no-own-team-flag")
			};
		}
		reason = $"no captured live data yet (source={captureSource}, rev={captureRevision})";
		return null;
	}

	private RosterSnapshot? BuildRosterFromTeam(object team, out string reason, bool? isOurTeam = null, string managedTeamSignal = "team-object")
	{
		string text = ReadString(team, "Name") ?? ReadString(team, "Nick") ?? "Current Team";
		object? value = InvokeNoArgs(team, "GetMainSquadPlayers");
		object value2 = InvokeNoArgs(team, "GetAllPlayers");
		List<object> list = ToObjectList(value);
		List<object> list2 = ToObjectList(value2);
		if (list.Count == 0)
		{
			list = ResolvePlayersFromIdList(team, "MainSquad");
		}
		if (list2.Count == 0)
		{
			list2 = ResolvePlayersFromIdList(team, "FullSquad");
		}
		if (list.Count == 0)
		{
			reason = "team=" + text + ": no main squad players returned";
			return null;
		}
		HashSet<string> hashSet = new HashSet<string>(from s in list.Select(PlayerKey)
			where !string.IsNullOrWhiteSpace(s)
			select s, StringComparer.OrdinalIgnoreCase);
		List<PlayerSnapshot> list3 = new List<PlayerSnapshot>();
		foreach (object item in list.Take(5))
		{
			list3.Add(ToSnapshot(item, isStarter: true));
		}
		foreach (object item2 in list2)
		{
			string text2 = PlayerKey(item2);
			if (string.IsNullOrWhiteSpace(text2) || !hashSet.Contains(text2))
			{
				PlayerSnapshot snap = ToSnapshot(item2, isStarter: false);
				if (!list3.Any((PlayerSnapshot x) => string.Equals(x.Nick, snap.Nick, StringComparison.OrdinalIgnoreCase)))
				{
					list3.Add(snap);
				}
			}
		}
		reason = $"team={text}: starters={list3.Count((PlayerSnapshot x) => x.IsStarter)}, all={list3.Count}";
		return new RosterSnapshot
		{
			TeamName = text,
			Players = list3,
			IsLikelyHumanManagedTeam = isOurTeam,
			ManagedTeamSignal = (isOurTeam.HasValue ? managedTeamSignal : "team-object/no-own-team-flag")
		};
	}

	private string? TryReadCapturedTeamName(object? team)
	{
		if (team == null)
		{
			return null;
		}
		return ReadString(team, "Name") ?? ReadString(team, "Nick");
	}

	private bool CapturedPlayersBelongToTeam(object? team, IReadOnlyList<object> main, IReadOnlyList<object> bench)
	{
		if (team == null)
		{
			return true;
		}
		List<object> list = ToObjectList(InvokeNoArgs(team, "GetAllPlayers"));
		if (list.Count == 0)
		{
			list = ResolvePlayersFromIdList(team, "FullSquad");
		}
		if (list.Count == 0)
		{
			return true;
		}
		HashSet<string> hashSet = new HashSet<string>(from x in list.Select(PlayerKey)
			where !string.IsNullOrWhiteSpace(x)
			select x, StringComparer.OrdinalIgnoreCase);
		if (hashSet.Count == 0)
		{
			return true;
		}
		List<string> list2 = (from x in main.Concat(bench).Select(PlayerKey)
			where !string.IsNullOrWhiteSpace(x)
			select x).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToList();
		if (list2.Count == 0)
		{
			return true;
		}
		int num = list2.Count(hashSet.Contains);
		int num2 = Math.Min(3, Math.Max(1, list2.Count / 2));
		return num >= num2;
	}

	private RosterSnapshot? TryReadFromSquadWindowTeam(out string reason)
	{
		reason = "no SquadWindow object found";
		foreach (object item in FindUnityObjectsByTypeName("SquadWindow"))
		{
			CaptureSquadWindow(item);
			object obj = ReadMember(item, "_team") ?? ReadMember(item, "team") ?? ReadMember(item, "Team");
			if (obj == null)
			{
				string text = ReadString(item, "_teamId") ?? ReadString(item, "teamId");
				reason = "SquadWindow exists but _team is null" + ((text == null) ? "" : (" (teamId=" + text + ")"));
				continue;
			}
			RosterSnapshot rosterSnapshot = BuildRosterFromTeam(obj, out reason);
			if (rosterSnapshot == null)
			{
				continue;
			}
			return rosterSnapshot;
		}
		return null;
	}

	private RosterSnapshot? TryReadFromVisibleSquadCards(out string reason)
	{
		reason = "no PlayerSquadCard components found";
		List<object> list = FindUnityObjectsByTypeName("PlayerSquadCard").ToList();
		if (list.Count == 0)
		{
			return null;
		}
		List<PlayerSnapshot> list2 = new List<PlayerSnapshot>();
		foreach (object item in list)
		{
			object obj = ReadMember(item, "_player") ?? ReadMember(item, "player") ?? ReadMember(item, "Player");
			if (obj != null)
			{
				bool isStarter = ReadBool(item, "IsMainSquad") ?? (ReadBool(item, "_IsMainSquad") == true);
				PlayerSnapshot snap = ToSnapshot(obj, isStarter);
				if (!string.IsNullOrWhiteSpace(snap.Nick) && !(snap.Nick == "unknown") && !list2.Any((PlayerSnapshot x) => string.Equals(x.Nick, snap.Nick, StringComparison.OrdinalIgnoreCase)))
				{
					list2.Add(snap);
				}
			}
		}
		string teamName = TryReadCapturedTeamName(_capturedTeam) ?? "Current Team";
		reason = $"cards={list.Count}, players={list2.Count}, starters={list2.Count((PlayerSnapshot x) => x.IsStarter)}";
		if (list2.Count != 0)
		{
			return new RosterSnapshot
			{
				TeamName = teamName,
				Players = list2,
				ManagedTeamSignal = "visible-cards/no-own-team-flag"
			};
		}
		return null;
	}

	private PlayerSnapshot ToSnapshot(object player, bool isStarter)
	{
		string nick = ReadString(player, "Nick") ?? ReadString(player, "nick") ?? ReadString(player, "_nick") ?? ReadString(player, "Name") ?? "unknown";
		PlayerRole role = MapRole(ReadString(player, "MainRole") ?? ReadString(player, "RawRole") ?? ReadString(player, "role") ?? ReadString(player, "roles") ?? string.Empty);
		bool valueOrDefault = ReadBool(player, "IsIGL") == true;
		if (valueOrDefault)
		{
			role = PlayerRole.IGL;
		}
		PlayerSnapshot p = new PlayerSnapshot
		{
			Nick = nick,
			Role = role,
			IsStarter = isStarter,
			IsCaptain = valueOrDefault,
			Skill = ReadAttribute(player, "Skill", "skill", "Overall", "Ability", "Rating"),
			AWP = ReadAttribute(player, "AWP", "Awp", "awp", "Sniper"),
			Rifle = ReadAttribute(player, "Rifle", "rifle", "Rifling", "Gun", "Shooting"),
			Pistol = ReadAttribute(player, "Pistol", "pistol"),
			Grenades = ReadAttribute(player, "Grenades", "Grenade", "grenades", "Utility", "Nades"),
			Creativity = ReadAttribute(player, "Creativity", "creativity", "Creative"),
			Clutch = ReadAttribute(player, "Clutch", "clutch"),
			Tactics = ReadAttribute(player, "Tactics", "Tactic", "tactic", "Tactical", "Strategy"),
			Leadership = ReadAttribute(player, "Leadership", "Leader", "leader", "Captain"),
			Teamwork = ReadAttribute(player, "Teamwork", "teamwork", "Team"),
			Morale = ReadAttribute(player, "Morale", "morale"),
			Stress = ReadAttribute(player, "StressResistance", "Stress", "StressResist", "StressResilience", "Stability"),
			Loyalty = ReadAttribute(player, "Loyalty", "loyalty"),
			Productivity = ReadAttribute(player, "Productivity", "productivity"),
			Reaction = ReadAttribute(player, "Reaction", "reaction", "Reflex"),
			Perception = ReadAttribute(player, "Perception", "perception", "Vision"),
			Immunity = ReadAttribute(player, "Immunity", "immunity"),
			Strength = ReadAttribute(player, "Strength", "strength", "Power"),
			Stamina = ReadAttribute(player, "Endurance", "Stamina", "stamina", "Durability", "endurance"),
			Form = NormalizeGauge(ReadNumeric(player, "Form") ?? ReadNumeric(player, "form") ?? 90.0),
			Health = NormalizeGauge(ReadNumeric(player, "Health") ?? ReadNumeric(player, "health") ?? 90.0),
			SalaryMonthly = (ReadNumeric(player, "Salary") ?? ReadNumeric(player, "salary") ?? ReadContractSalary(player).GetValueOrDefault()),
			GameRating = NormalizeOptionalAttr(ReadNumeric(player, "Rating") ?? ReadNumeric(player, "rating") ?? ReadNumeric(player, "Power") ?? ReadNumeric(player, "power")),
			ImpactRating = (ReadNumeric(player, "ImpactRating") ?? ReadNumeric(player, "IR") ?? ReadNumeric(player, "ir") ?? ReadNumeric(player, "Impact")),
			KdRatio = (ReadNumeric(player, "KD") ?? ReadNumeric(player, "KDRatio") ?? ReadNumeric(player, "KdRatio") ?? ReadNumeric(player, "KDR")),
			Adr = (ReadNumeric(player, "ADR") ?? ReadNumeric(player, "Adr") ?? ReadNumeric(player, "AverageDamage") ?? ReadNumeric(player, "DamagePerRound")),
			KastPercent = (ReadNumeric(player, "KAST") ?? ReadNumeric(player, "Kast") ?? ReadNumeric(player, "KastPercent")),
			Top1Rating = (ReadNumeric(player, "Top1Rating") ?? ReadNumeric(player, "VsTop1Rating") ?? ReadNumeric(player, "RatingVsTop1")),
			Top5Rating = (ReadNumeric(player, "Top5Rating") ?? ReadNumeric(player, "VsTop5Rating") ?? ReadNumeric(player, "RatingVsTop5")),
			Top10Rating = (ReadNumeric(player, "Top10Rating") ?? ReadNumeric(player, "VsTop10Rating") ?? ReadNumeric(player, "RatingVsTop10")),
			Top20Rating = (ReadNumeric(player, "Top20Rating") ?? ReadNumeric(player, "VsTop20Rating") ?? ReadNumeric(player, "RatingVsTop20")),
			Top50Rating = (ReadNumeric(player, "Top50Rating") ?? ReadNumeric(player, "VsTop50Rating") ?? ReadNumeric(player, "RatingVsTop50"))
		};
		PlayerSnapshot p2 = _directStatsCache.Enrich(p);
		return _tierStatsLogReader.Enrich(p2);
	}

	private double ReadAttribute(object player, params string[] aliases)
	{
		string[] array = aliases;
		foreach (string name in array)
		{
			double? num = ReadNumeric(player, name);
			if (num.HasValue)
			{
				return NormalizeAttr(num.Value);
			}
		}
		object obj = ReadMember(player, "Attributes") ?? ReadMember(player, "attributes");
		if (obj != null)
		{
			array = aliases;
			foreach (string name2 in array)
			{
				double? num2 = ReadNumeric(obj, name2);
				if (num2.HasValue)
				{
					return NormalizeAttr(num2.Value);
				}
			}
		}
		double? num3 = TryGetEffectiveAttribute(player, aliases);
		if (num3.HasValue)
		{
			return NormalizeAttr(num3.Value);
		}
		return 10.0;
	}

	private double? TryGetEffectiveAttribute(object player, string[] aliases)
	{
		MethodInfo method = player.GetType().GetMethod("GetEffectiveAttribute", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (method == null)
		{
			return null;
		}
		ParameterInfo[] parameters = method.GetParameters();
		if (parameters.Length != 1 || !parameters[0].ParameterType.IsEnum)
		{
			return null;
		}
		object obj = ResolveEnumAlias(parameters[0].ParameterType, aliases);
		if (obj == null)
		{
			return null;
		}
		try
		{
			return ToDouble(method.Invoke(player, new object[1] { obj }));
		}
		catch
		{
			return null;
		}
	}

	private object? ResolveEnumAlias(Type enumType, IEnumerable<string> aliases)
	{
		if (_attributeTypeCache == null)
		{
			_attributeTypeCache = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
		}
		foreach (string alias in aliases)
		{
			string key = enumType.FullName + ":" + NormalizeName(alias);
			if (_attributeTypeCache.TryGetValue(key, out object value))
			{
				return value;
			}
		}
		string[] names = Enum.GetNames(enumType);
		foreach (string alias2 in aliases)
		{
			string text = NormalizeName(alias2);
			string[] array = names;
			foreach (string value2 in array)
			{
				string text2 = NormalizeName(value2);
				if (text2 == text || text2.Contains(text) || text.Contains(text2))
				{
					object obj = Enum.Parse(enumType, value2);
					_attributeTypeCache[enumType.FullName + ":" + text] = obj;
					return obj;
				}
			}
		}
		return null;
	}

	private List<object> ResolvePlayersFromIdList(object team, string listMemberName)
	{
		List<object> list = ToObjectList(ReadMember(team, listMemberName));
		if (list.Count == 0)
		{
			return new List<object>();
		}
		object obj = FindDataContainer("DataPlayers");
		if (obj == null)
		{
			return new List<object>();
		}
		List<object> list2 = new List<object>();
		foreach (object item in list)
		{
			string text = Convert.ToString(item, CultureInfo.InvariantCulture);
			if (!string.IsNullOrWhiteSpace(text))
			{
				object obj2 = Invoke(obj, "GetInternal", text, false) ?? Invoke(obj, "Get", text, false) ?? Invoke(obj, "Get", text);
				if (obj2 != null)
				{
					list2.Add(obj2);
				}
			}
		}
		return list2;
	}

	private object? FindDataContainer(string typeName)
	{
		return null;
	}

	private IEnumerable<object> FindUnityObjectsByTypeName(string typeName)
	{
		HashSet<int> seen = new HashSet<int>();
		Type type = FindAllowedType(typeName);
		if (type != null)
		{
			foreach (object item3 in FindUnityObjectsBySystemType(type))
			{
				if (item3 != null)
				{
					int item = SafeIdentityHash(item3);
					if (seen.Add(item))
					{
						yield return item3;
					}
				}
			}
		}
		IEnumerable enumerable;
		try
		{
			enumerable = (IEnumerable)UnityEngine.Object.FindObjectsOfType<MonoBehaviour>();
		}
		catch
		{
			yield break;
		}
		foreach (object item4 in enumerable)
		{
			if (item4 != null && TypeNameMatches(item4, typeName))
			{
				int item2 = SafeIdentityHash(item4);
				if (seen.Add(item2))
				{
					yield return item4;
				}
			}
		}
	}

	private IEnumerable<object> FindUnityObjectsBySystemType(Type type)
	{
		foreach (object arg in BuildUnityTypeArguments(type))
		{
			foreach (object item in InvokeUnityObjectFind("FindObjectsOfType", arg))
			{
				yield return item;
			}
			foreach (object item2 in InvokeResourcesFindAll(arg))
			{
				yield return item2;
			}
		}
	}

	private IEnumerable<object> InvokeUnityObjectFind(string methodName, object typeArg)
	{
		List<MethodInfo> list = (from m in typeof(UnityEngine.Object).GetMethods(BindingFlags.Static | BindingFlags.Public)
			where m.Name == methodName && m.GetParameters().Length >= 1
			select m).ToList();
		foreach (MethodInfo item in list)
		{
			object value = null;
			try
			{
				object[] parameters = ((item.GetParameters().Length != 1) ? new object[2] { typeArg, true } : new object[1] { typeArg });
				value = item.Invoke(null, parameters);
			}
			catch
			{
			}
			foreach (object item2 in ToObjectList(value))
			{
				yield return item2;
			}
		}
	}

	private IEnumerable<object> InvokeResourcesFindAll(object typeArg)
	{
		List<MethodInfo> list = (from m in typeof(Resources).GetMethods(BindingFlags.Static | BindingFlags.Public)
			where m.Name == "FindObjectsOfTypeAll" && m.GetParameters().Length == 1
			select m).ToList();
		foreach (MethodInfo item in list)
		{
			object value = null;
			try
			{
				value = item.Invoke(null, new object[1] { typeArg });
			}
			catch
			{
			}
			foreach (object item2 in ToObjectList(value))
			{
				yield return item2;
			}
		}
	}

	private IEnumerable<object> BuildUnityTypeArguments(Type type)
	{
		yield return type;
		object obj = null;
		try
		{
			obj = (Type.GetType("Il2CppInterop.Runtime.Il2CppType, Il2CppInterop.Runtime")?.GetMethods(BindingFlags.Static | BindingFlags.Public).FirstOrDefault(delegate(MethodInfo m)
			{
				string name = m.Name;
				bool flag = ((name == "From" || name == "Of") ? true : false);
				return flag && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(Type);
			}))?.Invoke(null, new object[1] { type });
		}
		catch
		{
		}
		if (obj != null)
		{
			yield return obj;
		}
	}

	private Type? FindAllowedType(string typeName)
	{
		foreach (Type allowedType in GetAllowedTypes())
		{
			if (string.Equals(allowedType.Name, typeName, StringComparison.OrdinalIgnoreCase) || string.Equals(allowedType.FullName, typeName, StringComparison.OrdinalIgnoreCase))
			{
				return allowedType;
			}
		}
		return null;
	}

	private static bool TypeNameMatches(object obj, string wanted)
	{
		foreach (string item in RuntimeTypeNames(obj))
		{
			if (string.Equals(item, wanted, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
			if (item.EndsWith("." + wanted, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private static IEnumerable<string> RuntimeTypeNames(object obj)
	{
		Type t = null;
		try
		{
			t = obj.GetType();
		}
		catch
		{
		}
		if (t != null)
		{
			if (!string.IsNullOrWhiteSpace(t.Name))
			{
				yield return t.Name;
			}
			if (!string.IsNullOrWhiteSpace(t.FullName))
			{
				yield return t.FullName;
			}
		}
		object il2cppType = null;
		try
		{
			il2cppType = obj.GetType().GetMethod("GetIl2CppType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(obj, Array.Empty<object>());
		}
		catch
		{
		}
		if (il2cppType != null)
		{
			string text = ReadString(il2cppType, "Name");
			string fullName = ReadString(il2cppType, "FullName");
			if (!string.IsNullOrWhiteSpace(text))
			{
				yield return text;
			}
			if (!string.IsNullOrWhiteSpace(fullName))
			{
				yield return fullName;
			}
			string text2 = Convert.ToString(il2cppType, CultureInfo.InvariantCulture);
			if (!string.IsNullOrWhiteSpace(text2))
			{
				yield return text2;
			}
		}
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

	private static object? InvokeNoArgs(object target, string methodName)
	{
		try
		{
			return target.GetType().GetMethod(methodName, BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null)?.Invoke(target, Array.Empty<object>());
		}
		catch
		{
			return null;
		}
	}

	private static object? Invoke(object target, string methodName, params object[] args)
	{
		try
		{
			foreach (MethodInfo item in from m in target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
				where string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase) && m.GetParameters().Length == args.Length
				select m)
			{
				try
				{
					return item.Invoke(target, args);
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
		return null;
	}

	private static object? ReadMember(object obj, string name)
	{
		if (obj == null)
		{
			return null;
		}
		BindingFlags bindingAttr = BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
		Type type = obj.GetType();
		try
		{
			PropertyInfo property = type.GetProperty(name, bindingAttr);
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
			FieldInfo field = type.GetField(name, bindingAttr);
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

	private static bool? ReadBool(object obj, string name)
	{
		object obj2 = ReadMember(obj, name);
		if (obj2 is bool)
		{
			return (bool)obj2;
		}
		if (obj2 == null)
		{
			return null;
		}
		if (bool.TryParse(Convert.ToString(obj2, CultureInfo.InvariantCulture), out var result))
		{
			return result;
		}
		return null;
	}

	private static double? ReadNumeric(object obj, string name)
	{
		return ToDouble(ReadMember(obj, name));
	}

	private static double? ToDouble(object? value)
	{
		if (value == null)
		{
			return null;
		}
		try
		{
			double result;
			return (value is byte b) ? new double?((int)b) : ((value is sbyte b2) ? new double?(b2) : ((value is short num) ? new double?(num) : ((value is ushort num2) ? new double?((int)num2) : ((value is int num3) ? new double?(num3) : ((value is uint num4) ? new double?(num4) : ((value is long num5) ? new double?(num5) : ((value is ulong num6) ? new double?(num6) : ((value is float num7) ? new double?(num7) : ((value is double value2) ? new double?(value2) : ((!(value is decimal num8)) ? (double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out result) ? new double?(result) : ((double?)null)) : new double?((double)num8)))))))))));
		}
		catch
		{
			return null;
		}
	}

	private static double NormalizeAttr(double value)
	{
		if (value <= 0.0)
		{
			return 0.0;
		}
		if (value > 25.0 && value <= 100.0)
		{
			return Math.Clamp(value / 5.0, 0.0, 20.0);
		}
		return Math.Clamp(value, 0.0, 20.0);
	}

	private static double? NormalizeOptionalAttr(double? value)
	{
		if (!value.HasValue)
		{
			return null;
		}
		return NormalizeAttr(value.Value);
	}

	private static double NormalizeGauge(double value)
	{
		if (value <= 20.0)
		{
			return Math.Clamp(value * 5.0, 0.0, 100.0);
		}
		return Math.Clamp(value, 0.0, 100.0);
	}

	private static double? ReadContractSalary(object player)
	{
		object obj = ReadMember(player, "Contract") ?? ReadMember(player, "contract");
		if (obj == null)
		{
			return null;
		}
		return ReadNumeric(obj, "Salary") ?? ReadNumeric(obj, "salary") ?? ReadNumeric(obj, "Wage") ?? ReadNumeric(obj, "MonthlySalary");
	}

	private static List<object> ToObjectList(object? value)
	{
		List<object> list = new List<object>();
		if (value == null)
		{
			return list;
		}
		if (value is IEnumerable enumerable)
		{
			{
				foreach (object item in enumerable)
				{
					if (item != null)
					{
						list.Add(item);
					}
				}
				return list;
			}
		}
		double? num = ToDouble(ReadMember(value, "Count") ?? ReadMember(value, "Length"));
		if (!num.HasValue)
		{
			return list;
		}
		MethodInfo method = value.GetType().GetMethod("get_Item", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (method == null)
		{
			return list;
		}
		for (int i = 0; i < (int)num.Value; i++)
		{
			try
			{
				object obj = method.Invoke(value, new object[1] { i });
				if (obj != null)
				{
					list.Add(obj);
				}
			}
			catch
			{
			}
		}
		return list;
	}

	private static string PlayerKey(object player)
	{
		return ReadString(player, "Id") ?? ReadString(player, "id") ?? ReadString(player, "Nick") ?? ReadString(player, "nick") ?? string.Empty;
	}

	private static PlayerRole MapRole(string? roleText)
	{
		if (string.IsNullOrWhiteSpace(roleText))
		{
			return PlayerRole.Rifler;
		}
		string text = NormalizeName(roleText);
		if (text.Contains("awp") || text.Contains("sniper"))
		{
			return PlayerRole.AWPer;
		}
		if (text.Contains("igl") || text.Contains("leader") || text.Contains("captain"))
		{
			return PlayerRole.IGL;
		}
		if (text.Contains("support"))
		{
			return PlayerRole.Support;
		}
		if (text.Contains("lurk"))
		{
			return PlayerRole.Lurker;
		}
		return PlayerRole.Rifler;
	}

	private static string NormalizeName(string value)
	{
		return new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
	}

	private void ProbeCandidateTypes()
	{
		List<Type> source = GetAllowedTypes().ToList();
		List<Type> list = source.Where(delegate(Type t)
		{
			string text = t.FullName ?? t.Name;
			return text.Contains("Player", StringComparison.OrdinalIgnoreCase) || text.Contains("Roster", StringComparison.OrdinalIgnoreCase) || text.Contains("Team", StringComparison.OrdinalIgnoreCase) || text.Contains("Lineup", StringComparison.OrdinalIgnoreCase) || text.Contains("Squad", StringComparison.OrdinalIgnoreCase) || text.Equals("AttributeType", StringComparison.OrdinalIgnoreCase) || text.EndsWith(".Attributes", StringComparison.OrdinalIgnoreCase);
		}).Take(_config.ProbeTypeLimit.Value).ToList();
		ManualLogSource log = _log;
		bool flag = default(bool);
		log.LogInfo($"Candidate game/data types: {list.Count}");
		foreach (Type item in list)
		{
			ManualLogSource log2 = _log;
			log2.LogInfo($"  candidate: {FriendlyType(item)}");
		}
		if (!_config.ProbeMemberDetails.Value && !_config.ProbeStaticValues.Value)
		{
			return;
		}
		_log.LogInfo((object)"Deep probe enabled. Logging members for key roster/player/team/UI types.");
		foreach (Type item2 in (from t in source
			where KeyTypeNames.Any((string k) => string.Equals(t.FullName, k, StringComparison.OrdinalIgnoreCase) || string.Equals(t.Name, k, StringComparison.OrdinalIgnoreCase))
			orderby t.FullName
			select t).ToList())
		{
			ProbeTypeMembers(item2);
		}
	}

	private IEnumerable<Type> GetAllowedTypes()
	{
		return (from a in AppDomain.CurrentDomain.GetAssemblies()
			where Enumerable.Contains<string>(AllowedAssemblyNames, a.GetName().Name ?? string.Empty)
			select a).SelectMany(SafeReflection.GetLoadableTypes);
	}

	private void ProbeTypeMembers(Type type)
	{
		bool flag = default(bool);
		try
		{
			ManualLogSource log = _log;
			log.LogInfo($"[TYPE] {FriendlyType(type)} base={FriendlyType(type.BaseType)}");
			if (type.IsEnum)
			{
				_log.LogInfo((object)("  enum values: " + string.Join(", ", Enum.GetNames(type))));
			}
			if (_config.ProbeMemberDetails.Value)
			{
				BindingFlags bindingAttr = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
				int count = Math.Max(10, _config.ProbeMemberLimit.Value);
				List<FieldInfo> list = (from f in type.GetFields(bindingAttr)
					where !f.Name.Contains("BackingField", StringComparison.OrdinalIgnoreCase)
					orderby (!f.IsStatic) ? 1 : 0, f.Name
					select f).Take(count).ToList();
				ManualLogSource log2 = _log;
				log2.LogInfo($"  fields({list.Count} shown):");
				foreach (FieldInfo item in list)
				{
					ManualLogSource log3 = _log;
					log3.LogInfo($"    {(item.IsStatic ? "static " : "")}{FriendlyType(item.FieldType)} {item.Name}");
				}
				List<PropertyInfo> list2 = (from p in type.GetProperties(bindingAttr)
					orderby (!(p.GetMethod?.IsStatic ?? p.SetMethod?.IsStatic ?? false)) ? 1 : 0, p.Name
					select p).Take(count).ToList();
				ManualLogSource log4 = _log;
				log4.LogInfo($"  properties({list2.Count} shown):");
				foreach (PropertyInfo item2 in list2)
				{
					bool flag2 = item2.GetMethod?.IsStatic ?? item2.SetMethod?.IsStatic ?? false;
					ManualLogSource log5 = _log;
					log5.LogInfo($"    {(flag2 ? "static " : "")}{FriendlyType(item2.PropertyType)} {item2.Name}");
				}
				List<MethodInfo> list3 = (from m in type.GetMethods(bindingAttr)
					where !m.IsSpecialName
					orderby (!m.IsStatic) ? 1 : 0, m.Name
					select m).Take(count).ToList();
				ManualLogSource log6 = _log;
				log6.LogInfo($"  methods({list3.Count} shown):");
				foreach (MethodInfo item3 in list3)
				{
					string text = string.Join(", ", from p in item3.GetParameters()
						select FriendlyType(p.ParameterType) + " " + p.Name);
					ManualLogSource log7 = _log;
					log7.LogInfo($"    {(item3.IsStatic ? "static " : "")}{FriendlyType(item3.ReturnType)} {item3.Name}({text})");
				}
			}
			if (_config.ProbeStaticValues.Value)
			{
				ProbeStaticValues(type);
			}
		}
		catch (Exception ex)
		{
			ManualLogSource log8 = _log;
			log8.LogWarning($"Failed to probe type {FriendlyType(type)}");
			_log.LogWarning((object)ex.ToString());
		}
	}

	private void ProbeStaticValues(Type type)
	{
		BindingFlags bindingAttr = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
		bool flag = default(bool);
		foreach (FieldInfo item in type.GetFields(bindingAttr).Take(_config.ProbeMemberLimit.Value))
		{
			try
			{
				object value = item.GetValue(null);
				LogValueSummary("static field " + type.Name + "." + item.Name, value);
			}
			catch (Exception ex)
			{
				ManualLogSource log = _log;
				log.LogInfo($"    static field {type.Name}.{item.Name}: <unreadable {ex.GetType().Name}>");
			}
		}
		foreach (PropertyInfo item2 in (from p in type.GetProperties(bindingAttr)
			where p.GetIndexParameters().Length == 0
			select p).Take(_config.ProbeMemberLimit.Value))
		{
			try
			{
				MethodInfo getMethod = item2.GetGetMethod(nonPublic: true);
				if (!(getMethod == null) && getMethod.IsStatic)
				{
					object value2 = item2.GetValue(null, null);
					LogValueSummary("static property " + type.Name + "." + item2.Name, value2);
				}
			}
			catch (Exception ex2)
			{
				ManualLogSource log2 = _log;
				log2.LogInfo($"    static property {type.Name}.{item2.Name}: <unreadable {ex2.GetType().Name}>");
			}
		}
	}

	private void LogValueSummary(string label, object? value)
	{
		bool flag = default(bool);
		if (value == null)
		{
			ManualLogSource log = _log;
			log.LogInfo($"    {label}: null");
			return;
		}
		if (value is string value2)
		{
			ManualLogSource log2 = _log;
			log2.LogInfo($"    {label}: string '{Truncate(value2, 120)}'");
			return;
		}
		if (value is IEnumerable enumerable)
		{
			int? num = TryGetCount(value);
			ManualLogSource log3 = _log;
			log3.LogInfo($"    {label}: enumerable type={FriendlyType(value.GetType())} count={num?.ToString() ?? "?"}");
			int num2 = 0;
			{
				foreach (object item in enumerable)
				{
					if (num2 >= 5)
					{
						break;
					}
					ManualLogSource log4 = _log;
					log4.LogInfo($"      item[{num2}] type={FriendlyType(item?.GetType())} summary={SummarizeObject(item)}");
					num2++;
				}
				return;
			}
		}
		ManualLogSource log5 = _log;
		log5.LogInfo($"    {label}: type={FriendlyType(value.GetType())} value={Truncate(Convert.ToString(value) ?? "", 160)}");
	}

	private static int? TryGetCount(object value)
	{
		PropertyInfo propertyInfo = value.GetType().GetProperty("Count") ?? value.GetType().GetProperty("Length");
		try
		{
			object obj = propertyInfo?.GetValue(value, null);
			if (obj is int)
			{
				return (int)obj;
			}
		}
		catch
		{
		}
		return null;
	}

	private static string SummarizeObject(object? item)
	{
		if (item == null)
		{
			return "null";
		}
		string[] obj = new string[13]
		{
			"Nick", "nick", "Name", "name", "Surname", "surname", "TeamId", "team", "MainRole", "Rating",
			"rating", "Skill", "skill"
		};
		List<string> list = new List<string>();
		string[] array = obj;
		foreach (string text in array)
		{
			object obj2 = ReadMember(item, text);
			if (obj2 != null)
			{
				list.Add(text + "=" + Truncate(Convert.ToString(obj2) ?? "", 60));
			}
		}
		if (list.Count != 0)
		{
			return string.Join("; ", list);
		}
		return "<no simple summary>";
	}

	private static bool IsSimple(Type t)
	{
		if (!t.IsPrimitive && !t.IsEnum && !(t == typeof(string)) && !(t == typeof(decimal)))
		{
			return t == typeof(DateTime);
		}
		return true;
	}

	private static string FriendlyType(Type? t)
	{
		if (t == null)
		{
			return "null";
		}
		if (!t.IsGenericType)
		{
			return t.FullName ?? t.Name;
		}
		string text = t.FullName ?? t.Name;
		int num = text.IndexOf('`');
		if (num >= 0)
		{
			text = text.Substring(0, num);
		}
		return text + "<" + string.Join(",", t.GetGenericArguments().Select(FriendlyType)) + ">";
	}

	private static string Truncate(string value, int max)
	{
		if (string.IsNullOrEmpty(value) || value.Length <= max)
		{
			return value;
		}
		return value.Substring(0, max) + "…";
	}
}
