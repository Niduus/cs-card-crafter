using System;
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

	/// <summary>Justin 2026-11-03: nothing is written when no once-per limit applies, and "(Not once per turn)" is flagged.</summary>
	[Fact]
	public void NoNotOncePerTurn()
	{
		var card = Card();
		card.Effects.Add( new CardEffect { Section = EffectSection.Field, Usage = EffectUsage.None, Text = "Draw 1 card." } );
		Assert.Equal( "", EffectWriter.UsageLine( card ) );
		Assert.DoesNotContain( "once per", CardWriter.Description( card, keepTyped: false ), StringComparison.OrdinalIgnoreCase );
		Assert.DoesNotContain( CsCardCrafter.Core.Checks.CardChecks.Run( card ), r => r.Message.Contains( "Not once per" ) );
		card.Effects[0].Text = "Draw 1 card. (Not once per turn)";
		Assert.Contains( CsCardCrafter.Core.Checks.CardChecks.Run( card ), r => r.Message.Contains( "Not once per" ) );
	}

	/// <summary>Of the game's effects that already run on blocks, most read back word for word; the rest are older wording.</summary>
	[Fact]
	public void MatchesMostGameCards()
	{
		var game = CsCardCrafter.Core.Cards.GameFolder.Find();
		int same = 0, total = 0;
		static string N( string s ) => System.Text.RegularExpressions.Regex.Replace( s.ToLowerInvariant().Replace( "''", "'" ), "[^a-z0-9%]+", " " ).Trim();
		foreach ( var c in game.LoadCards() )
			// Only printed effects: a card may hold its keyword blocks in an unprinted Keyword effect.
			foreach ( var e in EffectEngine.Of( c.Card ).Where( e => e.HasOperations && !string.IsNullOrWhiteSpace( e.Text ) ) )
			{
				total++;
				if ( N( EffectWriter.Write( c.Card, e ) ) == N( e.Text ) )
					same++;
			}
		Assert.True( same * 2 > total, $"{same}/{total}" );
	}
}

public class InnateConditionTests
{
	[Fact]
	public void PrintsActivateOnlyIfAndLeadsTheCard()
	{
		var card = new CardDefinition { Name = "Test", Mastery = "Life" };
		card.Effects.Add( new CardEffect { Section = EffectSection.Field, Then = { BlockRegistry.Get( "OP_Draw" ).Make( ("Amount", "1"), ("Who", "you") ) } } );
		card.Effects.Add( new CardEffect { Section = EffectSection.ImportantTag, If = { BlockRegistry.Get( "OP_IfMastery" ).Make( ("Mastery", "Life") ) } } );
		Assert.True( EffectWriter.IsInnate( card.Effects[1] ) );
		Assert.Equal( "Activate only if your mastery is Life.", EffectWriter.Write( card, card.Effects[1] ) );
		Assert.StartsWith( "{BEG}Activate only if your mastery is Life.{END}{BEG}You draw 1.{END}", CardWriter.Description( card, keepTyped: false ) );
	}
}

/// <summary>Every keyword goes in the card text's keyword line (Justin, 2026-11-03).</summary>
public class KeywordLineTests
{
	static CardDefinition Card( string description, params string[] keywords ) =>
		new() { Name = "Test", Mastery = "Ice", Description = description, Keywords = keywords.ToList() };

	[Fact]
	public void AddsToTheExistingLineKeepingTheRest()
	{
		var text = "{BEG}Anchored, Exclusive: Ice, Sturdy 1 Usage.{END}\n{BEG}Combat: 100 Ice damage to target enemy.{END}";
		var card = Card( text, "Anchored", "MasteryCast", "Sturdy", "FinalWill" );
		Assert.Equal( "{BEG}Anchored, Exclusive: Ice, Sturdy 1 Usage, Final Will.{END}\n{BEG}Combat: 100 Ice damage to target enemy.{END}", KeywordLine.Ensure( card, text ) );
		Assert.Equal( text, KeywordLine.Ensure( Card( text, "Anchored", "MasteryCast", "Sturdy" ), text ) );
	}

	[Fact]
	public void StartsALineWhenThereIsNone()
	{
		var text = "{BEG}Combat: 100 Ice damage to target enemy.{END}\n{BEG}You draw 1.{END}";
		Assert.Equal( "{BEG}Final Will, Sturdy 1 Usage.{END}\n" + text, KeywordLine.Ensure( Card( text, "FinalWill", "Sturdy" ), text ) );
		Assert.Equal( "{BEG}Exclusive: Ice.{END}", KeywordLine.Ensure( Card( "", "MasteryCast" ), "" ) );
		var innate = "{BEG}Activate only if your mastery is Life.{END}{BEG}You draw 1.{END}";
		Assert.Equal( "{BEG}Activate only if your mastery is Life.{END}{BEG}Regenerate.{END}{BEG}You draw 1.{END}", KeywordLine.Ensure( Card( innate, "Regenerate" ), innate ) );
	}

	[Fact]
	public void RemovesFromTheLineAndDropsAnEmptyLine()
	{
		var text = "{BEG}Agnostic: Light, Anchored, Final Will, Rooted 1 Usage.{END}\n{BEG}You draw 1.{END}";
		Assert.Equal( "{BEG}Agnostic: Light, Anchored, Rooted 1 Usage.{END}\n{BEG}You draw 1.{END}", KeywordLine.Remove( text, "FinalWill" ) );
		Assert.Equal( "{BEG}You draw 1.{END}", KeywordLine.Remove( "{BEG}Final Will.{END}\n{BEG}You draw 1.{END}", "Final Will" ) );
		var list = "{BEG}Exclusive: Fire, Ice, Sturdy 1 Usage.{END}";
		Assert.Equal( "{BEG}Sturdy 1 Usage.{END}", KeywordLine.Remove( list, "Exclusive" ) );
	}

	[Fact]
	public void AutoTextPrintsEveryKeyword()
	{
		var card = Card( "", "FinalWill" );
		Assert.Contains( "{BEG}Final Will.{END}", CardWriter.Description( card ) );
	}
}
