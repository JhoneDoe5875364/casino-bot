using System;

namespace PragmaticBot.Services;

public class LocalStorageService : IDisposable
{
	private bool _disposed;

	public void Dispose()
	{
		if (!_disposed)
		{
			_disposed = true;
		}
		GC.SuppressFinalize(this);
	}
}
