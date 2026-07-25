using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using EM.RosterIntel.Data;
using HarmonyLib;

namespace EM.RosterIntel.Patches;

public static class RosterIntelPatches
{
	private static ManualLogSource? _log;

	private static ReflectionRosterDataProvider? _provider;

	private static readonly object LogLock = new object();

	private static int _squadWindowHits;

	private static int _squadListHits;

	private static int _cardHits;

	private static int _statsViewHits;

	private static int _overallStatsHits;

	private static int _playerStatsRowHits;

	private static int _playersStatsViewHits;

	private static bool _verboseHookLogging;

	public static void Configure(ManualLogSource log, ReflectionRosterDataProvider provider, bool verboseHookLogging = false)
	{
		_log = log;
		_provider = provider;
		_verboseHookLogging = verboseHookLogging;
		_squadWindowHits = 0;
		_squadListHits = 0;
		_cardHits = 0;
		_statsViewHits = 0;
		_overallStatsHits = 0;
		_playerStatsRowHits = 0;
		_playersStatsViewHits = 0;
	}

	public static void Install(Harmony harmony)
	{
		PatchMethodsByName(harmony, "SquadWindow", "Renew", "SquadWindowPostfix");
		PatchMethodsByName(harmony, "SquadList", "Init", "SquadListPostfix");
		PatchMethodsByName(harmony, "PlayerSquadCard", "Init", "PlayerSquadCardPostfix");
		PatchMethodsByName(harmony, "PlayerStatsView", "Setup", "PlayerStatsViewPostfix");
		PatchMethodsByName(harmony, "OverallStatsView", "Setup", "OverallStatsViewPostfix");
		PatchMethodsByName(harmony, "PlayersStatsView", "FillPlayerStatContexts", "PlayersStatsViewPostfix");
		PatchMethodsByName(harmony, "PlayerStatsRow", "Initialize", "PlayerStatsRowPostfix");
	}

	private static void PatchMethodsByName(Harmony harmony, string typeName, string methodName, string postfixName)
	{
		bool flag = default(bool);
		try
		{
			Type type = FindGameType(typeName);
			ManualLogSource log;
			if (type == null)
			{
				log = _log;
				if (log != null)
				{
					log.LogWarning($"RosterIntel hook skipped: type {typeName} not found.");
				}
				return;
			}
			MethodInfo method = typeof(RosterIntelPatches).GetMethod(postfixName, BindingFlags.Static | BindingFlags.Public);
			if (method == null)
			{
				log = _log;
				if (log != null)
				{
					log.LogWarning($"RosterIntel hook skipped: postfix {postfixName} not found.");
				}
				return;
			}
			List<MethodInfo> list = (from m in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
				where string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase)
				where !m.ContainsGenericParameters
				select m).ToList();
			if (list.Count == 0)
			{
				log = _log;
				if (log != null)
				{
					log.LogWarning($"RosterIntel hook skipped: {typeName}.{methodName} has no non-generic instance methods.");
				}
				return;
			}
			int num = 0;
			foreach (MethodInfo item in list)
			{
				try
				{
					harmony.Patch((MethodBase)item, (HarmonyMethod)null, new HarmonyMethod(method), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
					num++;
				}
				catch (Exception ex)
				{
					log = _log;
					if (log != null)
					{
						log.LogWarning($"RosterIntel hook failed for {typeName}.{methodName}: {ex.GetType().Name} {ex.Message}");
					}
				}
			}
			log = _log;
			if (log != null)
			{
				log.LogInfo($"RosterIntel read-only hook installed: {typeName}.{methodName} postfix x{num}.");
			}
		}
		catch (Exception ex2)
		{
			ManualLogSource log = _log;
			if (log != null)
			{
				log.LogWarning($"RosterIntel hook install exception for {typeName}.{methodName}: {ex2}");
			}
		}
	}

	private static Type? FindGameType(string name)
	{
		Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
		foreach (Assembly assembly in assemblies)
		{
			bool flag;
			switch (assembly.GetName().Name)
			{
			case "Assembly-CSharp":
			case "EsportsManager":
			case "EM.Core":
			case "EM.Data":
			case "EM.Sdk":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			if (!flag)
			{
				continue;
			}
			Type[] source;
			try
			{
				source = assembly.GetTypes();
			}
			catch (ReflectionTypeLoadException ex)
			{
				source = ex.Types.Where((Type t) => t != null).Cast<Type>().ToArray();
			}
			catch
			{
				continue;
			}
			Type type = source.FirstOrDefault((Type t) => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase) || string.Equals(t.FullName, name, StringComparison.OrdinalIgnoreCase));
			if (type != null)
			{
				return type;
			}
		}
		return null;
	}

	public static void SquadWindowPostfix(object __instance)
	{
		try
		{
			_provider?.CaptureSquadWindow(__instance);
			LogLimited(ref _squadWindowHits, "SquadWindow.Renew");
		}
		catch (Exception ex)
		{
			ManualLogSource? log = _log;
			if (log != null)
			{
				log.LogWarning((object)("RosterIntel SquadWindowPostfix failed: " + ex.GetType().Name + " " + ex.Message));
			}
		}
	}

	public static void SquadListPostfix(object __instance, object[] __args)
	{
		try
		{
			_provider?.CaptureSquadList(__instance);
			_provider?.CaptureSquadListArgs(__instance, __args);
			LogLimited(ref _squadListHits, $"SquadList.Init args={((__args != null) ? __args.Length : 0)}");
		}
		catch (Exception ex)
		{
			ManualLogSource? log = _log;
			if (log != null)
			{
				log.LogWarning((object)("RosterIntel SquadListPostfix failed: " + ex.GetType().Name + " " + ex.Message));
			}
		}
	}

	public static void PlayerSquadCardPostfix(object __instance, object[] __args)
	{
		try
		{
			_provider?.CapturePlayerSquadCard(__instance);
			_provider?.CapturePlayerSquadCardArgs(__instance, __args);
			LogLimited(ref _cardHits, $"PlayerSquadCard.Init args={((__args != null) ? __args.Length : 0)}");
		}
		catch (Exception ex)
		{
			ManualLogSource? log = _log;
			if (log != null)
			{
				log.LogWarning((object)("RosterIntel PlayerSquadCardPostfix failed: " + ex.GetType().Name + " " + ex.Message));
			}
		}
	}

	public static void PlayerStatsViewPostfix(object __instance, object[] __args)
	{
		try
		{
			_provider?.CaptureStatsContext("PlayerStatsView.Setup", __instance, __args);
			LogLimited(ref _statsViewHits, $"PlayerStatsView.Setup args={((__args != null) ? __args.Length : 0)}");
		}
		catch (Exception ex)
		{
			ManualLogSource? log = _log;
			if (log != null)
			{
				log.LogWarning((object)("RosterIntel PlayerStatsViewPostfix failed: " + ex.GetType().Name + " " + ex.Message));
			}
		}
	}

	public static void OverallStatsViewPostfix(object __instance, object[] __args)
	{
		try
		{
			_provider?.CaptureStatsContext("OverallStatsView.Setup", __instance, __args);
			LogLimited(ref _overallStatsHits, $"OverallStatsView.Setup args={((__args != null) ? __args.Length : 0)}");
		}
		catch (Exception ex)
		{
			ManualLogSource? log = _log;
			if (log != null)
			{
				log.LogWarning((object)("RosterIntel OverallStatsViewPostfix failed: " + ex.GetType().Name + " " + ex.Message));
			}
		}
	}

	public static void PlayersStatsViewPostfix(object __instance, object[] __args)
	{
		try
		{
			_provider?.CaptureStatsContext("PlayersStatsView.FillPlayerStatContexts", __instance, __args);
			LogLimited(ref _playersStatsViewHits, $"PlayersStatsView.FillPlayerStatContexts args={((__args != null) ? __args.Length : 0)}");
		}
		catch (Exception ex)
		{
			ManualLogSource? log = _log;
			if (log != null)
			{
				log.LogWarning((object)("RosterIntel PlayersStatsViewPostfix failed: " + ex.GetType().Name + " " + ex.Message));
			}
		}
	}

	public static void PlayerStatsRowPostfix(object __instance, object[] __args)
	{
		try
		{
			_provider?.CaptureStatsContext("PlayerStatsRow.Initialize", __instance, __args);
			LogLimited(ref _playerStatsRowHits, $"PlayerStatsRow.Initialize args={((__args != null) ? __args.Length : 0)}");
		}
		catch (Exception ex)
		{
			ManualLogSource? log = _log;
			if (log != null)
			{
				log.LogWarning((object)("RosterIntel PlayerStatsRowPostfix failed: " + ex.GetType().Name + " " + ex.Message));
			}
		}
	}

	private static void LogLimited(ref int counter, string source)
	{
		lock (LogLock)
		{
			counter++;
			if (!_verboseHookLogging || counter > 5)
			{
				return;
			}
			ManualLogSource log = _log;
			if (log != null)
			{
				bool flag = default(bool);
				log.LogInfo($"RosterIntel live capture hook fired: {source} hit={counter}.");
			}
		}
	}
}
