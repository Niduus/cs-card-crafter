using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CsCardCrafter.Core.Cards;

/// <summary>
/// Finds the Chaos Spells game folder on its own, so it never has to be chosen by hand (Justin, 2026-11-03):
/// <list type="number">
/// <item>the s&amp;box editor's own project list (config/addons.json in each s&amp;box install found through Steam), which
/// names every project opened in the editor, newest first;</item>
/// <item>the folder the crafter runs from and every folder above it (running from source finds the submodule);</item>
/// <item>a short look through the usual places a project is kept: Documents, Desktop, Downloads, source/repos,
/// GitHub, OneDrive, the user folder and the top of each drive.</item>
/// </list>
/// </summary>
public static class GameFinder
{
	/// <summary>Where the game was found, for the status line.</summary>
	public enum Source { Saved, SboxEditor, NearCrafter, Search }

	public sealed record Found( GameFolder Game, Source From );

	/// <summary>What the last <see cref="Find"/> looked at, line by line, for when the game isn't found.</summary>
	public static List<string> Report { get; private set; } = new();

	/// <summary>Every way to find the game, in order; the first hit wins. <paramref name="saved"/> is the folder used last time.</summary>
	public static Found Find( string saved = null, IEnumerable<string> starts = null, IEnumerable<(string Folder, int Depth)> searchRoots = null, IEnumerable<string> steamRoots = null, TimeSpan? budget = null )
	{
		var report = new List<string>();
		Report = report;
		if ( !string.IsNullOrWhiteSpace( saved ) )
		{
			report.Add( $"Last used folder: {saved}" );
			if ( GameFolder.At( saved ) is { } last )
				return new Found( last, Source.Saved );
			report.Add( "  not a Chaos Spells project any more" );
		}

		var steam = (steamRoots ?? SteamRoots()).ToList();
		report.Add( steam.Count == 0 ? "Steam: not found" : "Steam libraries: " + string.Join( "; ", steam ) );
		var lists = SboxProjectLists( steam ).ToList();
		report.Add( lists.Count == 0 ? "s&box project list (sbox/config/addons.json): not found" : "s&box project lists: " + string.Join( "; ", lists ) );
		foreach ( var project in SboxProjects( steam ) )
		{
			if ( GameFolder.At( project ) is { } game )
				return new Found( game, Source.SboxEditor );
			report.Add( $"  s&box project {project}: no Assets/cards with .cscard files" );
		}

		foreach ( var start in starts ?? new[] { AppContext.BaseDirectory, Environment.CurrentDirectory } )
		{
			report.Add( $"Next to the crafter: {start}" );
			if ( GameFolder.Find( start ) is { } near )
				return new Found( near, Source.NearCrafter );
		}

		var roots = (searchRoots ?? SearchRoots()).ToList();
		report.Add( "Searched: " + string.Join( "; ", roots.Where( r => Directory.Exists( r.Folder ) ).Select( r => $"{r.Folder} ({r.Depth} deep)" ) ) );
		var hit = Search( roots, budget ?? TimeSpan.FromSeconds( 10 ) );
		return hit is null ? null : new Found( hit, Source.Search );
	}

	// ---- s&box editor's project list --------------------------------------------------------------------------

	/// <summary>
	/// Steam folders that may hold s&amp;box: the default installs and every library Steam lists in libraryfolders.vdf.
	/// </summary>
	public static IEnumerable<string> SteamRoots()
	{
		var roots = new List<string>();
		void Add( string p )
		{
			if ( !string.IsNullOrWhiteSpace( p ) && Directory.Exists( p ) && !roots.Contains( p, StringComparer.OrdinalIgnoreCase ) )
				roots.Add( p );
		}

		// Where Steam says it is installed (Windows keeps it in the registry).
		if ( OperatingSystem.IsWindows() )
		{
			foreach ( var (hive, key, value) in new[]
			{
				(Microsoft.Win32.Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
				(Microsoft.Win32.Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
				(Microsoft.Win32.Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"),
			} )
			{
				try
				{
					using var k = hive.OpenSubKey( key );
					if ( k?.GetValue( value ) is string path )
						Add( path.Replace( '/', '\\' ) );
				}
				catch ( Exception )
				{
					// No access: the other places still count.
				}
			}
		}
		Add( Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.ProgramFilesX86 ), "Steam" ) );
		Add( Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.ProgramFiles ), "Steam" ) );
		Add( Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.UserProfile ), ".steam", "steam" ) );
		Add( Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.UserProfile ), ".local", "share", "Steam" ) );
		foreach ( var drive in Drives() )
		{
			Add( Path.Combine( drive, "Steam" ) );
			Add( Path.Combine( drive, "SteamLibrary" ) );
			Add( Path.Combine( drive, "Games", "Steam" ) );
			Add( Path.Combine( drive, "Games", "SteamLibrary" ) );
			Add( Path.Combine( drive, "Program Files (x86)", "Steam" ) );
			Add( Path.Combine( drive, "Program Files", "Steam" ) );
		}

		// Libraries on other drives, as Steam lists them.
		foreach ( var root in roots.ToList() )
			foreach ( var library in LibraryFolders( Path.Combine( root, "steamapps", "libraryfolders.vdf" ) ) )
				Add( library );
		return roots;
	}

	/// <summary>The "path" entries of a Steam libraryfolders.vdf.</summary>
	public static IEnumerable<string> LibraryFolders( string vdf )
	{
		string text;
		try
		{
			if ( !File.Exists( vdf ) ) yield break;
			text = File.ReadAllText( vdf );
		}
		catch ( Exception )
		{
			yield break;
		}
		foreach ( Match m in Regex.Matches( text, "\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase ) )
			yield return m.Groups[1].Value.Replace( @"\\", @"\" );
	}

	/// <summary>
	/// Project folders the s&amp;box editor knows, newest first: steamapps/common/sbox/config/addons.json lists each
	/// project's .sbproj ("ConfigFilePath") with when it was last opened.
	/// </summary>
	public static IEnumerable<string> SboxProjects( IEnumerable<string> steamRoots ) =>
		SboxProjectLists( steamRoots ).SelectMany( ReadProjectList )
			.OrderByDescending( p => p.Opened ).Select( p => p.Folder ).Distinct( StringComparer.OrdinalIgnoreCase ).ToList();

	/// <summary>
	/// Every s&amp;box project list (config/addons.json) in the Steam libraries: steamapps/common/sbox, or any other
	/// game folder there that has one (a renamed or test-branch install).
	/// </summary>
	public static IEnumerable<string> SboxProjectLists( IEnumerable<string> steamRoots )
	{
		var seen = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var root in steamRoots )
		{
			var installs = new List<string> { root };
			var common = Path.Combine( root, "steamapps", "common" );
			try
			{
				if ( Directory.Exists( common ) )
					installs.AddRange( Directory.EnumerateDirectories( common ) );
			}
			catch ( Exception )
			{
				installs.Add( Path.Combine( common, "sbox" ) );
			}
			foreach ( var install in installs )
			{
				var list = Path.Combine( install, "config", "addons.json" );
				if ( File.Exists( list ) && seen.Add( list ) )
					yield return list;
			}
		}
	}

	/// <summary>One s&amp;box addons.json: the folder of each listed project file, with when it was last opened.</summary>
	public static List<(string Folder, DateTimeOffset Opened)> ReadProjectList( string file )
	{
		var result = new List<(string, DateTimeOffset)>();
		try
		{
			if ( !File.Exists( file ) )
				return result;
			using var doc = JsonDocument.Parse( File.ReadAllText( file ) );
			if ( doc.RootElement.ValueKind != JsonValueKind.Array )
				return result;
			foreach ( var entry in doc.RootElement.EnumerateArray() )
			{
				if ( entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty( "ConfigFilePath", out var pathValue ) || pathValue.ValueKind != JsonValueKind.String )
					continue;
				var path = pathValue.GetString();
				if ( string.IsNullOrWhiteSpace( path ) )
					continue;
				var folder = path.EndsWith( ".sbproj", StringComparison.OrdinalIgnoreCase ) ? Path.GetDirectoryName( path ) : path;
				var opened = entry.TryGetProperty( "LastOpened", out var o ) && o.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse( o.GetString(), out var when ) ? when : DateTimeOffset.MinValue;
				result.Add( (folder, opened) );
			}
		}
		catch ( Exception )
		{
			// A list s&box is writing right now, or an older format: skip it, the other ways still find the game.
		}
		return result;
	}

	// ---- Looking through the usual places ---------------------------------------------------------------------

	static IEnumerable<string> Drives()
	{
		DriveInfo[] drives;
		try { drives = DriveInfo.GetDrives(); }
		catch ( Exception ) { yield break; }
		foreach ( var d in drives )
		{
			bool ok;
			try { ok = d.DriveType == DriveType.Fixed && d.IsReady && OperatingSystem.IsWindows(); }
			catch ( Exception ) { ok = false; }
			if ( ok ) yield return d.RootDirectory.FullName;
		}
	}

	/// <summary>The usual places a project lives, each with how deep to look under it.</summary>
	public static IEnumerable<(string Folder, int Depth)> SearchRoots()
	{
		var home = Environment.GetFolderPath( Environment.SpecialFolder.UserProfile );
		var documents = Environment.GetFolderPath( Environment.SpecialFolder.MyDocuments );
		var desktop = Environment.GetFolderPath( Environment.SpecialFolder.DesktopDirectory );
		yield return (documents, 4);
		yield return (desktop, 3);
		yield return (Path.Combine( home, "Downloads" ), 3);
		yield return (Path.Combine( home, "source", "repos" ), 3);
		yield return (Path.Combine( documents, "GitHub" ), 3);
		yield return (Path.Combine( home, "GitHub" ), 3);
		yield return (Path.Combine( home, "OneDrive" ), 4);
		yield return (Path.Combine( documents, "s&box projects" ), 2);
		yield return (home, 4);
		foreach ( var drive in Drives() )
			yield return (drive, 3);
	}

	/// <summary>Folders never worth looking in: big, generated, or system.</summary>
	static readonly HashSet<string> Skip = new( StringComparer.OrdinalIgnoreCase )
	{
		"Windows", "Program Files", "Program Files (x86)", "ProgramData", "$Recycle.Bin", "System Volume Information", "AppData",
		"node_modules", ".git", "Library", "obj", "bin", ".vs", ".idea", "steamapps", "Temp", ".cache", ".nuget", ".dotnet",
		"Assets", "Packages", "Logs", "UserSettings",
	};

	/// <summary>
	/// Looks for a chaos_spells.sbproj under the given folders, breadth first, stopping after <paramref name="budget"/>.
	/// When several copies exist, the one whose cards folder changed last wins (the copy being worked on).
	/// </summary>
	public static GameFolder Search( IEnumerable<(string Folder, int Depth)> roots, TimeSpan budget )
	{
		var clock = System.Diagnostics.Stopwatch.StartNew();
		var seen = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var found = new List<GameFolder>();
		foreach ( var (root, depth) in roots )
		{
			if ( string.IsNullOrWhiteSpace( root ) || !Directory.Exists( root ) )
				continue;
			var level = new List<string> { root };
			for ( var d = 0; d <= depth && level.Count > 0; d++ )
			{
				var next = new List<string>();
				foreach ( var dir in level )
				{
					if ( clock.Elapsed > budget )
						return Newest( found );
					if ( !seen.Add( dir ) )
						continue;
					if ( GameFolder.At( dir ) is { } game )
					{
						found.Add( game );
						continue;
					}
					if ( d == depth )
						continue;
					try
					{
						foreach ( var sub in Directory.EnumerateDirectories( dir ) )
						{
							var name = Path.GetFileName( sub );
							if ( Skip.Contains( name ) || name.StartsWith( '.' ) && name != ".steam" )
								continue;
							if ( (File.GetAttributes( sub ) & (FileAttributes.ReparsePoint | FileAttributes.System)) != 0 )
								continue;
							next.Add( sub );
						}
					}
					catch ( Exception )
					{
						// No access: skip it.
					}
				}
				level = next;
			}
			if ( found.Count > 0 && clock.Elapsed > TimeSpan.FromSeconds( 1 ) )
				break;
		}
		return Newest( found );
	}

	static GameFolder Newest( List<GameFolder> found ) =>
		found.OrderByDescending( g =>
		{
			try { return Directory.Exists( g.CardsFolder ) ? Directory.GetLastWriteTimeUtc( g.CardsFolder ) : DateTime.MinValue; }
			catch ( Exception ) { return DateTime.MinValue; }
		} ).FirstOrDefault();
}
