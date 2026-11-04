using System.Collections.ObjectModel;
using ChaosSpells.Cards;
using ChaosSpells.Rules;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CsCardCrafter.Core.Cards;
using CsCardCrafter.Core.Text;

namespace CsCardCrafter.App.ViewModels;

/// <summary>One blank of a block: "Amount", "Who", "Mastery"... edited in place, written straight into the card.</summary>
public sealed partial class ParamViewModel : ObservableObject
{
	readonly OperationBlock _op;
	readonly Action _changed;

	public BlockParam Param { get; }
	public string Label => Param.Label;
	public IReadOnlyList<string> Choices { get; }
	public bool HasChoices => Choices.Count > 0 && !IsFlag && !IsPlaces;

	/// <summary>A places blank (hand, field, Abyss...): one tick box per place, any number ticked.</summary>
	public bool IsPlaces => Param.Kind == ParamKind.Places;
	public IReadOnlyList<PlaceOption> PlaceOptions { get; }

	/// <summary>An on/off option ("Non-Fusion only"): a tick box, never a list of "true" and "false".</summary>
	public bool IsFlag => Choices.Count == 2 && Choices.Contains( "true" ) && Choices.Contains( "false" );

	/// <summary>Neither a list nor a tick box: typed.</summary>
	public bool IsTyped => Choices.Count == 0;

	public bool On
	{
		get => Value == "true";
		set
		{
			Value = value ? "true" : "false";
			OnPropertyChanged();
		}
	}

	public ParamViewModel( OperationBlock op, BlockParam param, Action changed )
	{
		_op = op;
		_changed = changed;
		Param = param;
		Choices = param.Kind switch
		{
			ParamKind.Who => Who.All,
			ParamKind.Mastery => new[] { "" }.Concat( CardParts.Masteries ).ToArray(),
			ParamKind.Card => param.Choices ?? CardRef.All,
			ParamKind.Places => param.Choices ?? Places.All,
			_ => param.Choices ?? Array.Empty<string>(),
		};
		PlaceOptions = IsPlaces ? Choices.Select( p => new PlaceOption( this, p ) ).ToList() : Array.Empty<PlaceOption>();
	}

	/// <summary>Ticks or unticks one place and stores the set ("hand, field").</summary>
	internal void SetPlace( string place, bool on )
	{
		var set = Places.Parse( Value );
		set.RemoveAll( p => p.Equals( place, StringComparison.OrdinalIgnoreCase ) );
		if ( on )
			set.Add( place );
		Value = Places.Join( set );
	}

	public string Value
	{
		get => _op.Params.TryGetValue( Param.Key, out var v ) ? v : Param.Default ?? "";
		set
		{
			// null comes from a list that can't show the value (one the game's choices don't name): keep the value.
			if ( value is null || value == Value )
				return;
			_op.Params[Param.Key] = value;
			OnPropertyChanged();
			_changed();
		}
	}
}

/// <summary>One tick box of a places blank: "hand", "field", "Abyss"...</summary>
public sealed partial class PlaceOption : ObservableObject
{
	readonly ParamViewModel _param;

	public string Name { get; }

	public PlaceOption( ParamViewModel param, string name )
	{
		_param = param;
		Name = name;
	}

	public bool IsChecked
	{
		get => Places.Parse( _param.Value ).Contains( Name, StringComparer.OrdinalIgnoreCase );
		set
		{
			if ( value == IsChecked )
				return;
			_param.SetPlace( Name, value );
			OnPropertyChanged();
		}
	}
}

/// <summary>One block in a slot: its name, how it reads on the card, and its blanks.</summary>
public sealed partial class BlockViewModel : ObservableObject
{
	readonly SlotViewModel _slot;

	public OperationBlock Op { get; }
	public Block Block { get; }
	public string Name => Block?.Name ?? Op.Op;
	public string Id => Op.Op;
	public bool Known => Block is not null;
	public ObservableCollection<ParamViewModel> Params { get; } = new();

	[ObservableProperty] string _reads = "";

	public BlockViewModel( SlotViewModel slot, OperationBlock op )
	{
		_slot = slot;
		Op = op;
		Block = BlockRegistry.Get( op.Op );
		foreach ( var p in Block?.Params ?? Array.Empty<BlockParam>() )
			Params.Add( new ParamViewModel( op, p, slot.Changed ) );
		Refresh();
	}

	public void Refresh() => Reads = Known ? OracleText.Of( Op ) : $"{Op.Op} (the game has no block by this name)";

	[RelayCommand] void Remove() => _slot.Remove( this );
	[RelayCommand] void MoveUp() => _slot.Move( this, -1 );
	[RelayCommand] void MoveDown() => _slot.Move( this, 1 );
}

/// <summary>A group of blocks in the "+ Add" menu, such as Health or Boons.</summary>
public sealed record BlockGroup( string Name, IReadOnlyList<Block> Blocks );

/// <summary>One column of an effect: WHEN, IF, COST, THEN or ALWAYS.</summary>
public sealed partial class SlotViewModel : ObservableObject
{
	readonly EffectViewModel _effect;

	public EffectSlot Slot { get; }
	public string Title => Slot.ToString().ToUpperInvariant();
	public string Hint => Slot switch
	{
		EffectSlot.When => "What starts it",
		EffectSlot.If => "What must be true",
		EffectSlot.Cost => "What you pay",
		EffectSlot.Then => "What happens, in order",
		_ => "Lasting while the card is out",
	};
	public ObservableCollection<BlockViewModel> Blocks { get; } = new();

	/// <summary>The blocks this column offers, grouped as the game groups them. IF also offers THEN's gates and vice versa.</summary>
	public IReadOnlyList<BlockGroup> Groups { get; }

	List<OperationBlock> List => _effect.Effect.GetSlot( Slot );

	public SlotViewModel( EffectViewModel effect, EffectSlot slot )
	{
		_effect = effect;
		Slot = slot;
		Groups = BlockRegistry.All
			.Where( b => b.Slot == slot )
			.GroupBy( b => b.Group )
			.OrderBy( g => g.Key )
			.Select( g => new BlockGroup( g.Key, g.OrderBy( b => b.Name ).ToList() ) )
			.ToList();
		foreach ( var op in List )
			Blocks.Add( new BlockViewModel( this, op ) );
	}

	public void Changed()
	{
		foreach ( var b in Blocks )
			b.Refresh();
		_effect.Changed();
	}

	public void Add( Block block )
	{
		var op = block.Make( block.Params.Where( p => !string.IsNullOrEmpty( p.Default ) ).Select( p => (p.Key, p.Default) ).ToArray() );
		List.Add( op );
		Blocks.Add( new BlockViewModel( this, op ) );
		Changed();
	}

	public void Remove( BlockViewModel block )
	{
		List.Remove( block.Op );
		Blocks.Remove( block );
		Changed();
	}

	public void Move( BlockViewModel block, int by )
	{
		var i = Blocks.IndexOf( block );
		var j = i + by;
		if ( i < 0 || j < 0 || j >= Blocks.Count )
			return;
		Blocks.Move( i, j );
		List.RemoveAt( i );
		List.Insert( j, block.Op );
		Changed();
	}
}
