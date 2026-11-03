using CsCardCrafter.Core.Cards;
using Xunit;

namespace CsCardCrafter.Tests;

/// <summary>The crafter finds the game on its own: from s&amp;box's project list, near itself, or by looking around.</summary>
public class GameFinderTests
{
	static readonly GameFolder Game = GameFolder.Find() ?? throw new InvalidOperationException( "The chaos_spells_sbox submodule is missing: git submodule update --init" );

	static string TempDir()
	{
		var dir = Path.Combine( Path.GetTempPath(), "csfinder-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( dir );
		return dir;
	}

	[Fact]
	public void FindsTheGameThroughTheSboxEditorsProjectList()
	{
		var steam = TempDir();
		var config = Path.Combine( steam, "steamapps", "common", "sbox", "config" );
		Directory.CreateDirectory( config );
		var sbproj = Path.Combine( Game.Root, GameFolder.ProjectFile ).Replace( "\\", "\\\\" );
		File.WriteAllText( Path.Combine( config, "addons.json" ),
			$"[{{\"ConfigFilePath\":\"C:\\\\nowhere\\\\other.sbproj\",\"LastOpened\":\"2026-11-01T10:00:00+00:00\"}}," +
			$"{{\"ConfigFilePath\":\"{sbproj}\",\"LastOpened\":\"2026-11-03T10:00:00+00:00\"}}]" );

		var found = GameFinder.Find( saved: null, starts: Array.Empty<string>(), searchRoots: Array.Empty<(string, int)>(), steamRoots: new[] { steam } );
		Assert.NotNull( found );
		Assert.Equal( GameFinder.Source.SboxEditor, found.From );
		Assert.Equal( Path.GetFullPath( Game.Root ), found.Game.Root );
	}

	[Fact]
	public void ReadsSteamLibraryFolders()
	{
		var dir = TempDir();
		var vdf = Path.Combine( dir, "libraryfolders.vdf" );
		File.WriteAllText( vdf, "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\"\n\t}\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"D:\\\\SteamLibrary\"\n\t}\n}\n" );
		Assert.Equal( new[] { @"C:\Program Files (x86)\Steam", @"D:\SteamLibrary" }, GameFinder.LibraryFolders( vdf ).ToArray() );
	}

	[Fact]
	public void SavedFolderWinsAndAStaleOneIsSkipped()
	{
		var none = Array.Empty<string>();
		Assert.Equal( GameFinder.Source.Saved, GameFinder.Find( Game.Root, none, Array.Empty<(string, int)>(), none ).From );
		var near = GameFinder.Find( Path.Combine( TempDir(), "gone" ), new[] { AppContext.BaseDirectory }, Array.Empty<(string, int)>(), none );
		Assert.Equal( GameFinder.Source.NearCrafter, near.From );
	}

	[Fact]
	public void SearchFindsAProjectInsideTheUsualFolders()
	{
		var docs = TempDir();
		var project = Path.Combine( docs, "Projects", "chaos_spells_sbox" );
		Directory.CreateDirectory( Path.Combine( project, "Assets", "cards" ) );
		File.WriteAllText( Path.Combine( project, GameFolder.ProjectFile ), "{}" );
		Directory.CreateDirectory( Path.Combine( docs, "node_modules", "x" ) );

		var none = Array.Empty<string>();
		var found = GameFinder.Find( null, none, new[] { (docs, 3) }, none );
		Assert.Equal( GameFinder.Source.Search, found.From );
		Assert.Equal( project, found.Game.Root );
		Assert.Null( GameFinder.Find( null, none, new[] { (docs, 1) }, none ) );
	}
}
