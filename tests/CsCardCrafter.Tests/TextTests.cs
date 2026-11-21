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
	public void CombatLineListsWhatTheTargetGets()
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
		Assert.Equal( "Combat: 150 Ice damage and one -100 damage Mantle to target enemy; you gain one 400 Shield.",
			EffectWriter.Write( Card(), effect ) );
	}

	[Fact]
	public void BoonsCountMagnitudeKindWho()
	{
		Assert.Equal( "allies gain three 350 Shields", OracleText.Of( Op( "OP_GiveBoon", ("Kind", "Shield"), ("Count", "3"), ("Amount", "350"), ("Who", "allies") ) ) );
		Assert.Equal( "you gain two 250 damage Blades", OracleText.Of( Op( "OP_GiveBoon", ("Kind", "Blade"), ("Count", "2"), ("Amount", "250"), ("Who", "you") ) ) );
		Assert.Equal( "Give one -15% lifesteal Mantle to target enemy", OracleText.Of( Op( "OP_GiveBoon", ("Kind", "LifestealMantle"), ("Count", "1"), ("Amount", "15"), ("Percent", "%"), ("Who", "target enemy") ) ) );
		Assert.Equal( "Give two 400 Traps to target enemy", OracleText.Of( Op( "OP_GiveBoon", ("Kind", "Trap"), ("Count", "2"), ("Amount", "400"), ("Who", "target enemy") ) ) );
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
		Assert.Equal( "When this card is sent to the Abyss, if your Mastery is Death: you draw 2.", EffectWriter.Write( Card(), effect ) );
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
		Assert.Equal( "You may discard this card: Allies gain one 25% Shield.", EffectWriter.Write( Card(), effect ) );
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
		// Justin, 2026-11-21: effects with the same limit share one list, and an Aura's Field effect is its "Aura" effect.
		card.Types = new() { "PositiveAura" };
		card.Effects[1].Usage = EffectUsage.OncePerTurn;
		Assert.Equal( "You can only activate this card's Aura and Abyss effects once per turn.", EffectWriter.UsageLine( card ) );
		// A Cast card's own effect is the card: "You can only activate this card once per turn and its Abyss effect once per Conflict."
		card.Types = new() { "Cast" };
		card.Effects[1].Usage = EffectUsage.OncePerConflict;
		Assert.Equal( "You can only activate this card once per turn and its Abyss effect once per Conflict.", EffectWriter.UsageLine( card ) );
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

	/// <summary>
	/// Every playable card's effects write clean text from their blocks: no block ids, no on/off words, no doubled
	/// leads, no "any location" restrictions and no listed decks in a restriction (Justin, 2026-11-19).
	/// </summary>
	[Fact]
	public void EveryGameCardWritesClean()
	{
		var game = CsCardCrafter.Core.Cards.GameFolder.Find();
		var bad = new List<string>();
		foreach ( var c in game.LoadCards().Where( c => !c.Card.IsDev ) )
			foreach ( var e in c.Card.Effects.Where( e => e.HasOperations && e.Section != EffectSection.Option ) )
			{
				var text = EffectWriter.Write( c.Card, e );
				if ( System.Text.RegularExpressions.Regex.IsMatch( text, @"OP_|\btrue\b|\bfalse\b|Activate only if Activate|\s{2}|""""|\b1 \d|\bgo off\b|any location \(|\(Hand, Field" ) )
					bad.Add( $"{c.Card.Name}: {text}" );
			}
		Assert.True( bad.Count == 0, string.Join( "\n", bad ) );
	}

	[Fact]
	public void AhziaelWritesItsPrintedText()
	{
		var game = CsCardCrafter.Core.Cards.GameFolder.Find();
		var card = game.LoadCards().First( c => c.Card.Name == "Ahziael, the True Form of Kitharas" ).Card;
		var effects = card.Effects.Where( e => e.HasOperations && e.Section != EffectSection.Option ).ToList();
		Assert.NotEmpty( effects );
		// The writer puts once-per limits on the end note (Justin, 2026-11-21); the printed text may still lead with one.
		static string NoLimit( string t ) => OracleText.Capitalize( System.Text.RegularExpressions.Regex.Replace( t, "^Once per (turn|round|Conflict)[,:] ", "" ) );
		foreach ( var e in effects.Where( e => !string.IsNullOrEmpty( e.Text ) ) ) // the keyword line is printed from the Description only
			Assert.Equal( NoLimit( e.Text ), EffectWriter.Write( card, e ) );
	}

	[Fact]
	public void WordingRules()
	{
		// A count before a value is a word; Select and Draw; HP; "Deck" alone is either deck.
		Assert.Equal( "you gain three 300 damage Blades", OracleText.Of( Op( "OP_GiveBoon", ("Kind", "Blade"), ("Count", "3"), ("Amount", "300"), ("Who", "you") ) ) );
		Assert.Equal( "Select and Draw 2 \"Kitharas\" cards of different Card Types from your Deck",
			OracleText.Of( Op( "OP_SelectAndDraw", ("Amount", "2"), ("Name", "Kitharas"), ("Pile", "Deck or Side Deck"), ("Distinct", "true") ) ) );
		Assert.Equal( "discard 1 Select non-\"Kitharas\" Crimson or Life card", OracleText.Of( Op( "OP_CostDiscardMatching", ("Amount", "1"), ("Mastery", "Crimson/Life"), ("Except", "Kitharas") ) ) );
		Assert.Equal( "plus 3 Crimson damage per 1% missing HP", OracleText.Tidy( "plus 3 Crimson damage per 1% missing Health" ) );
		// The True Form lock: your cards only, revealed Locations named, the field always in.
		Assert.Equal( "all non-\"Kitharas\" cards you control cannot activate their effects, including from the Hand, Abyss and/or Void",
			OracleText.Of( Op( "OP_OnlyNamedEffects", ("Name", "Kitharas") ) ) );
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
		Assert.Equal( "Activate only if your Mastery is Life.", EffectWriter.Write( card, card.Effects[1] ) );
		Assert.StartsWith( "{BEG}Activate only if your Mastery is Life.{END}{BEG}You draw 1.{END}", CardWriter.Description( card, keepTyped: false ) );
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

/// <summary>Justin, 2026-11-04: "If this card is tributed from your hand or field and is in your Abyss, Evade Cast this card", from blocks.</summary>
public class TributedEvadeTests
{
	static CardEffect Effect( string from, string ifCard, string places, string thenCard, string subject = "this card" ) => new()
	{
		Section = EffectSection.Persistent,
		When = { BlockRegistry.Get( "OP_WhenTributed" ).Make( ("Card", subject), ("From", from) ) },
		If = { BlockRegistry.Get( "OP_IfCardIsIn" ).Make( ("Card", ifCard), ("Places", places) ) },
		Then = { BlockRegistry.Get( "OP_EvadeCastSelf" ).Make( ("Card", thenCard) ) },
	};

	[Fact]
	public void WritesTheSentence()
	{
		var card = new CardDefinition { Name = "Test", Mastery = "Fire" };
		Assert.Equal( "If this card is tributed from your hand or field and is in your Abyss, Evade Cast this card.",
			EffectWriter.Write( card, Effect( "hand, field", "this card", "Abyss", "this card" ) ) );
		Assert.Equal( "If this card is tributed from your field and is in your hand or Void, Evade Cast this card.",
			EffectWriter.Write( card, Effect( "field", "this card", "hand, Void", "this card" ) ) );
		Assert.Equal( "If another card of yours is tributed and that card is in your Abyss, Evade Cast that card.",
			EffectWriter.Write( card, Effect( "", "that card", "Abyss", "that card", "another card of yours" ) ) );
	}

	[Fact]
	public void MordiaelStillReadsAsPrinted()
	{
		var card = new CardDefinition { Name = "Test", Mastery = "Fire" };
		var e = new CardEffect { Section = EffectSection.Persistent, When = { new OperationBlock { Op = "OP_WhenTributed" } }, Then = { new OperationBlock { Op = "OP_EvadeCastSelf" } } };
		Assert.Equal( "If this card is tributed, Evade Cast this card.", EffectWriter.Write( card, e ) );
	}
}

/// <summary>The revamped wording (card-description-revamps, 2026-11-20).</summary>
public class RevampWordingTests
{
	static OperationBlock Op( string id, params (string, string)[] values ) => BlockRegistry.Get( id ).Make( values );

	[Fact]
	public void SelectPicksForBoonsAndOvertimes()
	{
		Assert.Equal( "Destroy 3 Select Shields on target enemy", OracleText.Of( Op( "OP_DestroyBoons", ("Amount", "3"), ("Kind", "Shield"), ("Who", "target enemy"), ("Select", "Select") ) ) );
		Assert.Equal( "Destroy 3 Shields on target enemy", OracleText.Of( Op( "OP_DestroyBoons", ("Amount", "3"), ("Kind", "Shield"), ("Who", "target enemy") ) ) );
		Assert.Equal( "Detonate 1 Select Damage Overtime on target enemy", OracleText.Of( Op( "OP_DetonateOverTime", ("Amount", "1"), ("Kind", "Damage"), ("Who", "target enemy"), ("Select", "Select") ) ) );
		Assert.Equal( "Tribute 1 or more of your Select flat Shields, paying 1 Light Mana each", OracleText.Of( Op( "OP_TributeFlatShields", ("Select", "Select") ) ) );
	}

	[Fact]
	public void SelectAndDrawOtherThanAName()
	{
		Assert.Equal( "Select and Draw 1 card that mentions \"Light Mana\" from your Deck, other than \"Freezing Mana\"",
			OracleText.Of( Op( "OP_SelectAndDraw", ("Amount", "1"), ("Mentions", "Light Mana"), ("Pile", "Deck or Side Deck"), ("Except", "Freezing Mana") ) ) );
	}

	[Fact]
	public void CombatListsWhatTheTargetGetsAndTimesCrowdControl()
	{
		var card = new CardDefinition { Name = "Test", Mastery = "Spectre", Combat = "160" };
		var effect = new CardEffect
		{
			Section = EffectSection.Combat,
			Then =
			{
				Op( "OP_GiveOverTime", ("Heal", "false"), ("Count", "1"), ("Amount", "90"), ("Mastery", "Spectre"), ("Rounds", "4"), ("Increase", "90"), ("Who", "target enemy") ),
				Op( "OP_ApplyCrowdControl", ("Kind", "Stun"), ("Who", "target enemy"), ("Rounds", "1") ),
			},
		};
		Assert.Equal( "Combat: 160 Spectre damage and one 90 Spectre Damage Overtime (4 rounds, +90 each round) to target enemy, and Stun them for 1 round.",
			EffectWriter.Write( card, effect ) );
	}

	[Fact]
	public void MissingHpBonusAndHpCondition()
	{
		var card = new CardDefinition { Name = "Test", Mastery = "Crimson", Combat = "1020" };
		var effect = new CardEffect
		{
			Section = EffectSection.Combat,
			Then = { Op( "OP_GiveBoon", ("Kind", "Blade"), ("Count", "1"), ("Amount", "500"), ("Mastery", "Crimson"), ("Who", "you"), ("IfHealthAtMost", "50") ) },
			Always = { Op( "OP_CombatPerMissingHealth", ("Amount", "9") ) },
		};
		Assert.Equal( "Combat: 1020 Crimson damage to target enemy (+9 per 1% HP you are missing). If you have 50% HP or less, you gain one 500 Crimson damage Blade.",
			EffectWriter.Write( card, effect ) );
	}

	[Fact]
	public void OncePerLimitGoesOnTheEndNote()
	{
		var card = new CardDefinition { Name = "Test", Mastery = "Ice" };
		var effect = new CardEffect
		{
			Section = EffectSection.Hand, Usage = EffectUsage.OncePerTurn,
			When = { Op( "OP_Activate" ) }, Cost = { Op( "OP_DiscardSelf" ) }, Then = { Op( "OP_Draw", ("Amount", "1"), ("Who", "you") ) },
		};
		card.Effects.Add( effect );
		Assert.DoesNotContain( "once per", EffectWriter.Write( card, effect ), StringComparison.OrdinalIgnoreCase );
		Assert.Equal( "You can only activate this card's Hand effect once per turn.", EffectWriter.UsageLine( card ) );
	}
}
