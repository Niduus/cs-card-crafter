using ChaosSpells.Cards;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CsCardCrafter.Core.Text;

namespace CsCardCrafter.App.ViewModels;

/// <summary>
/// One row of the event sheet: an effect printed on the card, where it works (Hand, Field, Combat, Abyss...), how often,
/// its five block columns, and its text: typed by hand, or suggested from the blocks.
/// </summary>
public sealed partial class EffectViewModel : ObservableObject
{
	readonly MainViewModel _main;

	public CardEffect Effect { get; }
	public SlotViewModel[] Slots { get; }

	public static EffectSection[] Sections { get; } = Enum.GetValues<EffectSection>();
	public static EffectUsage[] Usages { get; } = Enum.GetValues<EffectUsage>();
	public static EffectUsageScope[] Scopes { get; } = Enum.GetValues<EffectUsageScope>();

	[ObservableProperty] string _suggested = "";
	[ObservableProperty] int _number;

	public EffectViewModel( MainViewModel main, CardEffect effect )
	{
		_main = main;
		Effect = effect;
		Slots = Enum.GetValues<EffectSlot>().Select( s => new SlotViewModel( this, s ) ).ToArray();
		Refresh();
	}

	public EffectSection Section
	{
		get => Effect.Section;
		set { if ( Effect.Section != value ) { Effect.Section = value; OnPropertyChanged(); Changed(); } }
	}

	public EffectUsage Usage
	{
		get => Effect.Usage;
		set { if ( Effect.Usage != value ) { Effect.Usage = value; OnPropertyChanged(); Changed(); } }
	}

	public EffectUsageScope UsageScope
	{
		get => Effect.UsageScope;
		set { if ( Effect.UsageScope != value ) { Effect.UsageScope = value; OnPropertyChanged(); Changed(); } }
	}

	/// <summary>The text this effect prints. Empty means the suggested text is used.</summary>
	public string Text
	{
		get => Effect.Text;
		set { if ( Effect.Text != value ) { Effect.Text = value ?? ""; OnPropertyChanged(); OnPropertyChanged( nameof( UsesSuggestion ) ); Changed(); } }
	}

	/// <summary>Whether the printed text is the one written from the blocks.</summary>
	public bool UsesSuggestion => string.IsNullOrWhiteSpace( Effect.Text ) || Effect.Text.Trim() == Suggested;

	public void Changed()
	{
		Refresh();
		_main.EffectsChanged();
	}

	public void Refresh()
	{
		Suggested = Effect.HasOperations ? EffectWriter.Write( _main.Card, Effect ) : "";
		OnPropertyChanged( nameof( UsesSuggestion ) );
	}

	[RelayCommand]
	void UseSuggested()
	{
		if ( Suggested.Length > 0 )
			Text = Suggested;
	}

	[RelayCommand] void Remove() => _main.RemoveEffect( this );
	[RelayCommand] void MoveUp() => _main.MoveEffect( this, -1 );
	[RelayCommand] void MoveDown() => _main.MoveEffect( this, 1 );
}
