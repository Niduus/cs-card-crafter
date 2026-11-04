using ChaosSpells.Cards;
using ChaosSpells.Rules;
using CsCardCrafter.Core.Text;
using Xunit;
using Xunit.Abstractions;

namespace CsCardCrafter.Tests;

/// <summary>A block's on/off options never print as "true" or "false" in card text.</summary>
public class BlockTextTests
{
	readonly ITestOutputHelper _out;
	public BlockTextTests( ITestOutputHelper output ) => _out = output;

	[Fact]
	public void OnOffOptionsNeverPrintTrueOrFalse()
	{
		var leaks = new List<string>();
		foreach ( var block in BlockRegistry.All )
		{
			var flags = block.Params.Where( p => p.Choices is { Length: 2 } c && c.Contains( "true" ) && c.Contains( "false" ) ).ToList();
			if ( flags.Count == 0 )
				continue;
			foreach ( var on in new[] { "true", "false" } )
			{
				var op = new OperationBlock { Op = block.Id };
				foreach ( var p in block.Params )
					op.Params[p.Key] = flags.Contains( p ) ? on : (p.Default is { Length: > 0 } d ? d : p.Choices?.FirstOrDefault() ?? "Knight");
				var text = OracleText.Of( op );
				_out.WriteLine( $"{block.Id} [{on}]: {text}" );
				if ( System.Text.RegularExpressions.Regex.IsMatch( text, @"\b(true|false)\b" ) )
					leaks.Add( $"{block.Id} ({on}): {text}" );
			}
		}
		Assert.True( leaks.Count == 0, string.Join( "\n", leaks ) );
	}
}
