using System.IO;
using ChaosSpells.Cards;

namespace CsCardCrafter.Core.Cards;

/// <summary>
/// A Chaos Spells game folder (a chaos_spells_sbox checkout): where the crafter finds the game's cards, art, card
/// backgrounds, icons and fonts, and where "Install into game" puts new cards. Like YGO Card Crafter finding Omega.
/// </summary>
public sealed class GameFolder
{
	public const string ProjectFile = "chaos_spells.sbproj";

	public string Root { get; }
	public string Assets => Path.Combine( Root, "Assets" );
	public string CardsFolder => Path.Combine( Assets, "cards" );
	public string ArtFolder => Path.Combine( CardsFolder, "art" );

	GameFolder( string root ) => Root = root;

	/// <summary>The folder at <paramref name="path"/> when it is a Chaos Spells game folder, else null.</summary>
	public static GameFolder At( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) )
			return null;
		try
		{
			var full = Path.GetFullPath( path );
			return File.Exists( Path.Combine( full, ProjectFile ) ) ? new GameFolder( full ) : null;
		}
		catch ( Exception )
		{
			return null;
		}
	}

	/// <summary>
	/// Looks for the game from <paramref name="start"/> upwards: a game folder itself, or the crafter repo's
	/// external/chaos_spells_sbox submodule (running from source).
	/// </summary>
	public static GameFolder Find( string start = null )
	{
		var dir = new DirectoryInfo( start ?? AppContext.BaseDirectory );
		while ( dir is not null )
		{
			var found = At( dir.FullName ) ?? At( Path.Combine( dir.FullName, "external", "chaos_spells_sbox" ) );
			if ( found is not null )
				return found;
			dir = dir.Parent;
		}
		return null;
	}

	/// <summary>A card's asset path ("cards/art/ash_snare.png", "ui/card/class/fire.png") as a file, or null when it isn't there.</summary>
	public string Resolve( string assetPath )
	{
		if ( string.IsNullOrWhiteSpace( assetPath ) )
			return null;
		var file = Path.Combine( Assets, assetPath.TrimStart( '/' ).Replace( '/', Path.DirectorySeparatorChar ) );
		return File.Exists( file ) ? file : null;
	}

	/// <summary>A file inside Assets as the asset path cards store ("cards/art/x.png").</summary>
	public string AssetPath( string file ) => Path.GetRelativePath( Assets, file ).Replace( Path.DirectorySeparatorChar, '/' );

	/// <summary>Every card file in the game, read. Files that don't parse are skipped and listed in <paramref name="broken"/>.</summary>
	public List<GameCard> LoadCards( List<string> broken = null )
	{
		var cards = new List<GameCard>();
		if ( !Directory.Exists( CardsFolder ) )
			return cards;
		foreach ( var file in Directory.EnumerateFiles( CardsFolder, "*" + CardFile.Extension, SearchOption.AllDirectories ) )
		{
			try
			{
				cards.Add( new GameCard( file, CardFile.Load( file ) ) );
			}
			catch ( Exception )
			{
				broken?.Add( file );
			}
		}
		return CardOrder.Sort( cards, c => c.Card ).ToList();
	}

	/// <summary>
	/// Adds a card to the game as an official card, all automatic (Justin, 2026-10-29): the card file goes in its
	/// collection folder beside the game's own cards (Assets/cards/fire, /spirit, /series...), it gets the next free
	/// card ID when it has none, its artwork is copied into Assets/cards/art, and the project's export list is checked
	/// so the card ships with the game. A card opened from the game is written back where it is.
	/// Returns what was done.
	/// </summary>
	public PublishResult Publish( CardDefinition card, string existingPath = null )
	{
		var copy = CardFile.Copy( card );
		var inGame = existingPath is not null && File.Exists( existingPath )
			&& Path.GetFullPath( existingPath ).StartsWith( Path.GetFullPath( CardsFolder ), StringComparison.OrdinalIgnoreCase );
		var others = LoadCards().Where( c => !inGame || !SamePath( c.Path, existingPath ) ).ToList();

		copy.IsCustom = false;
		if ( !inGame || string.IsNullOrEmpty( copy.Collection ) )
			copy.Collection = CollectionFor( copy );
		if ( copy.Id <= 0 || others.Any( c => c.Card.Id == copy.Id ) )
			copy.Id = NextId( copy, others.Select( c => c.Card ) );

		string artFile = null;
		var art = copy.Artwork ?? "";
		if ( Path.IsPathRooted( art ) && File.Exists( art ) && !Path.GetFullPath( art ).StartsWith( Path.GetFullPath( Assets ), StringComparison.OrdinalIgnoreCase ) )
		{
			Directory.CreateDirectory( ArtFolder );
			artFile = Path.Combine( ArtFolder, Path.GetFileNameWithoutExtension( CardFile.FileName( copy.Name ) ) + Path.GetExtension( art ).ToLowerInvariant() );
			File.Copy( art, artFile, overwrite: true );
			copy.Artwork = AssetPath( artFile );
		}
		else if ( Path.IsPathRooted( art ) && File.Exists( art ) )
		{
			copy.Artwork = AssetPath( art );
		}

		var path = inGame ? existingPath : Path.Combine( CardsFolder, FolderName( copy.Collection ), CardFile.FileName( copy.Name ) );
		if ( !inGame && File.Exists( path ) )
			throw new InvalidOperationException( $"The game already has a card file {AssetPath( path )}. Rename the card, or open that card to edit it." );
		CardFile.Save( copy, path );
		var exportFixed = EnsureExported();
		return new PublishResult( path, copy, artFile, exportFixed );
	}

	static bool SamePath( string a, string b ) => string.Equals( Path.GetFullPath( a ), Path.GetFullPath( b ), StringComparison.OrdinalIgnoreCase );

	/// <summary>
	/// The collection a new card joins, as the game's cards are filed: Spirit, Quick Cast and Fusion cards have their
	/// own, cards of a series (Spawn, Kitharas...) go in Series, the rest by Mastery (Light is stored as Sun).
	/// </summary>
	public static string CollectionFor( CardDefinition card )
	{
		if ( card.IsSpirit ) return "Spirit";
		if ( card.IsQuickCast ) return "QuickCast";
		if ( card.Deck == DeckKind.Side || card.HasType( "Fusion" ) ) return "Fusion";
		if ( card.Series.Count > 0 ) return "Series";
		var mastery = Masteries.Get( card.Mastery ).Name;
		return mastery == "Darkness" ? "Shadow" : mastery;
	}

	/// <summary>The folder under Assets/cards for a collection: its name in lower case, as the game's folders are.</summary>
	public static string FolderName( string collection ) =>
		System.Text.RegularExpressions.Regex.Replace( (collection ?? "").ToLowerInvariant(), "[^a-z0-9]+", "_" ).Trim( '_' );

	/// <summary>
	/// The next free ID. The game numbers its regular cards 1, 2, 3... (dev cards sit at 1000+, so they're skipped);
	/// Spirit and Fusion cards use 10000+.
	/// </summary>
	public static int NextId( CardDefinition card, IEnumerable<CardDefinition> others )
	{
		var ids = others.Select( c => c.Id ).ToList();
		if ( card.IsSpirit || card.Deck == DeckKind.Side || card.HasType( "Fusion" ) )
			return Math.Max( 10000, ids.Where( i => i >= 10000 ).DefaultIfEmpty( 10000 ).Max() + 1 );
		return ids.Where( i => i > 0 && i < 1000 ).DefaultIfEmpty( 0 ).Max() + 1;
	}

	/// <summary>
	/// The s&amp;box project's export list must hold cards/*, or new cards don't ship with the game. The editor has been
	/// seen saving it as null; this puts the game's list back. Returns true when it had to fix it.
	/// </summary>
	public bool EnsureExported()
	{
		var file = Path.Combine( Root, ProjectFile );
		var text = File.ReadAllText( file );
		var match = System.Text.RegularExpressions.Regex.Match( text, "\"Resources\":\\s*(null|\"(?<list>(?:[^\"\\\\]|\\\\.)*)\")" );
		if ( !match.Success )
			return false;
		var list = match.Groups["list"].Success ? match.Groups["list"].Value : null;
		if ( list is not null && list.Split( "\\n" ).Any( l => l.Trim() == "cards/*" ) )
			return false;
		var entries = (list ?? "").Split( "\\n", StringSplitOptions.RemoveEmptyEntries ).ToList();
		foreach ( var needed in new[] { "cards/*", "ui/*", "sounds/*", "fonts/*", "materials/*" } )
			if ( !entries.Contains( needed ) )
				entries.Add( needed );
		var fixedText = text.Remove( match.Index, match.Length ).Insert( match.Index, "\"Resources\": \"" + string.Join( "\\n", entries ) + "\"" );
		File.WriteAllText( file, fixedText );
		return true;
	}

	/// <summary>
	/// When the game folder is a git checkout: stages and commits the card file and its art, so the card is in the
	/// game's history ready to push. Returns git's answer, or null when there is no git or no repository.
	/// </summary>
	public string Commit( PublishResult published, string message = null )
	{
		if ( !Directory.Exists( Path.Combine( Root, ".git" ) ) && !File.Exists( Path.Combine( Root, ".git" ) ) )
			return null;
		var files = new List<string> { published.CardFile, Path.Combine( Root, ProjectFile ) };
		if ( published.ArtFile is not null )
			files.Add( published.ArtFile );
		var add = Git( "add -- " + string.Join( " ", files.Select( f => $"\"{Path.GetRelativePath( Root, f )}\"" ) ) );
		if ( add is null )
			return null;
		return Git( $"commit -m \"{(message ?? $"Add card: {published.Card.Name}").Replace( "\"", "'" )}\" -- " + string.Join( " ", files.Select( f => $"\"{Path.GetRelativePath( Root, f )}\"" ) ) );
	}

	string Git( string arguments )
	{
		try
		{
			var start = new System.Diagnostics.ProcessStartInfo( "git", arguments )
			{
				WorkingDirectory = Root,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true,
			};
			using var process = System.Diagnostics.Process.Start( start );
			var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
			process.WaitForExit();
			return output.Trim();
		}
		catch ( Exception )
		{
			return null;
		}
	}
}

/// <summary>What <see cref="GameFolder.Publish"/> did: the card file, the card as saved (its ID and collection), the art it copied.</summary>
public sealed record PublishResult( string CardFile, CardDefinition Card, string ArtFile, bool ExportListFixed );

/// <summary>A card file in the game folder.</summary>
public sealed record GameCard( string Path, CardDefinition Card );
