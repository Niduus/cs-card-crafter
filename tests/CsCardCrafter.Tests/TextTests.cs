using ChaosSpells.Cards;
using ChaosSpells.Rules;
using CsCardCrafter.Core.Text;
using Xunit;

namespace CsCardCrafter.Tests;

/// <summary>The suggested text follows oracle.md's wording laws.</summary>
public class TextTests
{
	static OperationBlock Op( string id, params (string, string)[] values ) => BlockRegistry.Get( id ).Make( values );

	static CardDefinition Card( string mastery = "Ice", string combat = "150" ) =>
		new() { Name = "Test", Mastery = mastery, Combat = combat, Types = { "Damage" } };

	[Fact]
	public void CombatLineWithThenAlso()
	{
		var effect = new CardEffect
		{
			Section = EffectSection.Combat,
			Then =
			{
				Op( "OP_GiveBoon", ("Kind", "Shield"), ("Count", "1"), ("Amount", "400"), ("Who", "you") ),
				Op( "OP_GiveBoon", ("Kind", "Mantle"), ("Count", "1"), ("Amount", "100"), ("Who", "target enemy") ),
			},
		};
		Assert.Equal( "Combat: 150 Ice damage to target enemy, Then you gain 1 400 Shield, Also give 1 -100 damage mantle to target enemy.",
			EffectWriter.Write( Card(), effect ) );
	}

	[Fact]
	public void BoonsCountMagnitudeKindWho()
	{
		Assert.Equal( "Allies gain 3 350 Shields", OracleText.Of( Op( "OP_GiveBoon", ("Kind", "Shield"), ("Count", "3"), ("Amount", "350"), ("Who", "allies") ) ) );
		Assert.Equal( "you gain 2 250 damage Blades", OracleText.Of( Op( "OP_GiveBoon", ("Kind", "Blade"), ("Count", "2"), ("Amount", "250"), ("Who", "you") ) ) );
		Assert.Equal( "Give 1 -15% lifesteal mantle to target enemy", OracleText.Of( Op( "OP_GiveBoon", ("Kind", "LifestealMantle"), ("Count", "1"), ("Amount", "15"), ("Percent", "%"), ("Who", "target enemy") ) ) );
		Assert.Equal( "Give 2 400 Traps to target enemy", OracleText.Of( Op( "OP_GiveBoon", ("Kind", "Trap"), ("Count", "2"), ("Amount", "400"), ("Who", "target enemy") ) ) );
	}

	[Fact]
	public void NeverPrintsFlat()
	{
		var text = OracleText.Of( Op( "OP_GiveBoon", ("Kind", "Shield"), ("Count", "3"), ("Amount", "100"), ("Who", "you") ) );
		Assert.DoesNotContain( "flat", text );
	}

	[Fact]
	public void TriggeredEffectWithCondition()
	{
		var effect = new CardEffect
		{
			Section = EffectSection.Abyss,
			When = { Op( "OP_WhenSentTo", ("Pile", "Abyss") ) },
			If = { Op( "OP_IfMastery", ("Mastery", "Death") ) },
			Then = { Op( "OP_Draw", ("Amount", "2"), ("Who", "you") ) },
		};
		Assert.Equal( "When this card is sent to the Abyss, if your mastery is Death: you draw 2.", EffectWriter.Write( Card(), effect ) );
	}

	[Fact]
	public void ActivatedWithCost()
	{
		var effect = new CardEffect
		{
			Section = EffectSection.Hand,
			When = { Op( "OP_Activate" ) },
			Cost = { Op( "OP_DiscardSelf" ) },
			Then = { Op( "OP_GiveBoon", ("Kind", "Shield"), ("Count", "1"), ("Amount", "25"), ("Percent", "%"), ("Who", "allies") ) },
		};
		Assert.Equal( "You may discard this card: Allies gain 1 25% Shield.", EffectWriter.Write( Card(), effect ) );
	}

	[Fact]
	public void DuoCardsHitInTheirLastMastery()
	{
		var card = Card( "Crimson", "600" );
		card.DuoMasteries = new() { "Crimson", "Ice" };
		Assert.StartsWith( "Combat: 600 Ice damage", EffectWriter.Write( card, new CardEffect { Section = EffectSection.Combat } ) );
	}

	[Fact]
	public void UsageLine()
	{
		var card = Card();
		card.Effects.Add( new CardEffect { Section = EffectSection.Field, Usage = EffectUsage.OncePerTurn } );
		card.Effects.Add( new CardEffect { Section = EffectSection.Abyss, Usage = EffectUsage.OncePerConflict } );
		Assert.Equal( "You can only activate this card's Field effect once per turn and its Abyss effect once per Conflict.", EffectWriter.UsageLine( card ) );
	}

	/// <summary>Of the game's effects that already run on blocks, most read back word for word; the rest are older wording.</summary>
	[Fact]
	public void MatchesMostGameCards()
	{
		var game = CsCardCrafter.Core.Cards.GameFolder.Find();
		int same = 0, total = 0;
		static string N( string s ) => System.Text.RegularExpressions.Regex.Replace( s.ToLowerInvariant().Replace( "''", "'" ), "[^a-z0-9%]+", " " ).Trim();
		foreach ( var c in game.LoadCards() )
			foreach ( var e in CardScript.Read( c.Card ).Where( e => e.HasOperations ) )
			{
				total++;
				if ( N( EffectWriter.Write( c.Card, e ) ) == N( e.Text ) )
					same++;
			}
		Assert.True( same * 2 > total, $"{same}/{total}" );
	}
}
