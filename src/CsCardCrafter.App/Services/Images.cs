using Avalonia.Media.Imaging;
using CsCardCrafter.Core.Cards;

namespace CsCardCrafter.App.Services;

/// <summary>
/// Card images (art, backgrounds, crests, icons) loaded from the game folder once and kept. A card's asset path
/// ("cards/art/ash_snare.png") is read from the game's Assets; a full path (new art not installed yet) from disk.
/// Files are read even while the s&amp;box editor has them open, a failed load is tried again next time (never kept
/// as missing), and every failure is written to the crafter's log with the reason.
/// </summary>
public static class Images
{
	static readonly Dictionary<string, Bitmap> Cache = new( StringComparer.OrdinalIgnoreCase );
	static readonly HashSet<string> Logged = new( StringComparer.OrdinalIgnoreCase );

	public static GameFolder Game { get; set; }

	public static Bitmap Get( string assetPath )
	{
		if ( string.IsNullOrWhiteSpace( assetPath ) )
			return null;
		var file = Path.IsPathRooted( assetPath ) ? assetPath : Game?.Resolve( assetPath );
		if ( file is null || !File.Exists( file ) )
		{
			Fail( assetPath, Game is null ? "no game folder" : $"no file at {Path.Combine( Game.Assets, assetPath )}" );
			return null;
		}
		if ( Cache.TryGetValue( file, out var bitmap ) )
			return bitmap;
		for ( var attempt = 0; attempt < 3 && bitmap is null; attempt++ )
		{
			try
			{
				using var stream = new FileStream( file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete );
				var memory = new MemoryStream();
				stream.CopyTo( memory );
				memory.Position = 0;
				bitmap = new Bitmap( memory );
			}
			catch ( Exception e )
			{
				if ( attempt == 2 )
					Fail( assetPath, $"{file}: {e.GetType().Name}: {e.Message}" );
				else
					Thread.Sleep( 30 );
			}
		}
		if ( bitmap is not null )
			Cache[file] = bitmap;
		return bitmap;
	}

	static void Fail( string assetPath, string why )
	{
		if ( Logged.Add( assetPath ) )
			Log.Write( $"Image not shown: {assetPath} ({why})" );
	}

	/// <summary>Forget a file's image, after it was replaced on disk.</summary>
	public static void Forget( string file ) => Cache.Remove( file );

	/// <summary>Forget every image (another game folder was chosen).</summary>
	public static void Clear()
	{
		Cache.Clear();
		Logged.Clear();
	}
}
