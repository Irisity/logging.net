using System;
using System.Threading;

namespace Logging.Net.Abstractions.Helpers
{
	/// <summary>
	/// Helper class for Time method.
	/// </summary>
	internal class DisposableAction : IDisposable
	{
		private readonly Action action;
		private int disposed;

		public DisposableAction(Action action)
		{
			this.action = action;
		}

		public void Dispose()
	    {
	        // Run at most once: a handle that is both used in a using block and disposed defensively
	        // would otherwise log the same operation twice, with different elapsed times.
	        if (Interlocked.Exchange(ref disposed, 1) != 0)
	        {
	            return;
	        }

	        try
	        {
	            action();
	        }
	        catch (Exception)
	        {
	            // Dispose commonly runs while an exception is unwinding the stack. Letting a logging
	            // failure escape here would replace the exception the caller actually needs to see.
	        }
	    }
	}
}
