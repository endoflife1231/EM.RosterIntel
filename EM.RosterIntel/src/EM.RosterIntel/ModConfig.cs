using System;
using BepInEx.Configuration;
using UnityEngine;

namespace EM.RosterIntel;

public sealed class ModConfig
{
	public ConfigEntry<bool> Enabled { get; }

	public ConfigEntry<bool> LogOnlyMode { get; }

	public ConfigEntry<bool> UseSampleData { get; }

	public ConfigEntry<bool> FallbackToSampleOnLiveFailure { get; }

	public ConfigEntry<bool> ShowDebugInfo { get; }

	public ConfigEntry<bool> CompactMode { get; }

	public ConfigEntry<bool> ManagedTeamFocus { get; }

	public ConfigEntry<string> ManagedTeamName { get; }

	public ConfigEntry<int> AutoManagedTeamMinBench { get; }

	public ConfigEntry<string> HotkeyToggle { get; }

	public ConfigEntry<float> PanelX { get; }

	public ConfigEntry<float> PanelY { get; }

	public ConfigEntry<float> PanelWidth { get; }

	public ConfigEntry<float> PanelHeight { get; }

	public ConfigEntry<float> Scale { get; }

	public ConfigEntry<float> RefreshIntervalSeconds { get; }

	public ConfigEntry<bool> DarkPanel { get; }

	public ConfigEntry<float> PanelOpacity { get; }

	public ConfigEntry<bool> ForceOpaquePanel { get; }

	public ConfigEntry<float> PanelBackgroundRed { get; }

	public ConfigEntry<float> PanelBackgroundGreen { get; }

	public ConfigEntry<float> PanelBackgroundBlue { get; }

	public ConfigEntry<bool> ShowPlayerRows { get; }

	public ConfigEntry<bool> AutoFitPanel { get; }

	public ConfigEntry<bool> RussianUi { get; }

	public ConfigEntry<int> MaxBenchRows { get; }

	public ConfigEntry<string> ResizeGrowHotkey { get; }

	public ConfigEntry<string> ResizeShrinkHotkey { get; }

	public ConfigEntry<string> ResetWindowHotkey { get; }

	public ConfigEntry<float> ResizeStepWidth { get; }

	public ConfigEntry<float> ResizeStepHeight { get; }

	public ConfigEntry<float> MinPanelWidth { get; }

	public ConfigEntry<float> MinPanelHeight { get; }

	public ConfigEntry<float> MaxPanelWidth { get; }

	public ConfigEntry<float> MaxPanelHeight { get; }

	public ConfigEntry<bool> DrawTextShadow { get; }

	public ConfigEntry<bool> UseReadableLayout { get; }

	public ConfigEntry<bool> PreferSquadWindowTeam { get; }

	public ConfigEntry<bool> UseSquadCardsFallback { get; }

	public ConfigEntry<int> MinimumStarterCountForLive { get; }

	public ConfigEntry<bool> LogLiveRosterOnce { get; }

	public ConfigEntry<bool> LogLiveFailures { get; }

	public ConfigEntry<bool> VerboseHookLogging { get; }

	public ConfigEntry<bool> EnableReadOnlyHarmonyHooks { get; }

	public ConfigEntry<bool> EnableDirectPlayerStatsImport { get; }

	public ConfigEntry<int> DirectStatsScanDepth { get; }

	public ConfigEntry<int> DirectStatsMaxObjectsPerCapture { get; }

	public ConfigEntry<bool> DirectStatsLogFirstCapture { get; }

	public ConfigEntry<bool> EnableOpponentTierStatsLogImport { get; }

	public ConfigEntry<string> OpponentTierStatsLogPath { get; }

	public ConfigEntry<int> OpponentTierStatsMaxLogBytes { get; }

	public ConfigEntry<float> MinimumSwapTeamFitDelta { get; }

	public ConfigEntry<float> MinimumSwapRoleDelta { get; }

	public ConfigEntry<bool> EnableTransferRadar { get; }

	public ConfigEntry<int> TransferRadarMaxRows { get; }

	public ConfigEntry<bool> ProbeGameTypes { get; }

	public ConfigEntry<int> ProbeTypeLimit { get; }

	public ConfigEntry<bool> ProbeMemberDetails { get; }

	public ConfigEntry<bool> ProbeStaticValues { get; }

	public ConfigEntry<int> ProbeMemberLimit { get; }

	public KeyCode ToggleKeyCode
	{
		get
		{
			if (!Enum.TryParse<KeyCode>(HotkeyToggle.Value, true, out KeyCode result))
			{
				return (KeyCode)290;
			}
			return result;
		}
	}

	public KeyCode ResizeGrowKeyCode
	{
		get
		{
			if (!Enum.TryParse<KeyCode>(ResizeGrowHotkey.Value, true, out KeyCode result))
			{
				return (KeyCode)61;
			}
			return result;
		}
	}

	public KeyCode ResizeShrinkKeyCode
	{
		get
		{
			if (!Enum.TryParse<KeyCode>(ResizeShrinkHotkey.Value, true, out KeyCode result))
			{
				return (KeyCode)45;
			}
			return result;
		}
	}

	public KeyCode ResetWindowKeyCode
	{
		get
		{
			if (!Enum.TryParse<KeyCode>(ResetWindowHotkey.Value, true, out KeyCode result))
			{
				return (KeyCode)8;
			}
			return result;
		}
	}

	public ModConfig(ConfigFile cfg)
	{
		Enabled = cfg.Bind<bool>("General", "Enabled", true, "Master switch.");
		LogOnlyMode = cfg.Bind<bool>("General", "LogOnlyMode", false, "If true, do not draw UI; only write reports to BepInEx log.");
		UseSampleData = cfg.Bind<bool>("General", "UseSampleData", false, "If true, force screenshot-based sample roster. release default is live-read mode.");
		FallbackToSampleOnLiveFailure = cfg.Bind<bool>("General", "FallbackToSampleOnLiveFailure", false, "If live roster cannot be read yet, show sample data. Release default is false so the panel waits for real live data instead of showing misleading sample numbers.");
		ShowDebugInfo = cfg.Bind<bool>("General", "ShowDebugInfo", true, "Show provider/debug status inside panel.");
		CompactMode = cfg.Bind<bool>("General", "CompactMode", true, "Compact 1920x1080-safe layout.");
		ManagedTeamFocus = cfg.Bind<bool>("General", "ManagedTeamFocus", true, "Release: prioritize the human-managed team for exact confidence/scouting. Other teams are still shown, but treated as scouting mode.");
		ManagedTeamName = cfg.Bind<string>("General", "ManagedTeamName", "auto", "Human-managed team name or aliases separated by ; . Use auto to learn the managed team from the active save/session instead of hardcoding G2.");
		AutoManagedTeamMinBench = cfg.Bind<int>("General", "AutoManagedTeamMinBench", 1, "When ManagedTeamName=auto, prefer learning from a full live roster with at least this many bench players. Set 0 if your save has no substitutes.");
		HotkeyToggle = cfg.Bind<string>("UI", "HotkeyToggle", "F9", "Unity KeyCode string. Example: F9, F10, Insert.");
		PanelX = cfg.Bind<float>("UI", "PanelX", 56f, "Panel X position. Release default matches the compact dashboard layout.");
		PanelY = cfg.Bind<float>("UI", "PanelY", 36f, "Panel Y position. Release default leaves room for the team header behind the overlay.");
		PanelWidth = cfg.Bind<float>("UI", "PanelWidth", 1120f, "Panel width. Release default is compact but still fits status, verdict, roster and bench rows.");
		PanelHeight = cfg.Bind<float>("UI", "PanelHeight", 760f, "Panel height. Release default matches the compact layout shown in testing; grow with = if needed.");
		Scale = cfg.Bind<float>("UI", "Scale", 1f, "UI scale multiplier. Release defaults to 1.0 because the panel itself is larger; increase only if you want bigger text.");
		RefreshIntervalSeconds = cfg.Bind<float>("UI", "RefreshIntervalSeconds", 3f, "How often the overlay refreshes the report.");
		DarkPanel = cfg.Bind<bool>("UI", "DarkPanel", true, "Keep the overlay on a dark panel.");
		PanelOpacity = cfg.Bind<float>("UI", "PanelOpacity", 1f, "Overlay alpha when ForceOpaquePanel=false. Release defaults to forced opaque fill.");
		ForceOpaquePanel = cfg.Bind<bool>("UI", "ForceOpaquePanel", true, "Release: force an opaque IMGUI panel using built-in GUIStyle textures/Box fallback. Does not call GUI.DrawTexture because Unity 6000 IL2CPP may throw Method unstripping failed.");
		PanelBackgroundRed = cfg.Bind<float>("UI", "PanelBackgroundRed", 0.012f, "Opaque panel background red channel, 0..1.");
		PanelBackgroundGreen = cfg.Bind<float>("UI", "PanelBackgroundGreen", 0.02f, "Opaque panel background green channel, 0..1.");
		PanelBackgroundBlue = cfg.Bind<float>("UI", "PanelBackgroundBlue", 0.034f, "Opaque panel background blue channel, 0..1.");
		ShowPlayerRows = cfg.Bind<bool>("UI", "ShowPlayerRows", true, "Show compact starter rows inside the panel when there is enough space.");
		AutoFitPanel = cfg.Bind<bool>("UI", "AutoFitPanel", true, "If true, release keeps the panel within min/max size so rows and verdicts fit.");
		RussianUi = cfg.Bind<bool>("UI", "RussianUi", true, "Use Russian labels in the overlay. Role names like IGL/Rifler/AWPer stay in esports notation.");
		MaxBenchRows = cfg.Bind<int>("UI", "MaxBenchRows", 3, "Maximum bench rows shown in compact mode. Release default is 3 to keep the panel clean.");
		ResizeGrowHotkey = cfg.Bind<string>("UI", "ResizeGrowHotkey", "Equals", "Hotkey to enlarge the panel. Unity KeyCode string. Default: Equals (= / + key on many keyboards).");
		ResizeShrinkHotkey = cfg.Bind<string>("UI", "ResizeShrinkHotkey", "Minus", "Hotkey to shrink the panel. Unity KeyCode string. Default: Minus (-).");
		ResetWindowHotkey = cfg.Bind<string>("UI", "ResetWindowHotkey", "Backspace", "Hotkey to reset the panel position and size.");
		ResizeStepWidth = cfg.Bind<float>("UI", "ResizeStepWidth", 80f, "Width added/removed per resize hotkey press.");
		ResizeStepHeight = cfg.Bind<float>("UI", "ResizeStepHeight", 55f, "Height added/removed per resize hotkey press.");
		MinPanelWidth = cfg.Bind<float>("UI", "MinPanelWidth", 980f, "Minimum panel width after auto-fit/resizing.");
		MinPanelHeight = cfg.Bind<float>("UI", "MinPanelHeight", 640f, "Minimum panel height after auto-fit/resizing.");
		MaxPanelWidth = cfg.Bind<float>("UI", "MaxPanelWidth", 1660f, "Maximum panel width after resizing.");
		MaxPanelHeight = cfg.Bind<float>("UI", "MaxPanelHeight", 980f, "Maximum panel height after resizing.");
		DrawTextShadow = cfg.Bind<bool>("UI", "DrawTextShadow", true, "Draw dark offset labels behind important text for readability without unsafe GUIStyle.");
		UseReadableLayout = cfg.Bind<bool>("UI", "UseReadableLayout", true, "Use release readable dashboard layout: structured sections, short non-duplicated footer, explicit bench-only swap scope.");
		PreferSquadWindowTeam = cfg.Bind<bool>("LiveData", "PreferSquadWindowTeam", true, "First try to read DataTeam from the live SquadWindow, then call GetMainSquadPlayers/GetAllPlayers.");
		UseSquadCardsFallback = cfg.Bind<bool>("LiveData", "UseSquadCardsFallback", true, "Fallback: read visible PlayerSquadCard components on the squad screen.");
		MinimumStarterCountForLive = cfg.Bind<int>("LiveData", "MinimumStarterCountForLive", 5, "Minimum starters required before a live read is accepted.");
		LogLiveRosterOnce = cfg.Bind<bool>("LiveData", "LogLiveRosterOnce", true, "Log the first successful live roster extraction.");
		LogLiveFailures = cfg.Bind<bool>("LiveData", "LogLiveFailures", true, "Release: log only meaningful/deduplicated live extraction failures. Normal waiting for SquadList.Init is not spammed every refresh.");
		VerboseHookLogging = cfg.Bind<bool>("LiveData", "VerboseHookLogging", false, "Release: if true, log first Squad/Stats hook hits for debugging. Default false keeps LogOutput clean after successful tests.");
		EnableReadOnlyHarmonyHooks = cfg.Bind<bool>("LiveData", "EnableReadOnlyHarmonyHooks", true, "Install read-only hooks on SquadWindow/SquadList/PlayerSquadCard and stats/profile UI methods. Does not modify game state.");
		EnableDirectPlayerStatsImport = cfg.Bind<bool>("LiveData", "EnableDirectPlayerStatsImport", true, "release primary stats path: capture PlayerStatsView/OverallStatsView/PlayersStatsView/PlayerStatsRow objects and read MapRecord-like data directly by reflection.");
		DirectStatsScanDepth = cfg.Bind<int>("LiveData", "DirectStatsScanDepth", 5, "Maximum object graph depth for direct stats capture. Keep modest for IL2CPP safety.");
		DirectStatsMaxObjectsPerCapture = cfg.Bind<int>("LiveData", "DirectStatsMaxObjectsPerCapture", 900, "Maximum objects scanned per stats UI hook hit.");
		DirectStatsLogFirstCapture = cfg.Bind<bool>("LiveData", "DirectStatsLogFirstCapture", false, "Release: log first successful direct stats capture only when debugging. Default false keeps release logs clean.");
		EnableOpponentTierStatsLogImport = cfg.Bind<bool>("LiveData", "EnableOpponentTierStatsLogImport", true, "Fallback only: parse BepInEx/LogOutput.log for OpponentTierStats tier rows if direct stats capture has not already supplied those values.");
		OpponentTierStatsLogPath = cfg.Bind<string>("LiveData", "OpponentTierStatsLogPath", "", "Optional full path to LogOutput.log. Empty = auto-detect next to BepInEx root.");
		OpponentTierStatsMaxLogBytes = cfg.Bind<int>("LiveData", "OpponentTierStatsMaxLogBytes", 900000, "Max tail bytes read from LogOutput.log when fallback-importing OpponentTierStats. Keeps file reads lightweight.");
		MinimumSwapTeamFitDelta = cfg.Bind<float>("Scoring", "MinimumSwapTeamFitDelta", 0.35f, "Release: minimum team-fit gain required before a bench swap is shown. Suppresses noisy +0.0/+0.1 flip-flops between close riflers.");
		MinimumSwapRoleDelta = cfg.Bind<float>("Scoring", "MinimumSwapRoleDelta", 0.35f, "Release: minimum role-adjusted gain required for normal non-AWP swaps. AWP protection can still require more.");
		EnableTransferRadar = cfg.Bind<bool>("TransferRadar", "EnableTransferRadar", true, "Release table: remember players from visited non-owned teams and compare them read-only against your current weak roles. Does not buy, sign, or write saves.");
		TransferRadarMaxRows = cfg.Bind<int>("TransferRadar", "TransferRadarMaxRows", 3, "How many transfer-radar rows to show in the overlay. Pool is built from teams/free-agent screens you have opened in this session.");
		ProbeGameTypes = cfg.Bind<bool>("Debug", "ProbeGameTypes", false, "Log candidate types from Assembly-CSharp/EsportsManager/EM.* only.");
		ProbeTypeLimit = cfg.Bind<int>("Debug", "ProbeTypeLimit", 150, "Max candidate types to log.");
		ProbeMemberDetails = cfg.Bind<bool>("Debug", "ProbeMemberDetails", false, "Log fields/properties/methods for key game data/UI types. Use only for diagnostics.");
		ProbeStaticValues = cfg.Bind<bool>("Debug", "ProbeStaticValues", false, "Try to log safe summaries of static fields/properties for key data containers. Use only for diagnostics.");
		ProbeMemberLimit = cfg.Bind<int>("Debug", "ProbeMemberLimit", 80, "Max members to log per probed type.");
	}
}
