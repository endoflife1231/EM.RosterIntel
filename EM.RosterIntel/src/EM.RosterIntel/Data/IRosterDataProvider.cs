namespace EM.RosterIntel.Data;

public interface IRosterDataProvider
{
	string Name { get; }

	bool IsLive { get; }

	RosterSnapshot GetRosterSnapshot();
}
