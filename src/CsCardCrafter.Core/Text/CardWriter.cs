using ChaosSpells.Cards;

namespace CsCardCrafter.Core.Text;

/// <summary>
/// The whole card's suggested face text: every effect written from its blocks in {BEG}…{END} paragraphs, as the game's
/// <see cref="CardDefinition.Description"/> stores them, keyword lines first and the once-per line last.
/// Effects whose text was typed by hand keep it (oracle law: the designer's wording wins).
/// </summary>
public static class CardWriter
{
	/// <summary>Sections in the order printed cards list them.</summary>
	static readonly EffectSection[] Order =
	{
		EffectSection.Keyword, EffectSection.Considered, EffectSection.FusionMaterial, EffectSection.Hand, EffectSection.Field, EffectSection.Battlefield,
		EffectSection.Combat, EffectSection.Attachment, EffectSection.Persistent, EffectSection.Abyss, EffectSection.Void,
		EffectSection.ImportantTag, EffectSection.Flavor,
	};

	/// <summary>The text an effect would get from its blocks alone, or its own text when it has no blocks.</summary>
	public static string Suggest( CardDefinition card, CardEffect effect )
	{
		var written = effect.HasOperations ? EffectWriter.Write( card, effect ) : "";
		return written.Length > 0 ? written : effect.Text ?? "";
	}

	/// <summary>The description the card would print: each effect's text (typed or suggested) and the once-per line.</summary>
	public static string Description( CardDefinition card, bool keepTyped = true )
	{
		var lines = new List<string>();
		// Innate conditions ("Activate only if ...") lead the card, as printed cards have them.
		// "Choose one" options and Coin Flip branches print inside their choice, not as lines of their own.
		var effects = card.Effects.Where( e => e.Section != EffectSection.Option )
			.OrderBy( e => EffectWriter.IsInnate( e ) ? -1 : Array.IndexOf( Order, e.Section ) ).ToList();
		// Name-wide limits ('You can only activate 1 "Spawn" card per round.') join the once-per end note in one sentence.
		var limits = new List<string>();
		foreach ( var effect in effects )
		{
			var text = keepTyped && !string.IsNullOrWhiteSpace( effect.Text ) ? effect.Text.Trim() : Suggest( card, effect );
			if ( effect.Section == EffectSection.ImportantTag && EffectWriter.IsFamilyLimit( effect ) )
			{
				if ( text.Length > 0 && !limits.Contains( text ) )
					limits.Add( text );
				continue;
			}
			// Two effects that share one text ("X. If Enhanced: instead Y." is X when not Enhanced and Y when it is) print once.
			if ( text.Length > 0 && !lines.Contains( text ) )
				lines.Add( text );
		}
		if ( EffectWriter.UsageLine( card ) is { Length: > 0 } usage )
			limits.Add( usage );
		if ( EffectWriter.JoinLimits( limits ) is { Length: > 0 } limit )
			lines.Add( limit );
		// Every keyword the card has goes in its keyword line (Justin, 2026-11-03).
		return KeywordLine.Ensure( card, string.Concat( lines.Select( l => "{BEG}" + l + "{END}" ) ) );
	}

	/// <summary>Writes every effect's suggested text into the effect and the card's description.</summary>
	public static void Apply( CardDefinition card )
	{
		// Option effects keep their text: it is how their choice finds them.
		foreach ( var effect in card.Effects.Where( e => e.HasOperations && e.Section != EffectSection.Option ) )
			effect.Text = Suggest( card, effect );
		card.Description = Description( card );
	}
}
