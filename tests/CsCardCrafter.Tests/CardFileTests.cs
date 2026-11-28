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

	/// <summary>
	/// Every playable game card is built the way the crafter builds cards: blocks the game knows in its slots, and an
	/// Option effect for each "Choose one" option, so opening any of them in the crafter shows how it works.
	/// </summary>
	[Fact]
	public void GameCardsAreBuiltFromBlocks()
	{
		var playable = Game.LoadCards().Select( c => c.Card ).Where( c => c.IsPlayable && !c.IsToken ).ToList();
		var unbuilt = playable.Where( c => !c.Effects.Any( e => e.HasOperations ) && c.Effects.Any( e => e.Section is not (EffectSection.Combat or EffectSection.Flavor) ) ).Select( c => c.Name ).ToList();
		Assert.True( unbuilt.Count == 0, "No blocks: " + string.Join( ", ", unbuilt ) );
		var problems = playable.SelectMany( c => CardChecks.Run( c ).Where( r => r.Level == CheckLevel.Problem && (r.Message.Contains( "no block" ) || r.Message.Contains( "Option effect" )) ).Select( r => $"{c.Name}: {r.Message}" ) ).ToList();
		Assert.True( problems.Count == 0, string.Join( "; ", problems.Take( 10 ) ) );
	}

	/// <summary>
	/// Siaenar (Justin's redesign, 2026-11-20): the new blocks write the card's own words, and its field effect's recipe
	/// lists the Fusion cards it may cast but never an Exclusive Fusion Cast one.
	/// </summary>
	[Fact]
	public void SiaenarsNewBlocksWriteItsText()
	{
		var cards = Game.LoadCards().Select( c => c.Card ).ToList();
		var si = cards.First( c => c.Name == "Siaenar, Protector of Spawns" );
		var hand = si.Effects.First( e => e.Section == EffectSection.Hand );
		var field = si.Effects.First( e => e.Section == EffectSection.Field );
		Assert.Contains( "Exclusive Fusion Cast: Reveal this card and 2 other \"Spawn\" cards in your hand, Fusion Cast this card", CsCardCrafter.Core.Text.EffectWriter.Write( si, hand ) );
		Assert.Contains( "other Fusion cards you Cast or Activate have their effects negated", CsCardCrafter.Core.Text.EffectWriter.Write( si, hand ) );
		var written = CsCardCrafter.Core.Text.EffectWriter.Write( si, field );
		Assert.Contains( "Void this card and its Attachments", written );
		Assert.Contains( "Fusion Cast 1 non-Exclusive Fusion Cast card from your hand or Side Deck, using cards in your hand as materials", written );
		var combat = si.Effects.First( e => e.Section == EffectSection.Combat ).Always[0];
		Assert.Equal( "+75 per card in every Abyss", CsCardCrafter.Core.Text.OracleText.Of( combat ) );
		var recipe = ChaosSpells.Rules.FusionRecipe.For( si, field.Then[0] );
		Assert.Contains( recipe.Ways, w => w[0].Text.StartsWith( "Noxius-Impure" ) );
		Assert.DoesNotContain( recipe.Ways, w => w[0].Text.StartsWith( "Siaenar" ) );
	}

	/// <summary>
	/// Kitharas Was Silenced (Justin's design, 2026-11-20): an Aura's activated effect says it works from the Aura, and the
	/// Evade Cast and "gains a keyword" blocks write the card's own words, word for word.
	/// </summary>
	[Fact]
	public void KitharasWasSilencedWritesItsText()
	{
		var card = Game.LoadCards().Select( c => c.Card ).First( c => c.Name == "Kitharas Was Silenced" );
		foreach ( var effect in card.Effects.Where( e => e.Section is EffectSection.Field or EffectSection.Abyss or EffectSection.Keyword ) )
			Assert.Equal( effect.Text, CsCardCrafter.Core.Text.EffectWriter.Write( card, effect ) );
		Assert.Equal( "While this card is your Aura, you can Evade Cast 1 Select non-Fusion Damage type \"Kitharas\" card from your hand, then it gains Final Will.",
			CsCardCrafter.Core.Text.EffectWriter.Write( card, card.Effects.First( e => e.Section == EffectSection.Field ) ) );
		Assert.Equal( "While this card is in your Abyss, you can Void this card: Evade Cast 1 target \"Kitharas\" card with a listed cost of 7 Light Mana from your Abyss, then it gains Anchored 3 Rounds.",
			CsCardCrafter.Core.Text.EffectWriter.Write( card, card.Effects.First( e => e.Section == EffectSection.Abyss ) ) );
		// The once-per limit is the end note (Justin, 2026-11-21).
		Assert.Equal( "You can only activate this card's Aura and Abyss effects once per turn.", CsCardCrafter.Core.Text.EffectWriter.UsageLine( card ) );
		Assert.Equal( card.Description.Replace( "\r\n", "\n" ).Replace( "}\n{", "}{" ), CsCardCrafter.Core.Text.CardWriter.Description( card ) );
	}

	/// <summary>
	/// Carnage (Justin's design, 2026-11-27): "While this card is your Aura", "in your Abyss", the hand-to-Abyss trade with
	/// "and if you do", the "hand or Deck" Evade Cast, and a once per Conflict end note shared by name.
	/// </summary>
	[Fact]
	public void CarnageWritesItsText()
	{
		var card = Game.LoadCards().Select( c => c.Card ).First( c => c.Name == "Carnage" );
		foreach ( var effect in card.Effects )
			Assert.Equal( effect.Text, CsCardCrafter.Core.Text.EffectWriter.Write( card, effect ) );
		Assert.Equal( "You can only activate \"Carnage\"'s Aura and Abyss effects once per Conflict.", CsCardCrafter.Core.Text.EffectWriter.UsageLine( card ) );
		Assert.Equal( card.Description.Replace( "\r\n", "\n" ).Replace( "}\n{", "}{" ), CsCardCrafter.Core.Text.CardWriter.Description( card ) );
	}

	/// <summary>
	/// The All-Inspiring Nyxara (Justin's design, 2026-11-27): Fusion materials of each name from the hand or field, the
	/// "when an enemy targets you" response with its detach from another Fusion, a Combat of two damage types, and the
	/// Abyss effect's "another" condition and shuffle cost; the end note mixes once per turn and once per Conflict.
	/// </summary>
	[Fact]
	public void AllInspiringNyxaraWritesItsText()
	{
		var card = Game.LoadCards().Select( c => c.Card ).First( c => c.Name == "The All-Inspiring Nyxara" );
		foreach ( var effect in card.Effects )
			Assert.Equal( effect.Text, CsCardCrafter.Core.Text.EffectWriter.Write( card, effect ) );
		Assert.Equal( card.Description.Replace( "\r\n", "\n" ).Replace( "}\n{", "}{" ), CsCardCrafter.Core.Text.CardWriter.Description( card ) );
	}

	/// <summary>
	/// Emergency Landing (Justin's design, 2026-11-27): the "your HP is 33% or less" innate condition, the legal Evade
	/// Cast from several places, Frontline with Final Will in one breath, and the Conflict-long "Dragon" lock.
	/// </summary>
	[Fact]
	public void EmergencyLandingWritesItsText()
	{
		var card = Game.LoadCards().Select( c => c.Card ).First( c => c.Name == "Emergency Landing" );
		foreach ( var effect in card.Effects )
			Assert.Equal( effect.Text, CsCardCrafter.Core.Text.EffectWriter.Write( card, effect ) );
		Assert.Equal( card.Description.Replace( "\r\n", "\n" ).Replace( "}\n{", "}{" ), CsCardCrafter.Core.Text.CardWriter.Description( card ) );
	}

	/// <summary>
	/// Spawn hand effect (Justin approved, 2026-11-23): an "if this card is in your hand" condition on a Hand effect you
	/// activate leads as "While this card is in your hand", and the once per Conflict limit is the end note.
	/// </summary>
	[Theory]
	[InlineData( "Spawn of Ice", "Ice" )]
	[InlineData( "Spawn of Spectre", "Spectre" )]
	[InlineData( "Spawn of Death", "Death" )]
	[InlineData( "Spawn of Light", "Light" )]
	public void SpawnHandEffectWritesItsText( string name, string mastery )
	{
		var card = Game.LoadCards().Select( c => c.Card ).First( c => c.Name == name );
		var hand = card.Effects.First( e => e.Section == EffectSection.Hand );
		Assert.Equal( $"While this card is in your hand, you can pay 3 Light Mana and discard this card: Select and Draw 1 {mastery} card from your Main Deck.",
			CsCardCrafter.Core.Text.EffectWriter.Write( card, hand ) );
		Assert.Equal( hand.Text, CsCardCrafter.Core.Text.EffectWriter.Write( card, hand ) );
		Assert.Equal( "You can only activate this card's Hand effect once per Conflict.", CsCardCrafter.Core.Text.EffectWriter.UsageLine( card ) );
		Assert.Equal( card.Description.Replace( "\r\n", "\n" ).Replace( "}\n{", "}{" ), CsCardCrafter.Core.Text.CardWriter.Description( card ) );
		// The name-wide limit and the once-per end note print as one sentence (Justin, 2026-11-28).
		Assert.EndsWith( "{BEG}You can only activate 1 \"Spawn\" card per round, and this card's Hand effect once per Conflict.{END}",
			CsCardCrafter.Core.Text.CardWriter.Description( card ) );
	}

	/// <summary>
	/// The Kitharas combo redesign (Justin approved, 2026-11-22): every effect of the rebuilt cards writes the approved
	/// words from its blocks, and the once-per end note names Battlefield effects.
	/// </summary>
	[Theory]
	[InlineData( "Kitharas Mordiael", "You can only activate this card's Hand and Battlefield effects once per turn." )]
	[InlineData( "Kitharas Tribute", "You can only activate this card and its Abyss effect once per turn." )]
	[InlineData( "Kitharas Sentence", "You can only activate this card once per turn." )]
	[InlineData( "Kitharas Hexation", "You can only activate this card once per turn." )]
	[InlineData( "Kitharas Devourer", "You can only activate this card's Battlefield effect once per turn." )]
	[InlineData( "Kitharas Andromeda", "You can only activate this card's Hand and Battlefield effects once per turn." )]
	[InlineData( "Kitharas Consumer", "" )]
	[InlineData( "Lumeria of Kitharas", "You can only activate this card's Abyss effect once per turn." )]
	[InlineData( "Crimes Against Kitharas", "You can only activate this card once per turn." )]
	public void KitharasRedesignWritesItsText( string name, string usage )
	{
		var card = Game.LoadCards().Select( c => c.Card ).First( c => c.Name == name );
		// Hand, Abyss and Persistent effects keep their live wording ("You can Void this card from your Abyss: ..."), typed by hand.
		foreach ( var effect in card.Effects.Where( e => e.HasOperations && e.Section is not (EffectSection.Keyword or EffectSection.Hand or EffectSection.Abyss or EffectSection.Persistent) ) )
			Assert.Equal( effect.Text, CsCardCrafter.Core.Text.EffectWriter.Write( card, effect ) );
		Assert.Equal( usage, CsCardCrafter.Core.Text.EffectWriter.UsageLine( card ) );
	}

	/// <summary>An option's Option effect never prints as a line of its own, and two effects sharing one text print it once.</summary>
	[Fact]
	public void OptionsAndSharedTextPrintOnce()
	{
		var cards = Game.LoadCards().Select( c => c.Card ).ToList();
		var headstart = cards.First( c => c.Name == "Headstart" );
		var text = CsCardCrafter.Core.Text.CardWriter.Description( headstart );
		Assert.Equal( 1, System.Text.RegularExpressions.Regex.Matches( text, "draw 1 card now" ).Count );
		var paired = cards.First( c => c.Effects.GroupBy( e => e.Text ).Any( g => g.Count() == 2 && g.Key.Length > 0 ) );
		var shared = paired.Effects.GroupBy( e => e.Text ).First( g => g.Count() == 2 && g.Key.Length > 0 ).Key.Trim();
		Assert.Equal( 1, System.Text.RegularExpressions.Regex.Matches( CsCardCrafter.Core.Text.CardWriter.Description( paired ), System.Text.RegularExpressions.Regex.Escape( shared ) ).Count );
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

	/// <summary>"Also considered" statements join in one sentence (Justin, 2026-11-28).</summary>
	[Fact]
	public void NyxaraConsideredLinesJoin()
	{
		var card = Game.LoadCards().Select( c => c.Card ).First( c => c.Name == "The All-Inspiring Nyxara" );
		var considered = card.Effects.First( e => e.Section == EffectSection.Considered && e.HasOperations );
		Assert.Equal( "This card is also a \"Knight\" card, and is Embraced and Light.", CsCardCrafter.Core.Text.EffectWriter.Write( card, considered ) );
		Assert.StartsWith( "{BEG}This card is also a \"Knight\" card, and is Embraced and Light.{END}{BEG}You can Fusion Cast", CsCardCrafter.Core.Text.CardWriter.Description( card ) );
		// A blockless Considered line beside a block one writes nothing: the block line carries both.
		card.Effects.Insert( 1, new CardEffect { Section = EffectSection.Considered } );
		Assert.Equal( "", CsCardCrafter.Core.Text.EffectWriter.Write( card, card.Effects[1] ) );
		Assert.StartsWith( "{BEG}This card is also a \"Knight\" card, and is Embraced and Light.{END}{BEG}You can Fusion Cast", CsCardCrafter.Core.Text.CardWriter.Description( card, keepTyped: false ) );
	}
}
