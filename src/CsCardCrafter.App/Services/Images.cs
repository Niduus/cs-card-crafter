using Avalonia.Media.Imaging;
using CsCardCrafter.Core.Cards;

namespace CsCardCrafter.App.Services;

/// <summary>
/// Card images (art, backgrounds, crests, icons) loaded from the game folder once and kept. A card's asset path
/// ("cards/art/ash_snare.png") is read from the game's Assets; a full path (new art not installed yet) from disk.
/// </summary>
public static class Images
{
	static readonly Dictionary<string, Bitmap> Cache = new( StringComparer.OrdinalIgnoreCase );

	public static GameFolder Game { get; set; }

	public static Bitmap Get( string assetPath )
	{
		if ( string.IsNullOrWhiteSpace( assetPath ) )
			return null;
		var file = Path.IsPathRooted( assetPath ) ? assetPath : Game?.Resolve( assetPath );
		if ( file is null || !File.Exists( file ) )
			return null;
		if ( Cache.TryGetValue( file, out var bitmap ) )
			return bitmap;
		try
		{
			using var stream = File.OpenRead( file );
			bitmap = new Bitmap( stream );
		}
		catch ( Exception )
		{
			bitmap = null;
		}
		Cache[file] = bitmap;
		return bitmap;
	}

	/// <summary>Forget a file's image, after it was replaced on disk.</summary>
	public static void Forget( string file ) => Cache.Remove( file );
}
