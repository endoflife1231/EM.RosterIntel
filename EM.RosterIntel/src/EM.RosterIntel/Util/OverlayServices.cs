using BepInEx.Logging;
using EM.RosterIntel.Data;
using EM.RosterIntel.Scoring;

namespace EM.RosterIntel.Util;

internal static class OverlayServices
{
	public static ManualLogSource? Log { get; private set; }

	public static ModConfig? Config { get; private set; }

	public static IRosterDataProvider? Provider { get; private set; }

	public static RosterScoringEngine? Scorer { get; private set; }

	public static void Configure(ManualLogSource log, ModConfig config, IRosterDataProvider provider, RosterScoringEngine scorer)
	{
		Log = log;
		Config = config;
		Provider = provider;
		Scorer = scorer;
	}
}
