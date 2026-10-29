using ChaosSpells.Cards;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CsCardCrafter.Core.Cards;

namespace CsCardCrafter.App.ViewModels;

/// <summary>One printed cost: which kind (its icon) and the amount on the badge.</summary>
public sealed partial class CostViewModel : ObservableObject
{
	readonly MainViewModel _main;
	public CardCost Cost { get; }
	public static CostKind[] Kinds => CardParts.Costs;

	public CostViewModel( MainViewModel main, CardCost cost )
	{
		_main = main;
		Cost = cost;
	}

	public CostKind Kind
	{
		get => CardParts.CostOf( Cost );
		set { if ( value is not null && Cost.Icon != value.Icon ) { Cost.Icon = value.Icon; OnPropertyChanged(); _main.CardChanged(); } }
	}

	public string Amount
	{
		get => Cost.Amount;
		set { if ( Cost.Amount != value ) { Cost.Amount = value ?? ""; OnPropertyChanged(); _main.CardChanged(); } }
	}

	[RelayCommand] void Remove() => _main.RemoveCost( this );
}
