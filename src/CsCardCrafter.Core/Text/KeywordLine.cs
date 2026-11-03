using System.Text.RegularExpressions;
using ChaosSpells.Cards;

namespace CsCardCrafter.Core.Text;

/// <summary>
/// The card's keyword line: every keyword the card has, printed in one paragraph at the top of its text
/// ("Anchored, Exclusive: Ice, Sturdy 1 Usage."). Justin, 2026-11-03: ALL keywords go in the description's keyword
/// line, so adding a keyword in the crafter adds it there and removing one takes it out, on hand-written text too.
/// </summary>
public static class KeywordLine
{
	static readonly Regex Paragraph = new( @"\{BEG\}(.*?)\{END\}", RegexOptions.Singleline );

	/// <summary>Keywords whose value is a list that can itself hold commas ("Exclusive: Fire, Ice").</summary>
	static readonly string[] ListKeywords = { "Exclusive", "Agnostic" };

	/// <summary>The keyword a part of the line starts with ("Sturdy 1 Usage" → Sturdy), or null.</summary>
	public static string KeywordOf( string part )
	{
		var words = Regex.Split( (part ?? "").Trim().TrimEnd( '.' ), @"[\s:]+" ).Where( w => w.Length > 0 ).ToArray();
		for ( var n = Math.Min( 3, words.Length ); n >= 1; n-- )
			if ( CardKeywords.Name( string.Join( " ", words.Take( n ) ) ) is { } name && IsCardKeyword( name ) )
				return name;
		return null;
	}

	static bool IsCardKeyword( string name ) =>
		CardKeywords.Glossary.TryGetValue( name, out var meaning )
		&& !meaning.Contains( "Mend protects from Crowd Control", StringComparison.Ordinal )
		&& name is not ("Crowd Control" or "Healing" or "Overhealing" or "Detonate");

	/// <summary>
	/// The keywords of a paragraph when it is a keyword line (every part a keyword, list keywords may run on), else null.
	/// </summary>
	public static List<(string Keyword, string Text)> Parts( string paragraph )
	{
		var body = (paragraph ?? "").Trim();
		if ( body.EndsWith( '.' ) )
			body = body[..^1];
		if ( body.Length == 0 )
			return null;
		var parts = new List<(string Keyword, string Text)>();
		foreach ( var raw in body.Split( ',' ) )
		{
			var piece = raw.Trim();
			if ( piece.Length == 0 )
				return null;
			if ( KeywordOf( piece ) is { } keyword )
				parts.Add( (keyword, piece) );
			else if ( parts.Count > 0 && ListKeywords.Contains( parts[^1].Keyword ) && !piece.Contains( ' ' ) )
				parts[^1] = (parts[^1].Keyword, parts[^1].Text + ", " + piece);
			else
				return null;
		}
		return parts;
	}

	static string Join( List<(string Keyword, string Text)> parts ) => string.Join( ", ", parts.Select( p => p.Text ) ) + ".";

	/// <summary>How a newly added keyword prints, using the card's own values where it has them.</summary>
	public static string Phrase( CardDefinition card, string keyword ) => keyword switch
	{
		"Exclusive" => "Exclusive: " + CsCardCrafter.Core.Cards.CardParts.MasteryLabel( string.IsNullOrEmpty( card.Mastery ) ? "Fire" : card.Mastery ),
		"Anchored" when !card.PermanentAnchored && card.AnchoredRounds > 0 => $"Anchored {card.AnchoredRounds} Rounds",
		"Lifesteal" when card.LifestealFlat > 0 && card.LifestealPercent > 0 => $"Lifesteal: {card.LifestealFlat} + {card.LifestealPercent}%",
		"Lifesteal" when card.LifestealPercent > 0 => $"Lifesteal: {card.LifestealPercent}%",
		"Lifesteal" when card.LifestealFlat > 0 => $"Lifesteal: {card.LifestealFlat}",
		"Sturdy" or "Rooted" or "Sanctified" or "Tethered" => keyword + " 1 Usage",
		_ => keyword,
	};

	/// <summary>
	/// The description with every keyword of the card in its keyword line: missing ones are added at the end of the
	/// line, and a card with no line gets one at the top (after any "Activate only if" line). Nothing else changes.
	/// </summary>
	public static string Ensure( CardDefinition card, string description )
	{
		var text = description ?? "";
		var wanted = (card.Keywords ?? new()).Select( k => CardKeywords.Name( k ) ).Where( n => n is not null && IsCardKeyword( n ) ).Distinct().ToList();
		if ( wanted.Count == 0 )
			return text;
		var paragraphs = Paragraph.Matches( text ).ToList();
		var line = paragraphs.FirstOrDefault( m => Parts( m.Groups[1].Value ) is not null );
		var parts = line is not null ? Parts( line.Groups[1].Value ) : new();
		var missing = wanted.Where( w => !parts.Any( p => p.Keyword == w ) ).ToList();
		if ( missing.Count == 0 )
			return text;
		parts.AddRange( missing.Select( m => (m, Phrase( card, m )) ) );
		if ( line is not null )
			return text[..line.Groups[1].Index] + Join( parts ) + text[(line.Groups[1].Index + line.Groups[1].Length)..];
		if ( paragraphs.Count == 0 )
			return "{BEG}" + Join( parts ) + "{END}" + (text.Trim().Length > 0 ? "\n{BEG}" + text.Trim() + "{END}" : "");
		// A new line goes first, after any innate "Activate only if" lines, with the same break the text uses.
		var before = paragraphs.TakeWhile( m => m.Groups[1].Value.TrimStart().StartsWith( "Activate only if", StringComparison.OrdinalIgnoreCase ) ).Count();
		var gap = text.Contains( "{END}\n{BEG}" ) ? "\n" : "";
		if ( before == 0 )
			return "{BEG}" + Join( parts ) + "{END}" + gap + text;
		var end = paragraphs[before - 1].Index + paragraphs[before - 1].Length;
		return text[..end] + gap + "{BEG}" + Join( parts ) + "{END}" + text[end..];
	}

	/// <summary>The description with <paramref name="keyword"/> taken out of the keyword line (the line goes when it empties).</summary>
	public static string Remove( string description, string keyword )
	{
		var text = description ?? "";
		var name = CardKeywords.Name( keyword ) ?? keyword;
		var line = Paragraph.Matches( text ).FirstOrDefault( m => Parts( m.Groups[1].Value ) is { } ps && ps.Any( x => x.Keyword == name ) );
		if ( line is null )
			return text;
		var parts = Parts( line.Groups[1].Value ).Where( p => p.Keyword != name ).ToList();
		if ( parts.Count > 0 )
			return text[..line.Groups[1].Index] + Join( parts ) + text[(line.Groups[1].Index + line.Groups[1].Length)..];
		// The line is empty now: take it out with the break after it.
		var after = line.Index + line.Length;
		if ( after < text.Length && text[after] == '\n' )
			after++;
		return text[..line.Index] + text[after..];
	}
}
