using ChaosSpells.Cards;

namespace CsCardCrafter.Core.Cards;

/// <summary>A card type the Card tab offers, with the icon the game prints for it top right.</summary>
public sealed record CardTypeInfo( string Id, string Label, string Icon );

/// <summary>A kind of printed cost: its icon, and how the amount reads ("3" Light, "7%" Health).</summary>
public sealed record CostKind( string Id, string Label, string Icon, string Suffix = "" );

/// <summary>
/// The choices behind the Card tab, taken from how the game's 392 cards are built: card types and their icons,
/// cost icons, deck icons and each Mastery's background and crest.
/// </summary>
public static class CardParts
{
	public static readonly CardTypeInfo[] Types =
	{
		new( "Damage", "Damage", "ui/card/resources/cardtypeicons/damage.png" ),
		new( "Cast", "Cast", "ui/card/resources/cardtypeicons/cast.png" ),
		new( "QuickCast", "Quick Cast", "ui/card/resources/cardtypeicons/quickcasts.png" ),
		new( "Blade", "Blade", "ui/card/icons/new_icons/blade.png" ),
		new( "Mantle", "Mantle", "ui/card/icons/new_icons/mantle.png" ),
		new( "Shield", "Shield", "ui/card/icons/new_icons/shield.png" ),
		new( "Trap", "Trap", "ui/card/icons/new_icons/trap.png" ),
		new( "Domain", "Domain", "ui/card/icons/new_icons/domain.png" ),
		new( "PositiveAura", "Positive Aura", "ui/card/icons/new_icons/positive_aura.png" ),
		new( "NegativeAura", "Negative Aura", "ui/card/icons/new_icons/negative_aura.png" ),
		new( "Heal", "Heal", "ui/card/icons/heal.png" ),
		new( "Spirit", "Spirit", "ui/card/icons/spirit.png" ),
		new( "Token", "Token", "ui/card/icons/unknown.png" ),
	};

	public static readonly CostKind[] Costs =
	{
		new( "Light", "Light Mana", "ui/card/icons/newtestmana.png" ),
		new( "Darkness", "Darkness Mana", "ui/card/icons/newshadmana.png" ),
		new( "Health", "Health %", "ui/card/icons/currenthp.png", "%" ),
		new( "Cards", "Cards", "ui/card/icons/card_new.png" ),
	};

	public const string MainDeckIcon = "ui/card/icons/decktypemain.png";
	public const string SideDeckIcon = "ui/card/icons/decktypeside.png";
	public const string OnlyOneIcon = "ui/card/icons/onlyone.png";
	/// <summary>Mythic: only one of that card on the field across all players (the game's BoardPreview.MythicTaken).</summary>
	public const string MythicIcon = "ui/card/icons/onlyoneall.png";
	public static bool IsMythicIcon( string icon ) => (icon ?? "").EndsWith( "onlyoneall.png", System.StringComparison.OrdinalIgnoreCase );
	public static bool IsLegendaryIcon( string icon ) => !string.IsNullOrEmpty( icon ) && !IsMythicIcon( icon );
	public const string NoReshuffleIcon = "ui/card/icons/noreshuffle.png";
	public const string FusionIcon = "ui/card/icons/newfuse.png";

	/// <summary>
	/// Masteries a card can Portray, in the card sort order (Light is stored as "Sun", as in the game's data). "Mastery"
	/// is the game's Mastery cards (Kitharas, Yharon), whose Combat hits in the Mastery they name.
	/// </summary>
	public static readonly string[] Masteries =
		{ "Ice", "Fire", "Storm", "Myth", "Life", "Death", "Balance", "Shadow", "Embraced", "Spectre", "Crimson", "Sun", "Darkness", "Masterless", "Mastery" };

	/// <summary>How a Mastery is named in menus: "Sun" is Light.</summary>
	public static string MasteryLabel( string mastery ) => mastery == "Sun" ? "Light" : mastery;

	public static CardTypeInfo Type( string id ) => Types.FirstOrDefault( t => t.Id.Equals( id, StringComparison.OrdinalIgnoreCase ) );

	public static CostKind CostOf( CardCost cost ) => Costs.FirstOrDefault( k => k.Icon == cost?.Icon ) ?? Costs[0];

	/// <summary>The background a card of this Mastery uses (Light is "lightbackground"); Spirit and Token cards have their own.</summary>
	public static string Background( string mastery, IEnumerable<string> types = null )
	{
		var t = types?.ToList() ?? new();
		if ( t.Contains( "Spirit" ) ) return "ui/card/backgrounds/spiritbackground.png";
		if ( t.Contains( "Token" ) ) return "ui/card/backgrounds/tokenbackground.png";
		var name = ChaosSpells.Cards.Masteries.Get( mastery ).Name.ToLowerInvariant();
		if ( name == "darkness" ) name = "shadow";
		return $"ui/card/backgrounds/{name}background.png";
	}

	/// <summary>The crest top left: the game's Mastery crest, or the rainbow Mastery crest for Mastery cards (as the game's importer gives them).</summary>
	public static string Crest( string mastery ) =>
		mastery == "Mastery" ? "ui/card/icons/mastery.png" : ChaosSpells.Cards.Masteries.Get( mastery ).Icon;

	/// <summary>A fresh card with everything the face needs, ready to edit.</summary>
	public static CardDefinition NewCard( string mastery = "Fire" )
	{
		var card = new CardDefinition
		{
			Name = "New Card",
			Mastery = mastery,
			Types = new() { "Damage" },
			Deck = DeckKind.Main,
			MaxCopies = 3,
			Costs = new() { new CardCost { Icon = Costs[0].Icon, Amount = "1" } },
			Combat = "100",
			CombatTarget = CombatTarget.Enemy,
			FooterLeft = $"Reg. @Frozen Lakes - {DateTime.Now.Year}\nSet: A.0 / Version 1.0",
			FooterRight = "Design Credit: Niduus\nImage Credit: ",
			Effects = new()
			{
				new CardEffect
				{
					Section = EffectSection.Combat,
					Then = { ChaosSpells.Rules.BlockRegistry.Get( "OP_GiveBoon" ).Make( ("Kind", "Shield"), ("Count", "1"), ("Amount", "100"), ("Who", "you") ) },
				},
			},
		};
		ApplyLook( card );
		return card;
	}

	/// <summary>Sets the background, crest, type and deck icons from the card's Mastery, type and deck, as the game's cards have them.</summary>
	public static void ApplyLook( CardDefinition card )
	{
		card.Background = Background( card.Mastery, card.Types );
		card.ClassIcon = Crest( card.Mastery );
		card.TypeIcon = Type( card.Types.FirstOrDefault() )?.Icon ?? "";
		card.DeckIcon = card.IsToken ? "" : card.BuildsInto == DeckKind.Side ? SideDeckIcon : card.BuildsInto == DeckKind.Main ? MainDeckIcon : "";
	}
}
