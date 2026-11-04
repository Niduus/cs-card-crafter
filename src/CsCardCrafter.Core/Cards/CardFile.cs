using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ChaosSpells.Cards;

namespace CsCardCrafter.Core.Cards;

/// <summary>
/// Reads and writes <c>.cscard</c> files exactly as the game stores them: indented JSON, enums as words, the
/// properties of the game's <see cref="CardDefinition"/> in its own order.
/// </summary>
public static class CardFile
{
	public const string Extension = ".cscard";

	public static readonly JsonSerializerOptions Json = new()
	{
		WriteIndented = true,
		Converters = { new JsonStringEnumConverter() },
		// Keep quotes and accents readable in the file ("Kitharas" stays "\"Kitharas\"", not ""Kitharas"").
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
	};

	public static CardDefinition Parse( string json ) => Fix( JsonSerializer.Deserialize<CardDefinition>( json, Json ) );

	public static CardDefinition Load( string path ) => Parse( ReadShared( path ) );

	/// <summary>
	/// Reads a file even while another program has it open (the s&amp;box editor watches and re-imports the project's
	/// files while it runs), so cards and images never go missing just because the editor is open.
	/// </summary>
	public static string ReadShared( string path )
	{
		using var stream = new FileStream( path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete );
		using var reader = new StreamReader( stream );
		return reader.ReadToEnd();
	}

	/// <summary>The file's text, with \n line endings on every system so files diff cleanly in the game repo.</summary>
	public static string Write( CardDefinition card ) => JsonSerializer.Serialize( card, Json ).Replace( "\r\n", "\n" ) + "\n";

	public static void Save( CardDefinition card, string path )
	{
		var folder = Path.GetDirectoryName( path );
		if ( !string.IsNullOrEmpty( folder ) )
			Directory.CreateDirectory( folder );
		File.WriteAllText( path, Write( card ) );
	}

	/// <summary>A deep copy, through the file format, so nothing is shared with the original.</summary>
	public static CardDefinition Copy( CardDefinition card ) => Parse( Write( card ) );

	/// <summary>The game's file name for a card: lower case, anything but letters and digits as one underscore ("Death's Recollection" is death_s_recollection).</summary>
	public static string FileName( string cardName )
	{
		var slug = Regex.Replace( (cardName ?? "").ToLowerInvariant(), "[^a-z0-9]+", "_" ).Trim( '_' );
		return (slug.Length > 0 ? slug : "new_card") + Extension;
	}

	/// <summary>Lists the JSON may leave out come back empty rather than null, so the editor never meets a null list.</summary>
	static CardDefinition Fix( CardDefinition card )
	{
		if ( card is null )
			return null;
		card.DuoMasteries ??= new();
		card.Types ??= new();
		card.Costs ??= new();
		card.Keywords ??= new();
		card.KeywordParams ??= new();
		card.Series ??= new();
		card.Effects ??= new();
		card.LegacyRecipeParams ??= new();
		card.ChoiceEntries ??= new();
		card.RoundEntries ??= new();
		card.ActiveEffectEntries ??= new();
		foreach ( var effect in card.Effects )
		{
			effect.When ??= new();
			effect.If ??= new();
			effect.Cost ??= new();
			effect.Then ??= new();
			effect.Always ??= new();
			foreach ( var op in effect.When.Concat( effect.If ).Concat( effect.Cost ).Concat( effect.Then ).Concat( effect.Always ) )
				op.Params ??= new();
		}
		return card;
	}
}
