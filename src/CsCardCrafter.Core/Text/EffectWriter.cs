using System.Text.RegularExpressions;
using ChaosSpells.Cards;
using ChaosSpells.Rules;

namespace CsCardCrafter.Core.Text;

/// <summary>
/// Writes an effect's card text from its blocks, the way YGO Card Crafter suggests text, but in Chaos Spells wording
/// (oracle.md and the game's existing cards):
/// <list type="bullet">
/// <item>Combat: "Combat: 550 Ice damage and one -400 damage Mantle to target enemy; you gain one 1050 Shield." in one
/// sentence: what the target gets in one list, the rest after a semicolon or ", then".</item>
/// <item>Keyword: the ALWAYS blocks as keyword lines ("Lifesteal: 10%.").</item>
/// <item>Others: "[WHEN], [IF]: [COST]: [THEN]." ("When this card is sent to the Abyss, if your mastery is Death: you
/// draw 2."), with "You may" / "While this card is in the Abyss, you can" for effects you activate.</item>
/// </list>
/// </summary>
public static class EffectWriter
{
	static readonly string[] Connectors = { "then", "also", "finally" };

	public static string Write( CardDefinition card, CardEffect effect )
	{
		if ( effect is null )
			return "";
		return effect.Section switch
		{
			EffectSection.Combat => Combat( card, effect ),
			EffectSection.Keyword => KeywordList( effect.Always ),
			_ when IsInnate( effect ) => Innate( effect ),
			_ when IsLasting( effect ) => Lasting( card, effect ),
			_ => Limited( effect, Activated( card, effect ) ),
		};
	}

	/// <summary>"Once per turn", "Once per round", "Once per Conflict", or empty for an effect with no once-per limit.</summary>
	public static string Limit( CardEffect effect ) => effect.Usage switch
	{
		EffectUsage.OncePerTurn => "Once per turn",
		EffectUsage.OncePerRound => "Once per round",
		EffectUsage.OncePerConflict => "Once per Conflict",
		_ => "",
	};

	/// <summary>
	/// Once-per limits go on the card's end note, "You can only activate this card's Aura and Abyss effects once per
	/// turn." (Justin, 2026-11-21), not in front of each effect; see <see cref="UsageLine"/>. A limit shared by name
	/// (one "Spawn" card per round) stays its own line.
	/// </summary>
	static string Limited( CardEffect effect, string text ) => text;

	/// <summary>
	/// The card's Combat line (card-description-revamps A1, 2026-11-20): its Combat value and target from the Card tab,
	/// then the THEN blocks. Everything the Combat's target also gets joins one list ("Combat: 550 Ice damage and one -400
	/// damage Mantle to target enemy"), a crowd control on them follows ("and Stun them for 1 round"), and the rest comes
	/// after a semicolon (or ", then" when nothing joined the list): "; you gain one 1050 Shield". ALWAYS combat blocks
	/// shape it: a Combat Mastery or True Damage replaces the damage word ("0 True Damage to target enemy"), "+N per ..."
	/// bonuses share one bracket ("(+100 per "Knight" card ... and +800 per ...)"), and an "(X = ...)" note ends the line.
	/// </summary>
	static string Combat( CardDefinition card, CardEffect effect )
	{
		var mastery = CombatMastery( card ) is { Length: > 0 } m ? m + " " : "";
		var damage = mastery + "damage";
		var bonuses = new List<string>();
		var inline = "";
		var notes = new List<string>();
		var combatNotes = new List<string>();
		foreach ( var op in effect.Always )
		{
			var bonus = OracleText.Of( op );
			if ( op.Op == "OP_TrueCombat" )
				damage = "True Damage";
			else if ( op.Op == "OP_CombatMastery" && BlockRegistry.Get( op.Op )?.Value( op, "Mastery" ) is { Length: > 0 } own )
				damage = (own == "Sun" ? "Light" : own) + " damage";
			else if ( bonus.Length == 0 )
				continue;
			else if ( bonus.StartsWith( "+X (", StringComparison.Ordinal ) )
				bonuses.Add( "+X. " + bonus[4..].TrimEnd( ')' ) );
			else if ( bonus.StartsWith( '+' ) )
				bonuses.Add( bonus );
			else if ( bonus.StartsWith( "(X =", StringComparison.Ordinal ) )
				combatNotes.Add( bonus.TrimEnd( '.' ) );
			else
				inline += bonus.StartsWith( '(' ) ? " " + bonus : ", " + bonus;
		}
		var who = card?.CombatTarget switch
		{
			ChaosSpells.Cards.CombatTarget.Self => Who.You,
			ChaosSpells.Cards.CombatTarget.Enemies => Who.AllEnemies,
			ChaosSpells.Cards.CombatTarget.Ally => Who.TargetAlly,
			ChaosSpells.Cards.CombatTarget.Friends => Who.Allies,
			_ => Who.TargetEnemy,
		};
		var target = who == Who.You ? "yourself" : who;
		var value = string.IsNullOrWhiteSpace( card?.Combat ) ? "X" : card.Combat.Trim();

		// What the Combat's own target also gets, and crowd control on them.
		var list = new List<string> { $"{value} {damage}" };
		var controls = new List<string>();
		var rest = new List<OperationBlock>();
		foreach ( var op in effect.Then )
		{
			if ( who != Who.You && OracleText.ItemWho( op ) == who && OracleText.Item( op ) is { Length: > 0 } item )
				list.Add( item );
			else if ( who != Who.You && op.Op == "OP_ApplyCrowdControl" && BlockRegistry.Get( op.Op )?.Value( op, "Who" ) == who )
				controls.Add( OracleText.CrowdControlOn( op, "them" ) );
			else
				rest.Add( op );
		}
		// An Overtime that grows with the defender's mana names its X at the end: "(X = 25 per Light Mana they have)".
		for ( var i = 1; i < list.Count; i++ )
		{
			var x = Regex.Match( list[i], @" (\(X = [^)]*\))$" );
			if ( !x.Success )
				continue;
			notes.Add( x.Groups[1].Value );
			list[i] = list[i][..x.Index];
		}
		// Bonuses and the X note sit on the damage: after it when other things join the list ("800 Fire damage (x2 if
		// their Mastery is Fire) and one 350 Trap to target enemy"), else after the target.
		var shape = (bonuses.Count > 0 ? " (" + string.Join( " and ", bonuses ) + ")" : "") + inline;
		foreach ( var x in combatNotes )
			shape += " " + x;
		if ( list.Count > 1 )
			list[0] += shape;
		var text = $"Combat: {Series( list )} to {target}";
		if ( list.Count == 1 )
			text += shape;
		// "and Stun them for 1 round" ("..., and Stun them" when the list already has an "and").
		foreach ( var control in controls )
			text += (list.Count > 1 ? ", and " : " and ") + control;
		var then = Rest( rest );
		if ( then.Length > 0 )
			text += then.StartsWith( ". " ) ? then : (list.Count > 1 || controls.Count > 0 ? "; " : ", then ") + then;
		text = Finish( text );
		return notes.Count > 0 ? Finish( text.TrimEnd( '.' ) + " " + string.Join( " ", notes ) ) : text;
	}

	/// <summary>"a", "a and b", "a, b and c".</summary>
	static string Series( IReadOnlyList<string> parts ) =>
		parts.Count == 1 ? parts[0] : string.Join( ", ", parts.Take( parts.Count - 1 ) ) + " and " + parts[^1];

	/// <summary>
	/// The Combat's other THEN blocks, joined with ", then": boons for one player share one list ("you gain one 400 Shield
	/// and one 300 damage Blade", "give one -560 damage Mantle and one 560 Trap to all enemies"). A gate ("If your Mastery
	/// is Fire or Life:"), a boon under an HP condition or a block that opens its own sentence starts a new sentence
	/// (the result then starts with ". ").
	/// </summary>
	static string Rest( List<OperationBlock> blocks )
	{
		var parts = new List<(string Text, bool Own)>();
		for ( var i = 0; i < blocks.Count; i++ )
		{
			var op = blocks[i];
			var who = OracleText.ItemWho( op );
			var item = OracleText.Item( op );
			var plain = OracleText.Of( op );
			if ( who.Length > 0 && item.Length > 0 && !(op.Op == "OP_GiveOverTime" && plain.StartsWith( "Give" ) && who is Who.You or Who.Allies) )
			{
				var items = new List<string> { item };
				while ( i + 1 < blocks.Count && OracleText.ItemWho( blocks[i + 1] ) == who && OracleText.Item( blocks[i + 1] ) is { Length: > 0 } next )
				{
					items.Add( next );
					i++;
				}
				var gains = who is Who.You or Who.Allies or Who.Everyone;
				var verb = who is Who.Everyone ? "gains" : "gain";
				parts.Add( (gains ? $"{(who == Who.You ? "you" : who)} {verb} {Series( items )}" : $"give {Series( items )} to {who}", false) );
				continue;
			}
			if ( plain.Length == 0 )
				continue;
			var own = plain.EndsWith( ':' ) || plain.StartsWith( "If ", StringComparison.Ordinal ) || plain.StartsWith( "Then, ", StringComparison.Ordinal );
			parts.Add( (plain, own) );
		}
		var text = "";
		for ( var i = 0; i < parts.Count; i++ )
		{
			var (part, own) = parts[i];
			if ( own )
				text += ". " + OracleText.Capitalize( part );
			else if ( i > 0 && parts[i - 1].Text.EndsWith( ':' ) )
				text += " " + AfterColon( part );
			else
				text += (i == 0 ? "" : ", then ") + OracleText.Continue( part );
		}
		return text;
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
		// Cards of no set Mastery hit in their caster's: "0 Mastery damage" (the Kitharas cards).
		if ( name == "Mastery" )
			return "Mastery";
		var info = Masteries.Get( name );
		return info.Name == Masteries.Masterless ? "" : info.Name;
	}

	/// <summary>
	/// An innate condition: a card-wide "Activate only if ..." line built from IF blocks alone (no WHEN, COST or THEN).
	/// If it doesn't hold, the card can't be activated (Justin, 2026-10-29: Second Breath's "Activate only if this card has
	/// 1 or more Spirit activations"). It sits in the Important Tag section and prints first.
	/// </summary>
	public static bool IsInnate( CardEffect effect ) =>
		effect.Section == EffectSection.ImportantTag && effect.If.Count > 0 && effect.When.Count + effect.Cost.Count + effect.Then.Count == 0;

	static string Innate( CardEffect effect )
	{
		var conditions = effect.If.Select( OracleText.Of ).Where( t => t.Length > 0 )
			.Select( t => System.Text.RegularExpressions.Regex.Replace( Condition( t ), "^(if|If) ", "" ) ).ToList();
		return conditions.Count == 0 ? "" : Finish( "Activate only if " + string.Join( " and ", conditions ) );
	}

	/// <summary>An effect made of ALWAYS blocks only: it holds while the card is where its section says.</summary>
	static bool IsLasting( CardEffect effect ) =>
		effect.Always.Count > 0 && effect.When.Count + effect.If.Count + effect.Cost.Count + effect.Then.Count == 0
		&& effect.Section is EffectSection.Field or EffectSection.Battlefield or EffectSection.Abyss or EffectSection.Void;

	/// <summary>
	/// "While this card is on the field, all non-"Kitharas" cards you control cannot ...": a lasting effect says where it
	/// holds, as printed cards do. Domains and Auras only ever work on the field, so theirs print bare ("You gain 50
	/// Fire passive damage."). A block that already sets its own timing ("While you control this card, ...") keeps it.
	/// </summary>
	static string Lasting( CardDefinition card, CardEffect effect )
	{
		var texts = effect.Always.Select( OracleText.Of ).Where( t => t.Length > 0 ).ToList();
		if ( texts.Count == 0 )
			return "";
		var bare = effect.Section == EffectSection.Field && card?.Types.Any( t => t is "Domain" or "PositiveAura" or "NegativeAura" or "Aura" ) == true;
		// "This card gains additional Masteries ..." is fixed at the Fusion Cast, not something that holds on the field.
		// A card rule that names its own subject ("This card can not declare Combat the turn it was Fusion Cast") prints bare.
		if ( bare || texts[0].StartsWith( "While ", StringComparison.OrdinalIgnoreCase ) || texts[0].StartsWith( "This card gains additional Masteries" )
			|| texts[0].StartsWith( "This card can", StringComparison.Ordinal ) || texts[0].StartsWith( "Its ", StringComparison.Ordinal ) )
			return Sentences( effect.Always );
		var where = effect.Section switch
		{
			EffectSection.Field => "on the field",
			// Only while the card itself is out, not attached (Justin, 2026-11-22).
			EffectSection.Battlefield => "on the Battlefield",
			_ => $"in the {effect.Section}",
		};
		var first = Finish( $"While this card is {where}, {OracleText.Continue( LowerFirst( texts[0] ) )}" );
		return string.Join( " ", new[] { first }.Concat( texts.Skip( 1 ).Select( t => Finish( OracleText.Capitalize( t ) ) ) ) );
	}

	/// <summary>Lower-cases a leading "You"/"Only"/"All" so the clause reads on after a comma; names and keywords keep theirs.</summary>
	static string LowerFirst( string text )
	{
		var first = text.Split( ' ', 2 )[0];
		return first is "You" or "Only" or "All" or "Your" or "If" ? char.ToLowerInvariant( text[0] ) + text[1..] : text;
	}

	/// <summary>An effect that starts on an event or that the player activates: lead, conditions, cost, then what it does.</summary>
	static string Activated( CardDefinition card, CardEffect effect )
	{
		if ( effect.Then.Count == 1 && effect.Then[0].Op == "OP_EnhanceCast" )
			return Finish( "You may " + OracleText.Of( effect.Then[0] ) );

		var when = effect.When.Where( w => w.Op != "OP_Activate" ).Select( w => OracleText.Of( w ).TrimEnd( ':' ) ).Where( t => t.Length > 0 ).ToList();
		var activated = effect.When.Any( w => w.Op == "OP_Activate" );
		var costs = effect.Cost.Select( c => OracleText.Of( c ).TrimEnd( ':' ) ).Where( t => t.Length > 0 ).ToList();

		// A condition on the card the trigger is about joins the trigger: "If this card is tributed from your hand or
		// field and is in your Abyss" (Justin, 2026-11-04), "If another card of yours is tributed and that card is in ...".
		var ifs = effect.If.ToList();
		var lead = string.Join( " or ", when );
		if ( when.Count == 1 && !activated )
		{
			var subject = TriggerSubject( effect.When.First( w => w.Op != "OP_Activate" ) );
			foreach ( var op in ifs.Where( op => op.Op == "OP_IfCardIsIn" ).ToList() )
			{
				var block = BlockRegistry.Get( op.Op );
				var which = block.Value( op, "Card" );
				var rest = Regex.Replace( OracleText.Of( op ), "^if " + Regex.Escape( which ) + " ", "" );
				lead += which == subject ? " and " + rest : $" and {which} {rest}";
				ifs.Remove( op );
			}
		}
		// "During the End Stage of the turn this card was Fusion Cast" (Kitharas Consumer): the condition names the turn.
		if ( effect.When.Count == 1 && effect.When[0].Op == "OP_WhenTurnEnds" && ifs.FirstOrDefault( op => op.Op == "OP_IfFusionCastThisTurn" ) is { } castTurn )
		{
			lead = "During the End Stage of the turn this card was Fusion Cast";
			ifs.Remove( castTurn );
			when.Clear();
		}
		// "When an enemy activates a card that Portrays or Assumes Fire": the Quick Cast's filter reads on from its trigger.
		var response = effect.When.Any( w => w.Op == "OP_QuickCastResponse" );
		foreach ( var op in ifs.Where( op => op.Op == "OP_IfTriggerIs" ).ToList() )
		{
			if ( response && OracleText.Of( op ) is { Length: > 0 } filter )
				lead += " " + filter;
			ifs.Remove( op );
		}
		// A Hand effect you activate that checks "if this card is in your hand" reads it as its precondition, the way
		// Abyss and Aura effects say where they work (Spawn cards, Justin 2026-11-23): "While this card is in your hand,
		// you can pay 3 Light Mana and discard this card: ...".
		var fromHand = false;
		if ( activated && effect.Section == EffectSection.Hand
			&& ifs.FirstOrDefault( op => op.Op == "OP_IfCardIsIn" && IsThisCardInHand( op ) ) is { } inHand )
		{
			ifs.Remove( inHand );
			fromHand = true;
		}
		var conditions = ifs.Select( OracleText.Of ).Where( t => t.Length > 0 ).Select( Condition ).ToList();
		if ( fromHand )
			lead = lead.Length == 0 ? "While this card is in your hand" : $"While this card is in your hand, {LowerWhen( lead )}";

		// A Field effect of a card that stays out, set off by something other than this card itself, says where it
		// listens: "While this card is on the field, whenever a Damage Overtime on an enemy increases".
		if ( effect.Section is EffectSection.Field or EffectSection.Battlefield && when.Count > 0 && !activated && !response && !Passing( card )
			&& effect.When.All( w => !SelfTriggers.Contains( w.Op ) && !IsSelfFusionCast( w ) || w.Op == "OP_WhenTributed" && TriggerSubject( w ) != CardRef.This ) )
			lead = $"While this card is {OnWhat( effect )}, {LowerWhen( lead )}";
		// A Battlefield effect you activate, or one that answers from the Battlefield, says where it works (Kitharas
		// Mordiael and Andromeda, game v0.8.52): "While this card is on the Battlefield, you can tribute it: ...".
		if ( effect.Section == EffectSection.Battlefield && (activated || response) )
			lead = lead.Length == 0 ? "While this card is on the Battlefield" : $"While this card is on the Battlefield, {LowerWhen( lead )}";
		// An Aura's activated effects work from the Aura zone and say so (Kitharas Was Silenced, Justin 2026-11-20):
		// "While this card is in your Aura, you can Evade Cast ...".
		var fromAura = effect.Section == EffectSection.Field && activated && IsAura( card );
		if ( fromAura )
			lead = lead.Length == 0 ? "While this card is in your Aura" : $"While this card is in your Aura, {LowerWhen( lead )}";
		// Effects that work from the Abyss or Void say so first, unless the trigger is this card arriving there.
		if ( effect.Section is EffectSection.Abyss or EffectSection.Void && !effect.When.Any( w => w.Op == "OP_WhenSentTo" ) )
		{
			var where = $"While this card is in the {effect.Section}";
			lead = lead.Length == 0 ? where : $"{where}, {LowerWhen( lead )}";
		}
		if ( conditions.Count > 0 )
			lead = Join( lead, lead.Length == 0 ? OracleText.Capitalize( string.Join( ", ", conditions ) ) : string.Join( ", ", conditions ) );

		// Costs are optional when you choose to activate, or to answer a trigger ("you can Void this card").
		var cost = string.Join( " and ", costs );
		// A Cast card's own "Tribute ...:" is its cost as printed; an effect you choose to activate says "you can tribute".
		if ( cost.Length > 0 && (activated || response || effect.Section is EffectSection.Abyss or EffectSection.Void) && (!cost.StartsWith( "Tribute", StringComparison.Ordinal ) || activated) )
			cost = (lead.Length == 0 ? "You may " : "you can ") + (cost.StartsWith( "Tribute", StringComparison.Ordinal ) ? "t" + cost[1..] : OracleText.Continue( cost ));

		var head = Join( lead, cost );
		if ( head.Length == 0 && activated && effect.Section == EffectSection.Hand && effect.Then.Count > 0 )
			head = "You can";
		var body = Chain( effect.Then, first: true ).TrimStart( ',', ' ' );
		var lasting = Sentences( effect.Always );
		// "If this card is tributed, Evade Cast this card": an "If ..." trigger with nothing to pay reads on with a comma,
		// as the game's cards print it (Kitharas Mordiael).
		var comma = !activated && costs.Count == 0 && conditions.Count == 0 && when.Count == 1 && lead.StartsWith( "If ", StringComparison.Ordinal )
			// "When this card arrives on the Battlefield, you can attach ..." (Kitharas Consumer): an optional step reads on with a comma.
			|| !activated && !response && costs.Count == 0 && conditions.Count == 0 && when.Count == 1 && lead.StartsWith( "When this card", StringComparison.Ordinal )
				&& body.StartsWith( "you can ", StringComparison.OrdinalIgnoreCase );
		// An optional effect with nothing to pay says "you can" before what it does ("While this card is in your Aura, you can ...").
		if ( fromAura && costs.Count == 0 && conditions.Count == 0 && head.Length > 0 && body.Length > 0 )
			return Finish( $"{head}, you can {OracleText.Continue( body )}" ) + (lasting.Length > 0 ? " " + lasting : "");
		var text = head == "You can" ? $"You can {OracleText.Continue( body )}"
			: head.Length > 0 && body.Length > 0 ? $"{head}{(comma ? ", " + OracleText.Continue( body ) : ": " + AfterColon( body ))}" : head + body;
		if ( text.Length > 0 && lasting.Length > 0 )
			return Finish( text ) + " " + lasting;
		return text.Length > 0 ? Finish( text ) : lasting;
	}

	/// <summary>"if this card is in your hand" and nowhere else: the precondition of a Hand effect.</summary>
	static bool IsThisCardInHand( OperationBlock op )
	{
		var block = BlockRegistry.Get( op.Op );
		return string.Equals( block.Value( op, "Card" ), CardRef.This, StringComparison.OrdinalIgnoreCase )
			&& Places.Parse( block.Value( op, "Places" ) ) is [Places.Hand];
	}

	/// <summary>Triggers about this card itself ("When this card arrives on the Battlefield"): they need no "While ..." lead.</summary>
	static readonly HashSet<string> SelfTriggers = new( StringComparer.Ordinal )
	{
		"OP_WhenArrives", "OP_WhenLeavesField", "OP_WhenCast", "OP_WhenSentTo", "OP_WhenDiscarded", "OP_WhenTributed", "OP_Activate", "OP_QuickCastResponse",
	};

	/// <summary>"When this card is Fusion Cast" (Kitharas Devourer): a trigger about this card itself.</summary>
	static bool IsSelfFusionCast( OperationBlock op ) =>
		op.Op == "OP_WhenFusionCast" && BlockRegistry.Get( op.Op )?.Value( op, "Card" ) == "this card";

	static string OnWhat( CardEffect effect ) => effect.Section == EffectSection.Battlefield ? "on the Battlefield" : "on the field";

	static bool IsAura( CardDefinition card ) => card?.Types.Any( t => t is "PositiveAura" or "NegativeAura" or "Aura" ) == true;

	/// <summary>Cast, Domain and Aura cards: their Field effects need no "While this card is on the field" (Cast cards leave, Domains and Auras only work there).</summary>
	static bool Passing( CardDefinition card ) =>
		card?.Types.Any( t => t is "Cast" or "Domain" or "PositiveAura" or "NegativeAura" or "Aura" or "QuickCast" ) == true;

	/// <summary>"When ..." and "Whenever ..." read on in lower case after "While this card is in the Abyss, ".</summary>
	static string LowerWhen( string lead ) =>
		Regex.IsMatch( lead, "^(When|Whenever|If|During) " ) ? char.ToLowerInvariant( lead[0] ) + lead[1..] : lead;

	/// <summary>What follows a colon starts with a capital ("...: Deal 400 Death damage"), except "you" and "allies" ("...: you draw 2").</summary>
	public static string AfterColon( string text ) =>
		text.StartsWith( "you ", StringComparison.Ordinal ) || text.StartsWith( "You ", StringComparison.Ordinal ) && !text.StartsWith( "You may", StringComparison.Ordinal )
			? "you " + text[4..]
			: text.StartsWith( "allies ", StringComparison.Ordinal ) ? text : OracleText.Capitalize( text );

	/// <summary>The card a WHEN block is about, as a card blank names it ("this card"), or empty when it names another.</summary>
	static string TriggerSubject( OperationBlock when )
	{
		var block = BlockRegistry.Get( when.Op );
		var card = block?.Params.Any( p => p.Key == "Card" ) == true ? block.Value( when, "Card" ) : "";
		// Triggers with no card blank are about this card ("When this card is sent to the Abyss").
		return card.Length == 0 || card == CardRef.This ? CardRef.This : "";
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
			var sentence = $"{OracleText.Capitalize( parts[gate] )} {AfterColon( after )}".Trim();
			return before.Length == 0 ? (first ? sentence : ". " + sentence) : Finish( before ) + " " + sentence;
		}
		// A block that opens its own sentence ("Then, if you do not control another "Kitharas" card, Void this card")
		// ends the one before it rather than stacking a second "Then".
		var own = parts.Count < 2 ? -1 : parts.FindIndex( 1, p => p.StartsWith( "Then, " ) );
		if ( own > 0 )
			return Finish( Chain( parts.Take( own ), first ) ) + " " + Chain( parts.Skip( own ), true ).TrimStart( ',', ' ' );
		var text = "";
		for ( var i = 0; i < parts.Count; i++ )
		{
			if ( first && i == 0 )
			{
				text = OracleText.Capitalize( parts[0] );
				continue;
			}
			// A gate that reads on ("Then, if 1 or more of them are Duo-Mastery,") takes the next part without a connector.
			if ( text.EndsWith( ',' ) )
			{
				text += " " + OracleText.Continue( parts[i] );
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

	/// <summary>Keywords as one list: "Lifesteal: 50%, Final Will."</summary>
	static string KeywordList( IEnumerable<OperationBlock> blocks ) =>
		Finish( string.Join( ", ", blocks.Select( OracleText.Of ).Where( t => t.Length > 0 ) ) );

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
	/// The end note for once-per limits, as Justin's cards print them (2026-11-21): "You can only activate this card's
	/// Aura and Abyss effects once per turn.", "You can only activate this card's Field effect once per turn and its
	/// Abyss effect once per Conflict." Effects with the same limit share one list. An Aura's (or Domain's) Field effect
	/// is named after its zone, and a Cast or Quick Cast card's own effect is the card itself ("You can only activate
	/// this card once per turn and its Abyss effect once per Conflict."). Empty when no effect has a limit, or the card
	/// already has its own Important Tag line.
	/// </summary>
	public static string UsageLine( CardDefinition card )
	{
		if ( card.Effects.Any( e => e.Section == EffectSection.ImportantTag && !IsInnate( e ) && !IsFamilyLimit( e ) ) )
			return "";
		// Effects whose text already prints their limit ("Once per turn, ..." typed by hand) need no note for it.
		var groups = card.Effects
			.Where( e => !(e.Section == EffectSection.ImportantTag && IsFamilyLimit( e )) )
			.Where( e => e.Usage is EffectUsage.OncePerTurn or EffectUsage.OncePerRound or EffectUsage.OncePerConflict )
			.Where( e => !(string.IsNullOrWhiteSpace( e.Text ) ? (e.HasOperations ? Write( card, e ) : "") : e.Text).Contains( "once per", StringComparison.OrdinalIgnoreCase ) )
			.GroupBy( e => e.Usage switch { EffectUsage.OncePerTurn => "turn", EffectUsage.OncePerRound => "round", _ => "Conflict" } )
			.Select( g => (Self: g.Any( e => IsSelf( card, e ) ), Effects: g.Where( e => !IsSelf( card, e ) ).ToList(), Per: g.Key) )
			.OrderByDescending( g => g.Self )
			.ToList();
		if ( groups.Count == 0 )
			return "";
		var parts = new List<string>();
		foreach ( var (self, effects, per) in groups )
		{
			var names = effects.Select( e => SectionName( card, e ) ).Distinct().ToList();
			var noun = effects.Count == 1 ? "effect" : "effects";
			var owner = parts.Count == 0 && !self ? "this card's" : "its";
			var what = self
				? (names.Count == 0 ? "this card" : $"this card and its {Series( names )} {noun}")
				: $"{owner} {Series( names )} {noun}";
			parts.Add( $"{what} once per {per}" );
		}
		return $"You can only activate {Series( parts )}.";
	}

	/// <summary>
	/// An Important Tag that is only a name-wide limit ('You can only activate 1 "Spawn" card per round.'): it is its own
	/// line, so the card's once-per end note still follows it (Spawn cards, Justin 2026-11-23).
	/// </summary>
	static bool IsFamilyLimit( CardEffect effect ) =>
		effect.Always.Count > 0 && effect.Always.All( op => op.Op == "OP_ActivationLimit" )
		&& effect.When.Count + effect.If.Count + effect.Cost.Count + effect.Then.Count == 0;

	/// <summary>A Cast or Quick Cast card's own effect (its Field or Hand effect): activating it is activating the card.</summary>
	static bool IsSelf( CardDefinition card, CardEffect effect ) =>
		card?.Types.Any( t => t is "Cast" or "QuickCast" ) == true && effect.Section is EffectSection.Field or EffectSection.Hand;

	/// <summary>"Hand", "Field", "Abyss"..., with an Aura's or Domain's Field effect called after its zone ("Aura").</summary>
	static string SectionName( CardDefinition card, CardEffect effect )
	{
		if ( effect.Section == EffectSection.Field && IsAura( card ) )
			return "Aura";
		if ( effect.Section == EffectSection.Field && card?.Types.Contains( "Domain" ) == true )
			return "Domain";
		return effect.Section.ToString();
	}
}
