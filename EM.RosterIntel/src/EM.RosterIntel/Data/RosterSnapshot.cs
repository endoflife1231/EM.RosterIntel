using System.Collections.Generic;
using System.Linq;

namespace EM.RosterIntel.Data;

public sealed class RosterSnapshot
{
	public string TeamName { get; init; } = "Unknown";

	public IReadOnlyList<PlayerSnapshot> Players { get; init; } = new List<PlayerSnapshot>();

	public IReadOnlyList<PlayerSnapshot> Starters => Players.Where((PlayerSnapshot p) => p.IsStarter).Take(5).ToList();

	public IReadOnlyList<PlayerSnapshot> Bench => Players.Where((PlayerSnapshot p) => !p.IsStarter).ToList();

	public bool? IsLikelyHumanManagedTeam { get; init; }

	public string ManagedTeamSignal { get; init; } = "unknown";
}
