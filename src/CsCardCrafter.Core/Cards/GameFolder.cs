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

	/// <summary>The folder for cards made in the crafter, under Assets/cards.</summary>
	public const string CustomCollection = "custom";

	public string Root { get; }
	public string Assets => Path.Combine( Root, "Assets" );
	public string CardsFolder => Path.Combine( Assets, "cards" );
	public string ArtFolder => Path.Combine( CardsFolder, "art" );
	public string CustomFolder => Path.Combine( CardsFolder, CustomCollection );

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
	/// Puts a card into the game: the card file under Assets/cards/custom (or where it already lives in the game) and its
	/// artwork, copied into Assets/cards/art when it came from elsewhere. Returns the card file written.
	/// </summary>
	public string Install( CardDefinition card, string artworkFile = null, string existingPath = null )
	{
		var copy = CardFile.Copy( card );
		if ( !string.IsNullOrEmpty( artworkFile ) && File.Exists( artworkFile ) )
		{
			var full = Path.GetFullPath( artworkFile );
			if ( !full.StartsWith( Path.GetFullPath( Assets ), StringComparison.OrdinalIgnoreCase ) )
			{
				Directory.CreateDirectory( ArtFolder );
				var target = Path.Combine( ArtFolder, Path.GetFileNameWithoutExtension( CardFile.FileName( card.Name ) ) + Path.GetExtension( full ).ToLowerInvariant() );
				File.Copy( full, target, overwrite: true );
				full = target;
			}
			copy.Artwork = AssetPath( full );
		}

		var inGame = existingPath is not null && Path.GetFullPath( existingPath ).StartsWith( Path.GetFullPath( CardsFolder ), StringComparison.OrdinalIgnoreCase );
		var path = inGame ? existingPath : Path.Combine( CustomFolder, CardFile.FileName( card.Name ) );
		if ( !inGame )
		{
			// A new card: it joins the game as a custom card.
			copy.IsCustom = true;
			if ( string.IsNullOrEmpty( copy.Collection ) )
				copy.Collection = CustomCollection;
		}
		CardFile.Save( copy, path );
		return path;
	}
}

/// <summary>A card file in the game folder.</summary>
public sealed record GameCard( string Path, CardDefinition Card );
