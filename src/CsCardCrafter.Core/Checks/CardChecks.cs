using ChaosSpells.Cards;
using ChaosSpells.Rules;
using CsCardCrafter.Core.Cards;

namespace CsCardCrafter.Core.Checks;

public enum CheckLevel { Problem, Warning }

public sealed record CheckResult( CheckLevel Level, string Message );

/// <summary>
/// What the crafter warns about before a card goes into the game: the deck rules (Spirit points, copy limits), the face
/// (art, name, costs) and effects whose blocks the game doesn't know.
/// </summary>
public static class CardChecks
{
	public static List<CheckResult> Run( CardDefinition card, GameFolder game = null )
	{
		var results = new List<CheckResult>();
		void Problem( string m ) => results.Add( new( CheckLevel.Problem, m ) );
		void Warn( string m ) => results.Add( new( CheckLevel.Warning, m ) );

		if ( string.IsNullOrWhiteSpace( card.Name ) )
			Problem( "The card has no name." );
		if ( card.Types.Count == 0 )
			Problem( "Pick a card type." );
		if ( card.IsDev )
			Warn( "Dev cards never reach play (decks and random deals skip them)." );
		if ( card.Id <= 0 )
			Warn( "The card has no ID yet, so the game treats it as unfinished and keeps it out of play. Give it an ID above 0." );
		if ( card.Costs.Count > 3 )
			Problem( "The card face shows at most 3 costs." );
		foreach ( var cost in card.Costs.Where( c => string.IsNullOrWhiteSpace( c.Amount ) ) )
			Warn( $"A {CardParts.CostOf( cost ).Label} cost has no amount. Write 0 for a free card (0 still shows on the face)." );
		if ( card.IsSpirit && card.SpiritPoints is < 1 or > 6 )
			Problem( "A Spirit card needs 1 to 6 Spirit points (a Spirit Deck holds at most 6 cards and 6 points)." );
		if ( card.IsSpirit && card.MaxCopies != 1 )
			Warn( "Spirit Decks take 1 copy of each Spirit card; set the copy limit to 1." );
		if ( card.MaxCopies < 1 && !card.IsToken )
			Warn( "A copy limit of 0 means the card can't go in a deck." );
		if ( card.HasType( "Damage" ) && string.IsNullOrWhiteSpace( card.Combat ) )
			Warn( "Damage cards deal damage in Combat; give it a Combat value." );
		if ( card.Effects.Count( e => e.Section == EffectSection.Combat ) > 1 )
			Warn( "The card has more than one Combat effect; printed cards have one Combat line." );

		if ( string.IsNullOrWhiteSpace( card.Artwork ) )
			Warn( "No artwork yet." );
		else if ( game is not null && game.Resolve( card.Artwork ) is null && !System.IO.File.Exists( card.Artwork ) )
			Warn( $"The artwork {card.Artwork} isn't in the game folder." );

		for ( var i = 0; i < card.Effects.Count; i++ )
		{
			var effect = card.Effects[i];
			foreach ( var slot in Enum.GetValues<EffectSlot>() )
				foreach ( var op in effect.GetSlot( slot ) )
				{
					var block = BlockRegistry.Get( op.Op );
					if ( block is null )
						Problem( $"Effect {i + 1}: the game has no block called {op.Op}." );
					else if ( block.Slot != slot && !(block.Slot == EffectSlot.Then && slot == EffectSlot.If) && !(block.Slot == EffectSlot.If && slot == EffectSlot.Then) )
						Warn( $"Effect {i + 1}: \"{block.Name}\" is a {block.Slot.ToString().ToUpperInvariant()} block but sits in {slot.ToString().ToUpperInvariant()}." );
				}
			if ( effect.Section is not (EffectSection.Flavor or EffectSection.ImportantTag or EffectSection.Considered or EffectSection.FusionMaterial)
				&& !effect.HasOperations && effect.Section != EffectSection.Combat )
				Warn( $"Effect {i + 1} ({effect.Section}) has text but no blocks, so it won't do anything in a duel yet." );
		}

		if ( card.Keywords.Any( k => CardKeywords.Name( k ) is null ) )
			Warn( "Unknown keyword: " + string.Join( ", ", card.Keywords.Where( k => CardKeywords.Name( k ) is null ) ) + "." );
		return results;
	}
}
