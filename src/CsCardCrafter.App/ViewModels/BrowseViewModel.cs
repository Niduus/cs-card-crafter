using System.Collections.ObjectModel;
using ChaosSpells.Cards;
using CommunityToolkit.Mvvm.ComponentModel;
using CsCardCrafter.Core.Cards;
using CsCardCrafter.Core.Text;

namespace CsCardCrafter.App.ViewModels;

/// <summary>A game card in the browser.</summary>
public sealed record BrowseItem( GameCard Game, string Name, string Mastery, string Type, string Cost, string Blocks )
{
	public CardDefinition Card => Game.Card;

	/// <summary>What the card picker shows and types to: the card's name, with its collection folder.</summary>
	public string Detail => $"{Mastery} · {Type} · {Collection}";
	public string Collection => Path.GetFileName( Path.GetDirectoryName( Game.Path ) ) ?? "";

	public override string ToString() => Name;
}

/// <summary>
/// Every card in the game folder, in the game's sort order (Mastery, then Mana Count, then A to Z), with one search bar
/// that takes the game's own search words. Picking one shows its face; Open edits it, Copy starts a new card from it.
/// </summary>
public sealed partial class BrowseViewModel : ObservableObject
{
	List<BrowseItem> _all = new();

	public ObservableCollection<BrowseItem> Results { get; } = new();

	/// <summary>Every card file in the game's cards folder (all collections, dev cards too), for the card picker.</summary>
	public ObservableCollection<BrowseItem> All { get; } = new();
	public string SearchHelp => string.Join( "\n", CardSearch.Help );

	[ObservableProperty] string _query = "";
	[ObservableProperty] BrowseItem _selected;
	[ObservableProperty] string _summary = "";

	public void Load( GameFolder game )
	{
		var broken = new List<string>();
		var cards = game?.LoadCards( broken ) ?? new();
		if ( game is not null )
		{
			Services.Log.Write( $"Game folder {game.Root}: {cards.Count} cards read from {game.CardsFolder}" + (broken.Count > 0 ? $", {broken.Count} couldn't be read" : "") );
			foreach ( var file in broken )
				Services.Log.Write( "  Card file not read: " + file + (GameFolder.LastError( file ) is { } why ? " (" + why + ")" : "") );
		}
		_all = cards.Select( g => new BrowseItem(
			g,
			g.Card.Name,
			CardParts.MasteryLabel( g.Card.Mastery ),
			string.Join( ", ", g.Card.Types.Select( CardSearch.TypeName ) ),
			CardOrder.Cost( g.Card ).ToString(),
			Coverage( g.Card ) ) ).ToList();
		All.Clear();
		foreach ( var item in _all )
			All.Add( item );
		Filter();
	}

	/// <summary>How much of the card runs on blocks in the game (as its file holds them): "3/3", or "-" for a card with no effects.</summary>
	static string Coverage( CardDefinition card )
	{
		try
		{
			return CoverageOf( card );
		}
		catch ( Exception e )
		{
			Services.Log.Write( $"  {card.Name}: its blocks couldn't be read ({e.GetType().Name}: {e.Message})" );
			return "?";
		}
	}

	static string CoverageOf( CardDefinition card )
	{
		var effects = ChaosSpells.Rules.EffectEngine.Of( card ).Where( e => e.Section is not (EffectSection.Flavor or EffectSection.ImportantTag or EffectSection.Considered) ).ToList();
		return effects.Count == 0 ? "-" : $"{effects.Count( e => e.HasOperations )}/{effects.Count}";
	}

	partial void OnQueryChanged( string value ) => Filter();

	/// <summary>A card the words match; a card the game's search can't read still matches by name (and is logged).</summary>
	static bool Matches( BrowseItem item, string query )
	{
		if ( item.Name.Contains( query, StringComparison.OrdinalIgnoreCase ) )
			return true;
		try
		{
			return CardSearch.Matches( item.Card, query );
		}
		catch ( Exception e )
		{
			Services.Log.Write( $"Search \"{query}\" failed on {item.Name}: {e.GetType().Name}: {e.Message}" );
			return false;
		}
	}

	/// <summary>
	/// The toolbar search bar: every game card the words match (the game's own search words, so "cost:2 fire" works),
	/// names that start with the words first, then names that hold them, then cards matched by text, type or Mastery.
	/// </summary>
	public List<BrowseItem> Search( string query, int max = 60 )
	{
		if ( string.IsNullOrWhiteSpace( query ) )
			return new();
		var q = query.Trim();
		return _all
			.Where( i => Matches( i, q ) )
			.Select( ( item, order ) => (item, order, rank:
				item.Name.Equals( q, StringComparison.OrdinalIgnoreCase ) ? 0 :
				item.Name.StartsWith( q, StringComparison.OrdinalIgnoreCase ) ? 1 :
				item.Name.Contains( q, StringComparison.OrdinalIgnoreCase ) ? 2 : 3) )
			.OrderBy( x => x.rank ).ThenBy( x => x.order )
			.Take( max )
			.Select( x => x.item )
			.ToList();
	}

	void Filter()
	{
		Results.Clear();
		foreach ( var item in _all.Where( i => string.IsNullOrWhiteSpace( Query ) || Matches( i, Query.Trim() ) ) )
			Results.Add( item );
		Summary = $"{Results.Count} of {_all.Count} cards";
	}
}
