namespace Logging.Net.Serilog
{
	public enum SinkType
	{
		// Outputs logs to the console
		Console,
		
		// Outputs logs to a single file without size limit
		File,

		// Transmits logs to BetterStack in their json format
		BetterStack,
	}
}
