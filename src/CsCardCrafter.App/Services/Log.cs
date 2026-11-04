namespace CsCardCrafter.App.Services;

/// <summary>
/// The crafter's log, %APPDATA%\CSCardCrafter\crafter-log.txt: which game folder it uses, how many cards it read,
/// card files it couldn't read, images it couldn't show and searches that failed, each with the reason. Started fresh
/// every run; send it when something looks wrong.
/// </summary>
public static class Log
{
	public static string File => Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.ApplicationData ), "CSCardCrafter", "crafter-log.txt" );
	static readonly object Gate = new();
	static bool _started;

	public static void Write( string line )
	{
		lock ( Gate )
		{
			try
			{
				Directory.CreateDirectory( Path.GetDirectoryName( File )! );
				if ( !_started )
				{
					_started = true;
					System.IO.File.WriteAllText( File, $"CS Card Crafter {typeof( Log ).Assembly.GetName().Version} on {Environment.OSVersion}, {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" );
				}
				System.IO.File.AppendAllText( File, $"{DateTime.Now:HH:mm:ss} {line}\n" );
			}
			catch ( Exception )
			{
				// Logging never stops the crafter.
			}
		}
	}
}
