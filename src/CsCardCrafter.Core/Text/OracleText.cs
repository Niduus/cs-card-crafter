using System.Globalization;
using System.Text.RegularExpressions;
using ChaosSpells.Cards;
using ChaosSpells.Rules;

namespace CsCardCrafter.Core.Text;

/// <summary>
/// One block as card text, in the wording laws of the Unity build's oracle.md: digits for counts, count then magnitude
/// then kind then who ("you gain 2 250 damage Blades", "Give 1 -400 damage mantle to target enemy"), "%" for percent,
/// never "flat". Blocks with no rule here read as the game's own sentence (<see cref="Block.Text"/>), tidied.
/// </summary>
public static class OracleText
{
	public static string Of( OperationBlock op )
	{
		var block = BlockRegistry.Get( op?.Op );
		if ( block is null )
			return op?.Op ?? "";
		var text = op.Op switch
		{
			"OP_GiveBoon" => Boon( block, op ),
			"OP_GiveOverTime" => OverTime( block, op ),
			"OP_Draw" => Draw( block, op ),
			"OP_Mill" => $"{Subject( block.Value( op, "Who" ) )} mill {block.Value( op, "Amount" )}",
			"OP_GainMana" => $"{Subject( block.Value( op, "Who" ) )} {Verb( block.Value( op, "Who" ), "gain" )} {block.Value( op, "Amount" )} {block.Value( op, "Mana" )} Mana",
			"OP_Heal" => $"{Subject( block.Value( op, "Who" ) )} {Verb( block.Value( op, "Who" ), "heal" )} {block.Value( op, "Amount" )}",
			"OP_DealDamage" => $"Deal {block.Value( op, "Amount" )} {Mastery( block.Value( op, "Mastery" ) )}damage to {Object( block.Value( op, "Who" ) )}",
			"OP_ApplyCrowdControl" => CrowdControl( block, op ),
			"OP_GainMend" => Mend( block.Value( op, "Who" ), block.Value( op, "Count" ) ),
			"OP_RegisterVariableCostBoon" => $"Give 1 ? {BoonNoun( block.Value( op, "Kind" ), false )} to {Object( block.Value( op, "Who" ) )} (? = {block.Value( op, "Scale" )} per {block.Value( op, "Per" )} mana paid)",
			"OP_IfEnhanced" => block.Value( op, "Enhanced" ) == "true" ? "If Enhanced:" : "",
			"OP_IfMastery" => $"if your mastery is {Mastery( block.Value( op, "Mastery" ) ).Trim()}",
			"OP_OnlyIfMastery" => $"If your mastery is {Mastery( block.Value( op, "Mastery" ) ).Trim()}:",
			"OP_Activate" => "",
			_ => block.Text( op ),
		};
		return Tidy( text );
	}

	/// <summary>"Give 1 -400 damage mantle to target enemy", "you gain 3 100 Shields", "Allies gain 3 300 damage Blades".</summary>
	static string Boon( Block block, OperationBlock op )
	{
		var kind = block.Value( op, "Kind" );
		var count = block.Value( op, "Count" );
		var amount = block.Value( op, "Amount" );
		var percent = block.Value( op, "Percent" );
		var who = block.Value( op, "Who" );
		var many = count != "1";
		var noun = BoonNoun( kind, many );
		var damp = kind.EndsWith( "Mantle" );
		var size = string.IsNullOrEmpty( amount ) ? "" : (damp && !amount.StartsWith( '-' ) ? "-" : "") + amount + (string.IsNullOrEmpty( percent ) ? "" : "%") + " ";
		var mastery = Mastery( block.Value( op, "Mastery" ) );
		var what = $"{count} {size}{mastery}{noun}";
		return Gains( who ) ? $"{Subject( who )} {Verb( who, "gain" )} {what}" : $"Give {what} to {Object( who )}";
	}

	/// <summary>A boon kind as printed: Mantles are Damps and name their kind ("damage mantle"), Blades name theirs unless plain damage.</summary>
	static string BoonNoun( string kind, bool many )
	{
		var noun = kind switch
		{
			"Blade" => "damage Blade",
			"Mantle" => many ? "damage Mantle" : "damage mantle",
			"HealingMantle" => many ? "healing Mantle" : "healing mantle",
			"LifestealMantle" => many ? "lifesteal Mantle" : "lifesteal mantle",
			"HealingBlade" => "Healing Blade",
			"LifestealBlade" => "Lifesteal Blade",
			_ => kind,
		};
		return many ? noun + "s" : noun;
	}

	/// <summary>"you gain Mend", "Allies gain Mend", "Give 2 Mends to target ally".</summary>
	static string Mend( string who, string count )
	{
		var what = count == "1" || string.IsNullOrEmpty( count ) ? "Mend" : $"{count} Mends";
		return Gains( who ) ? $"{Subject( who )} {Verb( who, "gain" )} {what}" : $"Give {(what == "Mend" ? "1 Mend" : what)} to {Object( who )}";
	}

	/// <summary>"you gain 1 200 Heal Overtime for 3 rounds" (Heal Overtime never has a Mastery), "give 2 400 Fire Damage Overtime to target enemy for 3 rounds".</summary>
	static string OverTime( Block block, OperationBlock op )
	{
		var who = block.Value( op, "Who" );
		var heal = block.Value( op, "Heal" ) == "true";
		var rounds = block.Number( op, "Rounds" );
		var increase = block.Number( op, "Increase" );
		var what = $"{block.Value( op, "Count" )} {block.Value( op, "Amount" )} {(heal ? "" : Mastery( block.Value( op, "Mastery" ) ))}{(heal ? "Heal" : "Damage")} Overtime";
		var time = $"for {rounds} {(rounds == 1 ? "round" : "rounds")}" + (increase != 0 ? $", increasing by {increase} each round" : "");
		return heal && Gains( who ) ? $"{Subject( who )} {Verb( who, "gain" )} {what} {time}" : $"Give {what} to {Object( who )} {time}";
	}

	static string Draw( Block block, OperationBlock op )
	{
		var who = block.Value( op, "Who" );
		var deck = block.Value( op, "Deck" );
		var from = string.IsNullOrEmpty( deck ) || deck == "Deck" ? "" : $" from your {deck}";
		return $"{Subject( who )} {Verb( who, "draw" )} {block.Value( op, "Amount" )}{from}";
	}

	static string CrowdControl( Block block, OperationBlock op )
	{
		// As the cards word it: "Stun target enemy" (one round goes unsaid), "Confuse target enemy for 2 rounds".
		var rounds = Math.Max( 1, block.Number( op, "Rounds" ) );
		var verb = block.Value( op, "Kind" ) switch { "Marked" => "Mark", "Confusion" => "Confuse", var kind => kind };
		return $"{verb} {Object( block.Value( op, "Who" ) )}" + (rounds == 1 ? "" : $" for {rounds} rounds");
	}

	/// <summary>Whether a grant to <paramref name="who"/> reads "X gain..." (self and all-player grants) rather than "Give ... to X".</summary>
	static bool Gains( string who ) => who is Who.You or Who.Allies or Who.AllEnemies or Who.Everyone;

	static string Subject( string who ) => who switch
	{
		Who.You or "" or null => "you",
		Who.Allies => "Allies",
		_ => who,
	};

	static string Object( string who ) => who is Who.You or "" or null ? "yourself" : who;

	/// <summary>"you gain", "target enemy gains": third person for one other player.</summary>
	static string Verb( string who, string verb ) => who is Who.TargetEnemy or Who.TargetAlly or Who.TriggerController ? verb + "s" : verb;

	static string Count( string count, string noun ) => count == "1" ? $"1 {noun}" : $"{count} {noun}s";

	/// <summary>A Mastery word followed by a space, or nothing for none. Light is stored as "Sun".</summary>
	static string Mastery( string mastery ) => string.IsNullOrWhiteSpace( mastery ) ? "" : (mastery == "Sun" ? "Light" : mastery) + " ";

	/// <summary>Plurals after counts above 1 ("discard 2 other card" reads "cards"), single spaces, no blank ends.</summary>
	public static string Tidy( string text )
	{
		// A count is a number not right after another number: in "1 500 Shield" the 500 is the Shield's size, not a count.
		text = Regex.Replace( text ?? "", @"(?<!\d )\b(\d*[02-9]|\d{2,}|that many) (card|round|Mend|Trap|Shield|Blade|Mantle|Material)\b(?!s)", "$1 $2s" );
		text = Regex.Replace( text, @"(?<!\d )\b1 (card|round|Mend|Trap|Shield|Blade|Mantle|Material)s\b", "1 $1" );
		// A blank card name prints nothing, not empty quotes ("select and draw 1 Ice card").
		text = text.Replace( " \"\"", "" );
		text = Regex.Replace( text, @"\s{2,}", " " ).Trim();
		return text;
	}

	/// <summary>Upper-cases the first letter, leaving the rest as written.</summary>
	public static string Capitalize( string text ) =>
		string.IsNullOrEmpty( text ) ? text : char.ToUpper( text[0], CultureInfo.InvariantCulture ) + text[1..];

	/// <summary>Plain verbs that drop to lower case after a connector ("Then give", "Also you gain"); mechanic names keep theirs ("Then Stun", "Then Void").</summary>
	static readonly HashSet<string> PlainVerbs = new( StringComparer.Ordinal )
	{
		"Give", "You", "Deal", "Draw", "Discard", "Destroy", "Pay", "Gain", "Heal", "Send", "Send", "Return", "Attach",
		"Select", "Steal", "Convert", "Detonate", "Reveal", "Target", "Halve", "Call", "Choose", "Negate", "Reflect",
	};

	/// <summary>Lower-cases a plain verb that continues a sentence ("Then you gain", "Also give").</summary>
	public static string Continue( string text )
	{
		if ( string.IsNullOrEmpty( text ) )
			return text;
		var first = text.Split( ' ', 2 )[0];
		return PlainVerbs.Contains( first ) ? char.ToLower( text[0], CultureInfo.InvariantCulture ) + text[1..] : text;
	}
}
