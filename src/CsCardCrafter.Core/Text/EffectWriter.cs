using ChaosSpells.Cards;
using ChaosSpells.Rules;

namespace CsCardCrafter.Core.Text;

/// <summary>
/// Writes an effect's card text from its blocks, the way YGO Card Crafter suggests text, but in Chaos Spells wording
/// (oracle.md and the game's existing cards):
/// <list type="bullet">
/// <item>Combat: "Combat: 650 Storm damage to target enemy, Then you gain 2 250 damage Blades, Also ..." in one sentence,
/// Then / Also / Finally in that order.</item>
/// <item>Keyword: the ALWAYS blocks as keyword lines ("Lifesteal: 10%.").</item>
/// <item>Others: "[WHEN], [IF]: [COST]: [THEN]." ("When this card is sent to the Abyss, if your mastery is Death: you
/// draw 2."), with "You may" / "While this card is in the Abyss, you can" for effects you activate.</item>
/// </list>
/// </summary>
public static class EffectWriter
{
	static readonly string[] Connectors = { "Then", "Also", "Finally" };

	public static string Write( CardDefinition card, CardEffect effect )
	{
		if ( effect is null )
			return "";
		return effect.Section switch
		{
			EffectSection.Combat => Combat( card, effect ),
			EffectSection.Keyword => Sentences( effect.Always ),
			_ => Activated( effect ),
		};
	}

	/// <summary>The card's Combat line: its Combat value and target from the Card tab, ALWAYS combat bonuses, then the THEN blocks.</summary>
	static string Combat( CardDefinition card, CardEffect effect )
	{
		var mastery = CombatMastery( card ) is { Length: > 0 } m ? m + " " : "";
		var target = card?.CombatTarget switch
		{
			ChaosSpells.Cards.CombatTarget.Self => "yourself",
			ChaosSpells.Cards.CombatTarget.Enemies => "all enemies",
			ChaosSpells.Cards.CombatTarget.Ally => "target ally",
			ChaosSpells.Cards.CombatTarget.Friends => "Allies",
			_ => "target enemy",
		};
		var value = string.IsNullOrWhiteSpace( card?.Combat ) ? "X" : card.Combat.Trim();
		var text = $"Combat: {value} {mastery}damage to {target}";
		foreach ( var bonus in effect.Always.Select( OracleText.Of ).Where( t => t.Length > 0 ) )
			text += bonus.StartsWith( '(' ) ? " " + bonus : ", " + bonus;
		return Finish( text + Chain( effect.Then ) );
	}

	/// <summary>
	/// The Mastery a card's Combat hits in: its own, or for Duo cards the secondary Duo Mastery (the game's
	/// DuoMasteryRules). Empty for Masterless and unknown Masteries.
	/// </summary>
	public static string CombatMastery( CardDefinition card )
	{
		if ( card is null )
			return "";
		var name = card.DuoMasteries.Count > 0 ? card.DuoMasteries[^1] : card.Mastery;
		var info = Masteries.Get( name );
		return info.Name == Masteries.Masterless ? "" : info.Name;
	}

	/// <summary>An effect that starts on an event or that the player activates: lead, conditions, cost, then what it does.</summary>
	static string Activated( CardEffect effect )
	{
		var when = effect.When.Where( w => w.Op != "OP_Activate" ).Select( w => OracleText.Of( w ).TrimEnd( ':' ) ).Where( t => t.Length > 0 ).ToList();
		var activated = effect.When.Any( w => w.Op == "OP_Activate" );
		var conditions = effect.If.Select( OracleText.Of ).Where( t => t.Length > 0 ).Select( Condition ).ToList();
		var costs = effect.Cost.Select( c => OracleText.Of( c ).TrimEnd( ':' ) ).Where( t => t.Length > 0 ).ToList();

		var lead = string.Join( " or ", when );
		if ( activated && effect.Section is EffectSection.Abyss or EffectSection.Void )
			lead = Join( lead, $"While this card is in the {effect.Section}" );
		if ( conditions.Count > 0 )
			lead = Join( lead, lead.Length == 0 ? OracleText.Capitalize( string.Join( ", ", conditions ) ) : string.Join( ", ", conditions ) );

		var cost = string.Join( " and ", costs );
		if ( cost.Length > 0 && activated )
			cost = (lead.Length == 0 ? "You may " : "you can ") + OracleText.Continue( cost );

		var head = Join( lead, cost );
		var body = Chain( effect.Then, first: true ).TrimStart( ',', ' ' );
		var lasting = Sentences( effect.Always );
		var text = head.Length > 0 && body.Length > 0 ? $"{head}: {OracleText.Continue( body )}" : head + body;
		if ( text.Length > 0 && lasting.Length > 0 )
			return Finish( text ) + " " + lasting;
		return text.Length > 0 ? Finish( text ) : lasting;
	}

	/// <summary>A condition reads as an "if" clause unless it already sets its own timing ("While...", "During...", "If Enhanced:").</summary>
	static string Condition( string text )
	{
		text = text.TrimEnd( ':' );
		var first = text.Split( ' ', 2 )[0];
		return first is "if" or "If" or "While" or "while" or "During" or "during" or "only" or "Only" ? text : "if " + OracleText.Continue( text );
	}

	/// <summary>THEN blocks as one sentence: the first as written (or after ", Then"), the rest after Also and Finally.</summary>
	static string Chain( IEnumerable<OperationBlock> blocks, bool first = false ) => Chain( blocks.Select( OracleText.Of ), first );

	static string Chain( IEnumerable<string> texts, bool first )
	{
		var parts = texts.Where( t => t.Length > 0 ).ToList();
		// A gate such as "If your mastery is Fire:" ends the sentence; what follows is a new one under that gate.
		var gate = parts.FindIndex( p => p.EndsWith( ':' ) );
		if ( gate >= 0 )
		{
			var before = Chain( parts.Take( gate ), first );
			var after = Chain( parts.Skip( gate + 1 ), true ).TrimStart( ',', ' ' );
			var sentence = $"{OracleText.Capitalize( parts[gate] )} {OracleText.Continue( after )}".Trim();
			return before.Length == 0 ? (first ? sentence : ", Then " + sentence) : Finish( before ) + " " + sentence;
		}
		var text = "";
		for ( var i = 0; i < parts.Count; i++ )
		{
			if ( first && i == 0 )
			{
				text = OracleText.Capitalize( parts[0] );
				continue;
			}
			var n = first ? i - 1 : i;
			// Then, Also, Finally; anything past three joins the last one with "and" rather than stacking connectors.
			text += n < Connectors.Length - 1 || (n == Connectors.Length - 1 && i == parts.Count - 1)
				? $", {Connectors[Math.Min( n, Connectors.Length - 1 )]} {OracleText.Continue( parts[i] )}"
				: $", and {OracleText.Continue( parts[i] )}";
		}
		return text;
	}

	/// <summary>Lasting (ALWAYS) blocks, one sentence each.</summary>
	static string Sentences( IEnumerable<OperationBlock> blocks ) =>
		string.Join( " ", blocks.Select( OracleText.Of ).Where( t => t.Length > 0 ).Select( t => Finish( OracleText.Capitalize( t ) ) ) );

	static string Join( string a, string b ) => a.Length == 0 ? b : b.Length == 0 ? a : $"{a}, {b}";

	/// <summary>Ends the sentence with one full stop (a sentence ending in ":" or ")" keeps a stop too).</summary>
	static string Finish( string text )
	{
		text = text.Trim().TrimEnd( ',', ';' );
		if ( text.Length == 0 ) return text;
		if ( text.EndsWith( ':' ) ) text = text.TrimEnd( ':' );
		return text.EndsWith( '.' ) || text.EndsWith( '!' ) ? text : text + ".";
	}

	/// <summary>
	/// The Important Tag line for once-per limits, as the game's cards print them ("You can only activate this card's
	/// Field effect once per turn and its Abyss effect once per Conflict."). Empty when no effect has a limit.
	/// </summary>
	public static string UsageLine( CardDefinition card )
	{
		var limited = card.Effects
			.Where( e => e.Usage is EffectUsage.OncePerTurn or EffectUsage.OncePerRound or EffectUsage.OncePerConflict )
			.Select( e => $"{(e.Section == EffectSection.Hand ? "Hand" : e.Section.ToString())} effect once per {e.Usage switch { EffectUsage.OncePerTurn => "turn", EffectUsage.OncePerRound => "round", _ => "Conflict" }}" )
			.Distinct()
			.ToList();
		if ( limited.Count == 0 )
			return "";
		var parts = limited.Select( ( l, i ) => i == 0 ? "this card's " + l : "its " + l ).ToList();
		var list = parts.Count == 1 ? parts[0] : string.Join( ", ", parts.Take( parts.Count - 1 ) ) + " and " + parts[^1];
		return $"You can only activate {list}.";
	}
}
