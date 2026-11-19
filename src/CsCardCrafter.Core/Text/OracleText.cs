using System.Globalization;
using System.Text.RegularExpressions;
using ChaosSpells.Cards;
using ChaosSpells.Rules;
using ChaosSpells.Rules.Blocks;

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
			"OP_IfEnhanced" => block.Value( op, "Enhanced" ) == "true" ? "If Enhanced:" : "",
			"OP_IfMastery" => $"if your mastery is {Mastery( block.Value( op, "Mastery" ) ).Trim()}",
			"OP_OnlyIfMastery" => $"If your mastery is {Mastery( block.Value( op, "Mastery" ) ).Trim()}:",
			"OP_Activate" => "",
			"OP_CombatPerCount" => CombatPerCount( block, op ),
			"OP_IfHaveCard" => HaveCard( block, op ),
			"OP_SelectAndDraw" => SelectAndDraw( block, op ),
			"OP_FusionCastSelf" => FusionCastSelf( block, op ),
			"OP_AttachTargetCard" => block.Value( op, "Host" ) is { Length: > 0 } host && host != "this card"
				? $"Attach target card on the field to target \"{host}\" Fusion card you control" : "Attach target card on the field to this card as material",
			"OP_VoidMasteryCardFromAbyss" => $"Void 1 {Mastery( block.Value( op, "Mastery" ) )}card in your Abyss",
			"OP_RegisterVariableCostBoon" => VariableBoon( block, op ),
			"OP_CostDiscardMatching" => DiscardMatching( block, op ),
			"OP_GrantAnchored" => Grant( block, op, "Anchored" + (block.Number( op, "Rounds" ) > 0 ? " " + block.Value( op, "Rounds" ) : "") ),
			"OP_GrantKeyword" => Grant( block, op, KeywordName( block.Value( op, "Keyword" ) ) ),
			"OP_ConvertBoons" => $"Select and convert up to {block.Value( op, "Amount" )} {block.Value( op, "Percent" )} {Spaced( block.Value( op, "Kind" ) )}s on yourself",
			"OP_Anchored" => block.Number( op, "Rounds" ) > 0 ? $"Anchored {block.Value( op, "Rounds" )} Rounds" : "Anchored",
			"OP_Frontline" => block.Number( op, "Value" ) > 0 ? $"Frontline {block.Value( op, "Count" )} ({block.Value( op, "Value" )} each)" : $"Frontline {block.Value( op, "Count" )}",
			"OP_Exclusive" => "Exclusive: " + string.Join( ", ", block.Value( op, "Masteries" ).Split( '/', ',' ).Select( m => Title( Mastery( m.Trim() ).Trim() ) ).Where( m => m.Length > 0 ) ),
			"OP_IfSpiritUses" => block.Text( op ).Replace( "Activate only if ", "if " ),
			_ => block.Text( op ),
		};
		return Tidy( text );
	}

	/// <summary>An on/off option's words: "non-Fusion " when on, nothing when off (never the word "true" or "false").</summary>
	static string Flag( Block block, OperationBlock op, string key, string words ) =>
		block.Value( op, key ) == "true" ? words + " " : "";

	/// <summary>A card name in quotes followed by a space, or nothing when the block names no card.</summary>
	static string Named( Block block, OperationBlock op ) =>
		block.Value( op, "Name" ) is { Length: > 0 } name ? $"\"{name}\" " : "";

	/// <summary>"+1000 per non-Fusion "Yharon" card on your field or in your Abyss", "+50 per attached card", "+60 per card revealed during its Fusion Cast".</summary>
	static string CombatPerCount( Block block, OperationBlock op )
	{
		var each = $"+{block.Value( op, "Amount" )} per ";
		var card = $"{Flag( block, op, "NonFusion", "non-Fusion" )}{Named( block, op )}card";
		return block.Value( op, "Where" ) switch
		{
			CombatPerCountBlock.Attached => each + "attached " + card,
			CombatPerCountBlock.Revealed => each + card + " revealed during its Fusion Cast",
			CombatPerCountBlock.YourAbyss => each + card + " in your Abyss",
			CombatPerCountBlock.EveryFieldAndAbyss => each + card + " on the field and in any Abyss",
			_ => each + card + " on your field or in your Abyss",
		};
	}

	/// <summary>"you have a non-Fusion "Kitharas" Damage type card on the field or in your hand".</summary>
	static string HaveCard( Block block, OperationBlock op )
	{
		var type = block.Value( op, "Type" ) is { Length: > 0 } t ? t + " type " : "";
		var where = block.Value( op, "Where" ) == "Abyss" ? "in your Abyss" : "on the field or in your hand";
		const string article = "a"; // printed cards say "a" whether or not this card itself counts
		return $"you have {article} {Flag( block, op, "NonFusion", "non-Fusion" )}{Named( block, op )}{type}card {where}";
	}

	/// <summary>
	/// 'Select and Draw 2 "Kitharas" cards of different Card Types from your Deck', 'Select and Draw 1 card that mentions
	/// "Light Mana" from your Deck (excluding "Burning Mana")'. "Deck" alone means either deck (Justin, 2026-11-02).
	/// </summary>
	static string SelectAndDraw( Block block, OperationBlock op )
	{
		var kind = block.Value( op, "Kind" ) switch { "Fusion" => "Fusion ", "Fusion or Cast" => "Fusion or Cast type ", "non-Fusion" => "non-Fusion ", _ => "" };
		var mentions = block.Value( op, "Mentions" ) is { Length: > 0 } m ? $" that mentions \"{m}\"" : "";
		var distinct = block.Value( op, "Distinct" ) == "true" ? " of different Card Types" : "";
		var pile = block.Value( op, "Pile" ) switch
		{
			"Deck" or "Deck or Side Deck" => "your Deck",
			"Main Deck" => "your Main Deck",
			"Side Deck" => "your Side Deck",
			"Abyss or Deck" => "your Abyss or Deck",
			"Any" => "your Deck or Abyss",
			var other => "your " + other,
		};
		return $"Select and Draw {block.Value( op, "Amount" )} {kind}{Mastery( block.Value( op, "Mastery" ) )}{Named( block, op )}card{distinct}{mentions} from {pile}";
	}

	/// <summary>
	/// "Fusion Cast this card from your hand by revealing 1 Fire card and 1 Crimson card in your hand (using them as
	/// Materials)", 'by revealing 1 other "Kitharas" card in your hand (not used as Materials)', "by revealing cards whose
	/// combined Light Mana cost is 7 or more", "by revealing all other cards in your hand".
	/// </summary>
	static string FusionCastSelf( Block block, OperationBlock op )
	{
		var from = block.Value( op, "From" ) == "Hand or Side Deck" ? "your hand or Side Deck" : "your hand";
		var reveals = new List<string>();
		if ( block.Value( op, "AllHand" ) == "true" )
			reveals.Add( "all other cards in your hand" );
		var masteries = block.Value( op, "Masteries" ).Split( ',', '/' ).Select( m => Mastery( m.Trim() ).Trim() ).Where( m => m.Length > 0 ).ToList();
		var parts = masteries.Select( m => $"1 {m} card" ).ToList();
		if ( block.Value( op, "Named" ) is { Length: > 0 } named )
		{
			var count = Math.Max( 1, (int)block.Number( op, "NamedCount" ) );
			var orMore = block.Value( op, "NamedOrMore" ) == "true";
			var light = block.Number( op, "ExactLight" ) > 0 ? $"{block.Value( op, "ExactLight" )} Light Mana " : "";
			parts.Add( count == 1 && !orMore && light.Length == 0
				? $"1 other \"{named}\" card"
				: $"{(light.Length > 0 && count < Words.Length ? Words[count] : count.ToString())}{(orMore ? " or more" : "")} {light}\"{named}\" card{(count == 1 && !orMore ? "" : "s")}" );
		}
		if ( block.Number( op, "Count" ) > 0 )
			parts.Add( $"{block.Value( op, "Count" )} other cards" );
		if ( parts.Count > 0 )
			reveals.Add( (parts.Count == 1 ? parts[0] : string.Join( ", ", parts.Take( parts.Count - 1 ) ) + " and " + parts[^1]) + " in your hand" );
		if ( block.Number( op, "MinLight" ) > 0 )
			reveals.Add( $"cards in your hand whose combined Light Mana cost is {block.Value( op, "MinLight" )} or more" );
		var how = reveals.Count == 0 ? "" : " by revealing " + string.Join( " and ", reveals );
		var material = reveals.Count == 0 ? "" : block.Value( op, "Keep" ) == "true" ? " (not used as Materials)" : " (using them as Materials)";
		var immunity = block.Value( op, "ImmunityIf" ) is { Length: > 0 } spawn ? $". If one of them is a \"{spawn}\" card, you gain Immunity 1" : "";
		return $"Fusion Cast this card from {from}{how}{material}{immunity}";
	}

	/// <summary>"Give one ? damage Blade to target ally (? = 35 per Light Mana paid)", "(? = 2 per 1% HP paid)".</summary>
	static string VariableBoon( Block block, OperationBlock op )
	{
		var per = block.Value( op, "Per" ) switch
		{
			"Health percent" or "HealthPercent" => "1% HP",
			"Health" => "HP",
			var mana => $"{mana} Mana",
		};
		var percent = block.Value( op, "Percent" ) is { Length: > 0 } ? "%" : "";
		return $"Give 1 ?{percent} {Mastery( block.Value( op, "Mastery" ) )}{BoonNoun( block.Value( op, "Kind" ), false )} to {Object( block.Value( op, "Who" ) )} (? = {block.Value( op, "Scale" )} per {per} paid)";
	}

	/// <summary>'discard 1 select non-"Kitharas" Crimson or Life card'.</summary>
	static string DiscardMatching( Block block, OperationBlock op )
	{
		var except = block.Value( op, "Except" ) is { Length: > 0 } e ? $"non-\"{e}\" " : "";
		return $"discard {block.Value( op, "Amount" )} select {except}{Masteries( block.Value( op, "Mastery" ) )}card";
	}

	/// <summary>'Damage type "Kitharas" cards you control of your Mastery gain Anchored 3'.</summary>
	static string Grant( Block block, OperationBlock op, string what )
	{
		var type = block.Value( op, "Type" ) is { Length: > 0 } t ? t + " type " : "";
		var mastery = block.Value( op, "YourMastery" ) == "true" ? " of your Mastery" : "";
		return $"{type}{Named( block, op )}cards you control{mastery} gain {what}";
	}

	static string KeywordName( string id ) => id switch { "FinalWill" => "Final Will", _ => id };

	/// <summary>"HealingMantle" reads "healing Mantle", "LifestealBlade" reads "Lifesteal Blade".</summary>
	static string Spaced( string kind ) => kind switch
	{
		"HealingMantle" => "healing Mantle",
		"LifestealMantle" => "lifesteal Mantle",
		"HealingBlade" => "Healing Blade",
		"LifestealBlade" => "Lifesteal Blade",
		"Mantle" => "damage Mantle",
		_ => kind,
	};

	/// <summary>"Crimson or Life " from "Crimson/Life"; nothing for none.</summary>
	static string Masteries( string value )
	{
		var list = (value ?? "").Split( '/', ',' ).Select( m => Mastery( m.Trim() ).Trim() ).Where( m => m.Length > 0 ).ToList();
		return list.Count == 0 ? "" : (list.Count == 1 ? list[0] : string.Join( ", ", list.Take( list.Count - 1 ) ) + " or " + list[^1]) + " ";
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
	static bool Gains( string who ) => who is Who.You or Who.Allies or Who.Everyone;

	static string Subject( string who ) => who switch
	{
		Who.You or "" or null => "you",
		Who.Allies => "Allies",
		_ => who,
	};

	static string Object( string who ) => who is Who.You or "" or null ? "yourself" : who;

	/// <summary>"you gain", "target enemy gains": third person for one other player.</summary>
	static string Verb( string who, string verb ) => who is Who.TargetEnemy or Who.TargetAlly or Who.TriggerController or Who.Everyone ? verb + "s" : verb;

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
		// 'A|B' name lists read '"A" or "B"'.
		while ( Regex.IsMatch( text, "\"([^\"|]+)\\|([^\"]+)\"" ) )
			text = Regex.Replace( text, "\"([^\"|]+)\\|([^\"]+)\"", "\"$1\" or \"$2\"" );
		// Mastery lists "Fire/Life" read "Fire or Life".
		text = Regex.Replace( text, @"\b(Ice|Fire|Storm|Myth|Life|Death|Balance|Shadow|Embraced|Spectre|Crimson|Light|Darkness)/(?=[A-Z])", "$1 or " );
		while ( Regex.IsMatch( text, @"\b(\w+) or (\w+) or " ) )
			text = Regex.Replace( text, @"\b(\w+) or (\w+) or ", "$1, $2 or " );
		// A Card Type before a card reads "Damage type" ('Evade Cast 1 Damage type "Kitharas" card').
		text = Regex.Replace( text, @"\bDamage (?=(""[^""]+"" )?cards?\b)", "Damage type " );
		// House words (Justin, 2026-11-19): Select and Draw, Light Mana, HP.
		text = Regex.Replace( text, @"\b[Ss]elect and [Dd]raw\b", "Select and Draw" );
		text = Regex.Replace( text, @"\b(Light|Darkness|LIGHT|DARKNESS) mana\b", m => Title( m.Groups[1].Value ) + " Mana" );
		text = Regex.Replace( text, @"\b[Hh]ealth\b(?! cost)", "HP" );
		text = Regex.Replace( text, @" on you(?=[.,;:)]|$)", " on yourself" );
		text = text.Replace( "gains Any Mana", "gains Mana" );
		// A count straight before a value is a word: "Gain three 300 Blades", "give one 400 Trap" (Justin, 2026-11-19).
		text = Regex.Replace( text, @"(?<![\d.%] |\d)\b([1-9]|1[0-2]) (?=[-?]?\d|\?)", m => Words[int.Parse( m.Groups[1].Value )] + " " );
		text = Regex.Replace( text, @"\s{2,}", " " ).Trim();
		// Plural across a card's describing words: 'Select and Draw 2 "Kitharas" cards'.
		text = Regex.Replace( text, @"(?<![\d] )\b(\d*[02-9]|\d{2,}|two|three|four|five|six|seven|eight|nine|ten) ((?:(?!(?:per|of|in|from|with|on|to|and|or)\b)[\w""-]+ ){1,3})card\b(?!s)", "$1 $2cards" );
		return text;
	}

	static readonly string[] Words = { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve" };

	static string Title( string word ) => word.Length == 0 ? word : char.ToUpperInvariant( word[0] ) + word[1..].ToLowerInvariant();

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
		if ( text.StartsWith( "Select and Draw", StringComparison.Ordinal ) )
			return text;
		var first = text.Split( ' ', 2 )[0];
		return PlainVerbs.Contains( first ) ? char.ToLower( text[0], CultureInfo.InvariantCulture ) + text[1..] : text;
	}
}
