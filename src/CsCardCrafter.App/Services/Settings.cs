using System.Text.Json;

namespace CsCardCrafter.App.Services;

/// <summary>What the crafter remembers between runs: the game folder and recent cards. Saved in the user's app data.</summary>
public sealed class Settings
{
	public string GameFolder { get; set; } = "";
	public List<string> Recent { get; set; } = new();

	static string File => Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.ApplicationData ), "CSCardCrafter", "settings.json" );

	public static Settings Load()
	{
		try
		{
			return System.IO.File.Exists( File ) ? JsonSerializer.Deserialize<Settings>( System.IO.File.ReadAllText( File ) ) ?? new() : new();
		}
		catch ( Exception )
		{
			return new();
		}
	}

	public void Save()
	{
		try
		{
			Directory.CreateDirectory( Path.GetDirectoryName( File )! );
			System.IO.File.WriteAllText( File, JsonSerializer.Serialize( this, new JsonSerializerOptions { WriteIndented = true } ) );
		}
		catch ( Exception )
		{
			// Settings are a convenience; failing to save them never stops the crafter.
		}
	}

	public void AddRecent( string path )
	{
		Recent.Remove( path );
		Recent.Insert( 0, path );
		if ( Recent.Count > 10 )
			Recent.RemoveRange( 10, Recent.Count - 10 );
		Save();
	}
}
