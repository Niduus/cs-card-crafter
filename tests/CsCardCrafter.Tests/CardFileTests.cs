using ChaosSpells.Cards;
using CsCardCrafter.Core.Cards;
using CsCardCrafter.Core.Checks;
using Xunit;

namespace CsCardCrafter.Tests;

public class CardFileTests
{
	static readonly GameFolder Game = GameFolder.Find() ?? throw new InvalidOperationException( "The chaos_spells_sbox submodule is missing: git submodule update --init" );

	[Fact]
	public void FindsTheGameAndAllItsCards()
	{
		var broken = new List<string>();
		var cards = Game.LoadCards( broken );
		Assert.Empty( broken );
		Assert.True( cards.Count >= 392, $"only {cards.Count} cards" );
	}

	/// <summary>
	/// Opening and saving a game card without edits gives back the same file, so the crafter never rewrites cards it
	/// didn't change. The one allowed difference: older imports have no DuoMasteries line, which saving adds empty.
	/// </summary>
	[Fact]
	public void GameCardsRoundTripUnchanged()
	{
		static string Plain( string json ) => json.Replace( "\r\n", "\n" ).Replace( "  \"DuoMasteries\": [],\n", "" ).TrimEnd() + "\n";
		var changed = Game.LoadCards()
			.Where( c => Plain( CardFile.Write( c.Card ) ) != Plain( File.ReadAllText( c.Path ) ) )
			.Select( c => Path.GetFileName( c.Path ) )
			.ToList();
		Assert.True( changed.Count == 0, "Changed on save: " + string.Join( ", ", changed.Take( 10 ) ) );
	}

	[Fact]
	public void FileNamesFollowTheGame()
	{
		Assert.Equal( "death_s_recollection.cscard", CardFile.FileName( "Death's Recollection" ) );
		Assert.Equal( "new_card.cscard", CardFile.FileName( "  " ) );
	}

	[Fact]
	public void NewCardHasItsLookAndPassesChecksButIdAndArt()
	{
		var card = CardParts.NewCard( "Ice" );
		Assert.Equal( "ui/card/backgrounds/icebackground.png", card.Background );
		Assert.Equal( "ui/card/class/ice.png", card.ClassIcon );
		Assert.NotNull( Game.Resolve( card.Background ) );
		Assert.NotNull( Game.Resolve( card.TypeIcon ) );
		var problems = CardChecks.Run( card, Game ).Where( c => c.Level == CheckLevel.Problem ).ToList();
		Assert.Empty( problems );
	}

	[Fact]
	public void InstallWritesACustomCard()
	{
		var root = Path.Combine( Path.GetTempPath(), "cscrafter-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( Path.Combine( root, "Assets", "cards" ) );
		File.WriteAllText( Path.Combine( root, GameFolder.ProjectFile ), "{}" );
		var art = Path.Combine( root, "art.png" );
		File.WriteAllBytes( art, new byte[] { 1, 2, 3 } );
		try
		{
			var game = GameFolder.At( root );
			var card = CardParts.NewCard();
			card.Name = "Ember Test";
			var path = game.Install( card, art );
			Assert.Equal( Path.Combine( game.CustomFolder, "ember_test.cscard" ), path );
			var saved = CardFile.Load( path );
			Assert.Equal( "cards/art/ember_test.png", saved.Artwork );
			Assert.True( saved.IsCustom );
			Assert.NotNull( game.Resolve( saved.Artwork ) );
		}
		finally
		{
			Directory.Delete( root, true );
		}
	}
}
