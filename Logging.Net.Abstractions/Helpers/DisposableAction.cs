using System;

namespace Logging.Net.Abstractions.Helpers
{
	/// <summary>
	/// Helper class for Time method.
	/// </summary>
	internal class DisposableAction : IDisposable
	{
		private readonly Action action;

		public DisposableAction(Action action)
		{
			this.action = action;
		}

		public void Dispose()
	    {
	        action();
	    }
	}
}
