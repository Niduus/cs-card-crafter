using ChaosSpells.Cards;
using ChaosSpells.Rules;
using CsCardCrafter.Core.Checks;
using Xunit;

namespace CsCardCrafter.Tests;

/// <summary>The Script blocks (Spellscript): the crafter catches mistakes before a card reaches the game.</summary>
public class ScriptTests
{
	static CardDefinition WithScript( string op, string code, string says = "Something happens." ) => new()
	{
		Name = "Script test",
		Types = { "Cast" },
		Effects =
		{
			new CardEffect
			{
				Section = EffectSection.Hand,
				Then = op == "OP_Script" ? new() { new OperationBlock { Op = op, Params = { ["Script"] = code, ["Says"] = says } } } : new(),
				If = op == "OP_IfScript" ? new() { new OperationBlock { Op = op, Params = { ["Script"] = code, ["Says"] = says } } } : new(),
			},
		},
	};

	[Fact]
	public void AGoodScriptHasNoProblems()
	{
		var card = WithScript( "OP_Script", "let n = count(field, you, \"Fire\")\nif n > 0\n  do Deal damage (Who: target enemy, Amount: 50 * n)\nend" );
		Assert.DoesNotContain( CardChecks.Run( card ), r => r.Level == CheckLevel.Problem && r.Message.Contains( "Script" ) );
	}

	[Fact]
	public void MistakesAreProblemsWithTheirLine()
	{
		var card = WithScript( "OP_Script", "let n = 1\ndo Nonsense (Amount: n)" );
		Assert.Contains( CardChecks.Run( card ), r => r.Level == CheckLevel.Problem && r.Message.Contains( "line 2" ) );
	}

	[Fact]
	public void AConditionIsOneExpression()
	{
		Assert.Empty( Spellscript.CheckCondition( "hp(you) < maxhp(you) / 2 and not myturn()" ) );
		Assert.NotEmpty( Spellscript.CheckCondition( "do Draw (Amount: 1)" ) );
		var card = WithScript( "OP_IfScript", "count(abyss, you) >=" );
		Assert.Contains( CardChecks.Run( card ), r => r.Level == CheckLevel.Problem );
	}

	[Fact]
	public void MissingCardTextIsAWarning()
	{
		var card = WithScript( "OP_Script", "do Draw (Amount: 1)", says: "" );
		Assert.Contains( CardChecks.Run( card ), r => r.Level == CheckLevel.Warning && r.Message.Contains( "Card text" ) );
	}

	[Fact]
	public void TheCardPrintsTheAuthorsWords()
	{
		var block = BlockRegistry.Get( "OP_Script" );
		var op = block.Make( ("Says", "Deal 50 damage for each Fire card you control."), ("Script", "do Draw (Amount: 1)") );
		Assert.Equal( "Deal 50 damage for each Fire card you control.", block.Text( op ) );
	}
}
