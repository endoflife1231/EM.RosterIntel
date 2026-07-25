namespace EM.RosterIntel.Data;

public interface IDiagnosticRosterProvider
{
	string Status { get; }

	string LastSource { get; }

	int LastStarterCount { get; }

	int LastBenchCount { get; }

	bool LastReadWasLive { get; }
}
