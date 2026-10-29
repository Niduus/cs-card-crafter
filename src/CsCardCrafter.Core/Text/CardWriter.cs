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
		EffectSection.Keyword, EffectSection.Considered, EffectSection.FusionMaterial, EffectSection.Hand, EffectSection.Field,
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
		var effects = card.Effects.OrderBy( e => Array.IndexOf( Order, e.Section ) ).ToList();
		foreach ( var effect in effects )
		{
			var text = keepTyped && !string.IsNullOrWhiteSpace( effect.Text ) ? effect.Text.Trim() : Suggest( card, effect );
			if ( text.Length > 0 )
				lines.Add( text );
		}
		if ( !card.Effects.Any( e => e.Section == EffectSection.ImportantTag ) && EffectWriter.UsageLine( card ) is { Length: > 0 } usage )
			lines.Add( usage );
		return string.Concat( lines.Select( l => "{BEG}" + l + "{END}" ) );
	}

	/// <summary>Writes every effect's suggested text into the effect and the card's description.</summary>
	public static void Apply( CardDefinition card )
	{
		foreach ( var effect in card.Effects.Where( e => e.HasOperations ) )
			effect.Text = Suggest( card, effect );
		card.Description = Description( card );
	}
}
