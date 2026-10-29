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
	public void PublishAddsAnOfficialCard()
	{
		var root = Path.Combine( Path.GetTempPath(), "cscrafter-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( Path.Combine( root, "Assets", "cards", "fire" ) );
		// The editor once saved the export list as null; publishing puts it back.
		File.WriteAllText( Path.Combine( root, GameFolder.ProjectFile ), "{\n  \"Resources\": null,\n  \"Title\": \"x\"\n}" );
		var existing = CardParts.NewCard();
		existing.Id = 41;
		existing.Name = "Old Flame";
		CardFile.Save( existing, Path.Combine( root, "Assets", "cards", "fire", "old_flame.cscard" ) );
		var art = Path.Combine( root, "art.png" );
		File.WriteAllBytes( art, new byte[] { 1, 2, 3 } );
		try
		{
			var game = GameFolder.At( root );
			var card = CardParts.NewCard( "Fire" );
			card.Name = "Ember Test";
			card.Artwork = art;
			var result = game.Publish( card );
			Assert.Equal( Path.Combine( game.CardsFolder, "fire", "ember_test.cscard" ), result.CardFile );
			var saved = CardFile.Load( result.CardFile );
			Assert.Equal( 42, saved.Id );
			Assert.Equal( "Fire", saved.Collection );
			Assert.False( saved.IsCustom );
			Assert.True( saved.IsPlayable );
			Assert.Equal( "cards/art/ember_test.png", saved.Artwork );
			Assert.NotNull( game.Resolve( saved.Artwork ) );
			Assert.True( result.ExportListFixed );
			Assert.Contains( "cards/*", File.ReadAllText( Path.Combine( root, GameFolder.ProjectFile ) ) );

			// Publishing it again from where it lives keeps its ID and file.
			var again = game.Publish( saved, result.CardFile );
			Assert.Equal( 42, again.Card.Id );
			Assert.Equal( result.CardFile, again.CardFile );
		}
		finally
		{
			Directory.Delete( root, true );
		}
	}

	[Fact]
	public void CollectionsAndIdsFollowTheGame()
	{
		var spirit = CardParts.NewCard();
		spirit.Types = new() { "Spirit" };
		Assert.Equal( "Spirit", GameFolder.CollectionFor( spirit ) );
		Assert.Equal( "Light", GameFolder.CollectionFor( CardParts.NewCard( "Sun" ) ) );
		var cards = Game.LoadCards().Select( c => c.Card ).ToList();
		var next = GameFolder.NextId( CardParts.NewCard(), cards );
		Assert.DoesNotContain( cards, c => c.Id == next );
		Assert.True( next < 1000 );
		Assert.True( GameFolder.NextId( spirit, cards ) > 10000 );
	}
}
