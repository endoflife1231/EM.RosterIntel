using System;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using EM.RosterIntel.Data;
using EM.RosterIntel.Patches;
using EM.RosterIntel.Scoring;
using EM.RosterIntel.Ui;
using EM.RosterIntel.Util;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace EM.RosterIntel;

[BepInPlugin("com.dignityty.esm26.rosterintel", "EM Roster Intel", "1.0.0")]
public sealed class Plugin : BasePlugin
{
	public const string PluginGuid = "com.dignityty.esm26.rosterintel";

	public const string PluginName = "EM Roster Intel";

	public const string PluginVersion = "1.0.0";

	private ModConfig? _config;

	private Harmony? _harmony;

	public override void Load()
	{
		_config = new ModConfig(((BasePlugin)this).Config);
		ManualLogSource log = ((BasePlugin)this).Log;
		log.LogInfo($"{PluginName} {PluginVersion} loading.");
		ManualLogSource log2 = ((BasePlugin)this).Log;
		log2.LogInfo($"Unity version: {Application.unityVersion}");
		bool flag2 = AppDomain.CurrentDomain.GetAssemblies().Any(delegate(Assembly a)
		{
			switch (a.GetName().Name)
			{
			case "EM.Framework":
			case "EM.Core":
			case "EM.Data":
			case "EM.Sdk":
				return true;
			default:
				return false;
			}
		});
		ManualLogSource log3 = ((BasePlugin)this).Log;
		log3.LogInfo($"EM.Framework assemblies already loaded: {flag2}");
		if (!_config.Enabled.Value)
		{
			((BasePlugin)this).Log.LogWarning((object)"Plugin disabled by config.");
			return;
		}
		try
		{
			ClassInjector.RegisterTypeInIl2Cpp<RosterIntelOverlay>();
			GameObject val2 = new GameObject("EM.RosterIntel.Overlay");
			UnityEngine.Object.DontDestroyOnLoad(val2);
			val2.hideFlags = (HideFlags)61;
			IRosterDataProvider provider;
			if (_config.UseSampleData.Value)
			{
				provider = new SampleRosterProvider();
				((BasePlugin)this).Log.LogWarning((object)"UseSampleData=true, live roster extraction disabled by config.");
			}
			else
			{
				ReflectionRosterDataProvider reflectionRosterDataProvider = new ReflectionRosterDataProvider(((BasePlugin)this).Log, _config);
				provider = reflectionRosterDataProvider;
				if (_config.EnableReadOnlyHarmonyHooks.Value)
				{
					try
					{
						_harmony = new Harmony("com.dignityty.esm26.rosterintel.readonlyhooks");
						RosterIntelPatches.Configure(((BasePlugin)this).Log, reflectionRosterDataProvider, _config.VerboseHookLogging.Value);
						RosterIntelPatches.Install(_harmony);
					}
					catch (Exception ex)
					{
						((BasePlugin)this).Log.LogWarning((object)"RosterIntel read-only hook installation failed; continuing with passive reflection search.");
						((BasePlugin)this).Log.LogWarning((object)ex.ToString());
					}
				}
			}
			OverlayServices.Configure(((BasePlugin)this).Log, _config, provider, new RosterScoringEngine(_config));
			val2.AddComponent<RosterIntelOverlay>().Initialize();
			((BasePlugin)this).Log.LogInfo((object)"Overlay created. release: four-tab read-only roster analytics, reliable top-bar drag and fixed-step controls, objective-confidence audit, bench recommendations and sport-only Transfer Radar; no game UI buttons/tables are modified.");
			((BasePlugin)this).Log.LogInfo((object)"Read-only: no save writes, no roster changes, no match simulation patches. Live roster comes from Squad UI hooks; stats come from PlayerStatsView/MapRecord-like objects with log fallback only.");
		}
		catch (Exception ex2)
		{
			((BasePlugin)this).Log.LogError((object)"Failed to initialize EM.RosterIntel. Falling back to log-only sample report.");
			((BasePlugin)this).Log.LogError((object)ex2.ToString());
			RosterReport rosterReport = new RosterScoringEngine().Analyze(new SampleRosterProvider().GetRosterSnapshot());
			ManualLogSource log4 = ((BasePlugin)this).Log;
			log4.LogInfo($"LOG-ONLY SAMPLE: TeamFit={rosterReport.TeamFit:0.0}, WeakLink={rosterReport.WeakestLink}, Verdict={rosterReport.Verdict}");
		}
	}
}
