using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using ChaosSpells.Cards;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CsCardCrafter.App.Services;
using CsCardCrafter.Core.Cards;
using CsCardCrafter.Core.Checks;
using CsCardCrafter.Core.Text;

namespace CsCardCrafter.App.ViewModels;

/// <summary>
/// The card being made and everything the window shows about it. Card fields write straight into the game's
/// <see cref="CardDefinition"/>; every change bumps <see cref="Revision"/> so the face redraws, re-runs the checks and,
/// with auto text on, rewrites the description from the effects.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
	readonly Settings _settings = Settings.Load();
	bool _loading;

	public BrowseViewModel Browse { get; } = new();
	public ObservableCollection<EffectViewModel> Effects { get; } = new();
	public ObservableCollection<CostViewModel> Costs { get; } = new();
	public ObservableCollection<CheckResult> Checks { get; } = new();
	public ObservableCollection<KeywordInfo> KeywordHelp { get; } = new();

	public static string[] Masteries => CardParts.Masteries;
	public static CardTypeInfo[] Types => CardParts.Types;
	public static DeckKind[] Decks { get; } = Enum.GetValues<DeckKind>();
	public static CombatTarget[] CombatTargets { get; } = Enum.GetValues<CombatTarget>();

	[ObservableProperty] CardDefinition _card;
	[ObservableProperty] int _revision;
	[ObservableProperty] GameFolder _game;
	[ObservableProperty] string _gameStatus = "";
	[ObservableProperty] string _filePath;
	[ObservableProperty] string _status = "";
	[ObservableProperty] string _json = "";
	[ObservableProperty] bool _dirty;

	/// <summary>Where the card lives in the game folder, when it was opened from there (Install writes it back there).</summary>
	[ObservableProperty] string _gamePath;

	/// <summary>The game card being edited, as the card picker shows it (null for a new card or one from elsewhere).</summary>
	[ObservableProperty] BrowseItem _picked;

	/// <summary>Points the card picker at the card being edited, after a load or after the card list was re-read.</summary>
	void SyncPicked() =>
		Picked = GamePath is null ? null : Browse.All.FirstOrDefault( i => string.Equals( Path.GetFullPath( i.Game.Path ), Path.GetFullPath( GamePath ), StringComparison.OrdinalIgnoreCase ) );

	/// <summary>On: the description is written from the effects on every change. Off: it is typed on the Text tab.</summary>
	[ObservableProperty] bool _autoText = true;

	public MainViewModel()
	{
		SetGame( GameFolder.At( _settings.GameFolder ) ?? GameFolder.Find() ?? GameFolder.Find( Environment.CurrentDirectory ) );
		Load( CardParts.NewCard(), null, null, autoText: true );
	}

	public string Title => $"CS Card Crafter - {(string.IsNullOrEmpty( Card?.Name ) ? "Untitled" : Card.Name)}{(Dirty ? " *" : "")}";

	// ---- Game folder -------------------------------------------------------------------------------------------

	public void SetGame( GameFolder game )
	{
		Game = game;
		Images.Game = game;
		OnPropertyChanged( nameof( HasGame ) );
		GameStatus = game is null ? "Chaos Spells game folder not found. Choose it to see art and cards." : $"Game: {game.Root}";
		if ( game is not null )
		{
			_settings.GameFolder = game.Root;
			_settings.Save();
		}
		Browse.Load( game );
		SyncPicked();
		Revision++;
	}

	public bool HasGame => Game is not null;

	public bool TrySetGame( string folder )
	{
		// Any folder inside the game works too (Assets/cards, a collection folder): the game is found above it.
		var game = GameFolder.At( folder ) ?? GameFolder.Find( folder );
		if ( game is null )
		{
			Status = $"Couldn't find the Chaos Spells game in {folder} or above it (no {GameFolder.ProjectFile}). Choose your chaos_spells_sbox folder.";
			return false;
		}
		SetGame( game );
		Status = "Game folder set.";
		return true;
	}

	// ---- Loading and saving --------------------------------------------------------------------------------------

	void Load( CardDefinition card, string file, string gamePath, bool autoText )
	{
		_loading = true;
		Card = card;
		FilePath = file;
		GamePath = gamePath;
		AutoText = autoText;
		Effects.Clear();
		foreach ( var effect in card.Effects )
			Effects.Add( new EffectViewModel( this, effect ) );
		Costs.Clear();
		foreach ( var cost in card.Costs )
			Costs.Add( new CostViewModel( this, cost ) );
		_loading = false;
		foreach ( var name in CardFields )
			OnPropertyChanged( name );
		Renumber();
		CardChanged();
		Dirty = false;
		TextNote = "";
		SyncPicked();
	}

	[RelayCommand]
	void New()
	{
		Load( CardParts.NewCard( Card?.Mastery is { Length: > 0 } m ? m : "Fire" ), null, null, autoText: true );
		Status = "New card.";
	}

	public void OpenFile( string path )
	{
		try
		{
			var card = CardFile.Load( path );
			// A card opened from inside the game (Assets/cards/...) also sets the game folder, which fills the card
			// picker and gives the face its art and icons.
			if ( (Game is null || !Path.GetFullPath( path ).StartsWith( Path.GetFullPath( Game.Root ), StringComparison.OrdinalIgnoreCase ))
				&& GameFolder.Find( Path.GetDirectoryName( Path.GetFullPath( path ) ) ) is { } found )
				SetGame( found );
			var inGame = Game is not null && Path.GetFullPath( path ).StartsWith( Path.GetFullPath( Game.CardsFolder ), StringComparison.OrdinalIgnoreCase );
			// A card that already has text keeps it, as written (oracle law: the designer's wording wins).
			Load( card, path, inGame ? path : null, autoText: string.IsNullOrWhiteSpace( card.Description ) );
			_settings.AddRecent( path );
			Status = $"Opened {Path.GetFileName( path )}.";
		}
		catch ( Exception e )
		{
			Status = $"Couldn't open {Path.GetFileName( path )}: {e.Message}";
		}
	}

	/// <summary>Opens a game card from the browser to edit it in place.</summary>
	[RelayCommand]
	void OpenSelected()
	{
		if ( Browse.Selected is { } item )
			OpenFile( item.Game.Path );
	}

	/// <summary>Starts a new card from a game card: same everything, new name, no ID.</summary>
	[RelayCommand]
	void CopySelected()
	{
		if ( Browse.Selected is not { } item )
			return;
		var copy = CardFile.Copy( item.Card );
		copy.Name = item.Card.Name + " (copy)";
		copy.Id = 0;
		copy.Collection = "";
		Load( copy, null, null, autoText: false );
		Dirty = true;
		Status = $"New card from {item.Card.Name}.";
	}

	public bool Save( string path )
	{
		try
		{
			PrepareForSave();
			CardFile.Save( Card, path );
			FilePath = path;
			if ( Game is not null && Path.GetFullPath( path ).StartsWith( Path.GetFullPath( Game.CardsFolder ), StringComparison.OrdinalIgnoreCase ) )
			{
				// A game card: the picker and browser show the saved values straight away.
				GamePath = path;
				Browse.Load( Game );
				SyncPicked();
			}
			_settings.AddRecent( path );
			Dirty = false;
			Status = $"Saved {Path.GetFileName( path )}.";
			return true;
		}
		catch ( Exception e )
		{
			Status = $"Couldn't save: {e.Message}";
			return false;
		}
	}

	/// <summary>On: "Add to game" also commits the card to the game folder's git, ready to push.</summary>
	[ObservableProperty] bool _commitToGit = true;

	/// <summary>
	/// Adds the card to the game as an official card: its collection folder, the next free ID, its art copied into
	/// Assets/cards/art and the export list checked. Optionally commits it to the game's git.
	/// </summary>
	[RelayCommand]
	void Install()
	{
		if ( Game is null )
		{
			Status = "Choose the Chaos Spells game folder first.";
			return;
		}
		try
		{
			PrepareForSave();
			var result = Game.Publish( Card, GamePath );
			_loading = true;
			Card.Id = result.Card.Id;
			Card.Collection = result.Card.Collection;
			Card.IsCustom = false;
			Card.Artwork = result.Card.Artwork;
			_loading = false;
			OnPropertyChanged( nameof( Id ) );
			OnPropertyChanged( nameof( Artwork ) );
			GamePath = result.CardFile;
			FilePath = result.CardFile;
			Browse.Load( Game );
			SyncPicked();
			var note = $"Added to the game as #{result.Card.Id} in {result.Card.Collection}: {Game.AssetPath( result.CardFile )}.";
			if ( result.ExportListFixed )
				note += " The project's export list had lost cards/*, so it was put back.";
			if ( CommitToGit )
			{
				var git = Game.Commit( result );
				note += git is null ? " (No git found, so nothing was committed.)" : " Committed to the game's git; push it when you're ready.";
			}
			CardChanged();
			Dirty = false;
			Status = note;
		}
		catch ( Exception e )
		{
			_loading = false;
			Status = $"Couldn't add it to the game: {e.Message}";
		}
	}

	void PrepareForSave()
	{
		if ( AutoText )
			Card.Description = CardWriter.Description( Card );
	}

	public void SetArtwork( string file )
	{
		Card.Artwork = Game is not null && Path.GetFullPath( file ).StartsWith( Path.GetFullPath( Game.Assets ), StringComparison.OrdinalIgnoreCase )
			? Game.AssetPath( file )
			: file;
		Images.Forget( file );
		OnPropertyChanged( nameof( Artwork ) );
		CardChanged();
	}

	// ---- Changes -----------------------------------------------------------------------------------------------

	/// <summary>Called after any edit: rewrites the text (auto text), redraws, re-checks.</summary>
	public void CardChanged()
	{
		if ( _loading || Card is null )
			return;
		if ( AutoText )
		{
			Card.Description = CardWriter.Description( Card );
			OnPropertyChanged( nameof( Description ) );
		}
		foreach ( var e in Effects )
			e.Refresh();
		Checks.Clear();
		foreach ( var c in CardChecks.Run( Card, Game ) )
			Checks.Add( c );
		KeywordHelp.Clear();
		foreach ( var (name, meaning) in CardKeywords.For( Card ) )
			KeywordHelp.Add( new KeywordInfo( name, meaning ) );
		Json = CardFile.Write( Card );
		Dirty = true;
		Revision++;
		OnPropertyChanged( nameof( Title ) );
		OnPropertyChanged( nameof( SuggestedDescription ) );
	}

	public void EffectsChanged() => CardChanged();

	partial void OnAutoTextChanged( bool value )
	{
		if ( value ) TextNote = "";
		CardChanged();
	}
	partial void OnDirtyChanged( bool value ) => OnPropertyChanged( nameof( Title ) );

	/// <summary>The description the effects would write, shown next to a hand-typed one.</summary>
	public string SuggestedDescription => Card is null ? "" : CardText.Plain( CardWriter.Description( Card ), Card.Name );

	[RelayCommand]
	void WriteTextFromEffects()
	{
		CardWriter.Apply( Card );
		foreach ( var e in Effects )
			OnPropertyChangedFor( e );
		OnPropertyChanged( nameof( Description ) );
		CardChanged();
		Status = "Card text written from the effects.";
	}

	static void OnPropertyChangedFor( EffectViewModel e ) => e.Text = e.Effect.Text;

	// ---- Effects -----------------------------------------------------------------------------------------------

	[RelayCommand]
	void AddEffect( string section )
	{
		// An innate condition is an Important Tag effect holding only IF blocks ("Activate only if ...").
		var effect = new CardEffect { Section = section == "Innate" ? EffectSection.ImportantTag : Enum.TryParse<EffectSection>( section, out var s ) ? s : EffectSection.Field };
		Card.Effects.Add( effect );
		Effects.Add( new EffectViewModel( this, effect ) );
		Renumber();
		CardChanged();
	}

	public void RemoveEffect( EffectViewModel effect )
	{
		Card.Effects.Remove( effect.Effect );
		Effects.Remove( effect );
		Renumber();
		CardChanged();
	}

	public void MoveEffect( EffectViewModel effect, int by )
	{
		var i = Effects.IndexOf( effect );
		var j = i + by;
		if ( i < 0 || j < 0 || j >= Effects.Count )
			return;
		Effects.Move( i, j );
		Card.Effects.RemoveAt( i );
		Card.Effects.Insert( j, effect.Effect );
		Renumber();
		CardChanged();
	}

	void Renumber()
	{
		for ( var i = 0; i < Effects.Count; i++ )
			Effects[i].Number = i + 1;
	}

	// ---- Costs -------------------------------------------------------------------------------------------------

	[RelayCommand]
	void AddCost()
	{
		if ( Card.Costs.Count >= 3 )
		{
			Status = "The card face shows at most 3 costs.";
			return;
		}
		var cost = new CardCost { Icon = CardParts.Costs[0].Icon, Amount = "1" };
		Card.Costs.Add( cost );
		Costs.Add( new CostViewModel( this, cost ) );
		CardChanged();
	}

	public void RemoveCost( CostViewModel cost )
	{
		Card.Costs.Remove( cost.Cost );
		Costs.Remove( cost );
		CardChanged();
	}

	// ---- Card fields -------------------------------------------------------------------------------------------

	static readonly string[] CardFields =
	{
		nameof( Name ), nameof( Id ), nameof( Mastery ), nameof( DuoMasteries ), nameof( CardType ), nameof( ExtraTypes ), nameof( Deck ),
		nameof( MaxCopies ), nameof( Combat ), nameof( CombatTarget ), nameof( BladeValue ), nameof( MantleValue ), nameof( ShieldValue ),
		nameof( TrapValue ), nameof( LifestealFlat ), nameof( LifestealPercent ), nameof( SpiritLimit ), nameof( SpiritUses ),
		nameof( PermanentAnchored ), nameof( AnchoredRounds ), nameof( Keywords ), nameof( Series ), nameof( OnlyOne ), nameof( NoReshuffle ),
		nameof( FusionMark ), nameof( FooterLeft ), nameof( FooterRight ), nameof( Artwork ), nameof( Description ), nameof( IsDev ), nameof( Title ),
	};

	bool Set<T>( T current, T value, Action<T> write, bool look = false, [CallerMemberName] string name = null )
	{
		// A list that can't show the card's value (a Mastery or type it doesn't name) writes back null when a card is
		// opened. That is never an edit: ignore it, so opening a card never changes it.
		if ( value is null || EqualityComparer<T>.Default.Equals( current, value ) )
			return false;
		write( value );
		if ( look )
		{
			CardParts.ApplyLook( Card );
		}
		OnPropertyChanged( name );
		CardChanged();
		return true;
	}

	/// <summary>
	/// A printed value (Combat, Shield, Anchored...): when the card's text is written by hand, the old number in the
	/// text changes with it, so the face shows the new value. It is changed only where it can't be mistaken:
	/// "Combat: 600" for Combat, otherwise the number when it appears exactly once. Anything else is left to the
	/// designer, with a note under the face.
	/// </summary>
	bool SetValue<T>( T current, T value, Action<T> write, string lead = null, [CallerMemberName] string name = null )
	{
		if ( !Set( current, value, write, name: name ) )
			return false;
		var from = Convert.ToString( current, System.Globalization.CultureInfo.InvariantCulture ) ?? "";
		var to = Convert.ToString( value, System.Globalization.CultureInfo.InvariantCulture ) ?? "";
		if ( AutoText || string.IsNullOrWhiteSpace( from ) || from == "0" || string.IsNullOrEmpty( Card.Description ) )
			return true;
		var text = Card.Description;
		var number = $@"(?<![\d.,]){System.Text.RegularExpressions.Regex.Escape( from )}(?![\d%])";
		string updated = null;
		if ( lead is not null && System.Text.RegularExpressions.Regex.Matches( text, System.Text.RegularExpressions.Regex.Escape( lead ) + number ).Count == 1 )
			updated = System.Text.RegularExpressions.Regex.Replace( text, System.Text.RegularExpressions.Regex.Escape( lead ) + number, lead + to );
		else if ( System.Text.RegularExpressions.Regex.Matches( text, number ).Count == 1 )
			updated = System.Text.RegularExpressions.Regex.Replace( text, number, to );
		if ( updated is null )
		{
			TextNote = $"The card's text is written by hand and couldn't be matched to {from}: change it on the Text tab, or tick \"Write the card text from the effects\".";
			return true;
		}
		Card.Description = updated;
		OnPropertyChanged( nameof( Description ) );
		CardChanged();
		return true;
	}

	/// <summary>Shown under the face when an edit couldn't reach the hand-written text.</summary>
	[ObservableProperty] string _textNote = "";

	public string Name { get => Card?.Name ?? ""; set => Set( Name, value ?? "", v => Card.Name = v ); }
	public int Id { get => Card?.Id ?? 0; set => Set( Id, value, v => Card.Id = v ); }
	public string Mastery { get => Card?.Mastery ?? ""; set => Set( Mastery, value, v => Card.Mastery = v, look: true ); }

	/// <summary>Duo cards (Kitharas) portray two more Masteries: "Crimson, Life". Combat hits in the last one.</summary>
	public string DuoMasteries { get => string.Join( ", ", Card?.DuoMasteries ?? new() ); set => Set( DuoMasteries, value ?? "", v => Card.DuoMasteries = SplitList( v ) ); }

	public CardTypeInfo CardType
	{
		get => CardParts.Type( Card?.Types.FirstOrDefault() ?? "" );
		set => Set( CardType, value, v =>
		{
			if ( v is null ) return;
			if ( Card.Types.Count == 0 ) Card.Types.Add( v.Id ); else Card.Types[0] = v.Id;
		}, look: true );
	}

	/// <summary>Further types after the first, such as Token.</summary>
	public string ExtraTypes { get => string.Join( ", ", Card?.Types.Skip( 1 ) ?? Enumerable.Empty<string>() ); set => Set( ExtraTypes, value ?? "", v => Card.Types = Card.Types.Take( 1 ).Concat( SplitList( v ) ).ToList(), look: true ); }

	public DeckKind Deck { get => Card?.Deck ?? DeckKind.Main; set => Set( Deck, value, v => Card.Deck = v, look: true ); }
	public int MaxCopies { get => Card?.MaxCopies ?? 0; set => Set( MaxCopies, value, v => Card.MaxCopies = v ); }
	public string Combat { get => Card?.Combat ?? ""; set => SetValue( Combat, value ?? "", v => Card.Combat = v, "Combat: " ); }
	public CombatTarget CombatTarget { get => Card?.CombatTarget ?? default; set => Set( CombatTarget, value, v => Card.CombatTarget = v ); }
	public int BladeValue { get => Card?.BladeValue ?? 0; set => SetValue( BladeValue, value, v => Card.BladeValue = v ); }
	public int MantleValue { get => Card?.MantleValue ?? 0; set => SetValue( MantleValue, value, v => Card.MantleValue = v ); }
	public int ShieldValue { get => Card?.ShieldValue ?? 0; set => SetValue( ShieldValue, value, v => Card.ShieldValue = v ); }
	public int TrapValue { get => Card?.TrapValue ?? 0; set => SetValue( TrapValue, value, v => Card.TrapValue = v ); }
	public int LifestealFlat { get => Card?.LifestealFlat ?? 0; set => SetValue( LifestealFlat, value, v => Card.LifestealFlat = v ); }
	public int LifestealPercent { get => Card?.LifestealPercent ?? 0; set => SetValue( LifestealPercent, value, v => Card.LifestealPercent = v ); }

	/// <summary>A Spirit card's point cost for the Spirit Deck ("2"), up to 6 points a deck.</summary>
	public string SpiritLimit { get => Card?.SpiritLimit ?? ""; set => Set( SpiritLimit, value ?? "", v => Card.SpiritLimit = v ); }
	public int SpiritUses { get => Card?.SpiritUses ?? 0; set => Set( SpiritUses, value, v => Card.SpiritUses = v ); }
	public bool PermanentAnchored { get => Card?.PermanentAnchored ?? false; set => Set( PermanentAnchored, value, v => Card.PermanentAnchored = v ); }
	public int AnchoredRounds { get => Card?.AnchoredRounds ?? 0; set => SetValue( AnchoredRounds, value, v => Card.AnchoredRounds = v ); }
	public string Keywords { get => string.Join( ", ", Card?.Keywords ?? new() ); set => Set( Keywords, value ?? "", v => Card.Keywords = SplitList( v ) ); }
	public string Series { get => string.Join( ", ", Card?.Series ?? new() ); set => Set( Series, value ?? "", v => Card.Series = SplitList( v ) ); }
	public bool OnlyOne { get => !string.IsNullOrEmpty( Card?.OnlyIcon ); set => Set( OnlyOne, value, v => Card.OnlyIcon = v ? CardParts.OnlyOneIcon : "" ); }
	public bool NoReshuffle { get => !string.IsNullOrEmpty( Card?.ReshuffleIcon ); set => Set( NoReshuffle, value, v => Card.ReshuffleIcon = v ? CardParts.NoReshuffleIcon : "" ); }
	public bool FusionMark { get => !string.IsNullOrEmpty( Card?.FusionIcon ); set => Set( FusionMark, value, v => Card.FusionIcon = v ? CardParts.FusionIcon : "" ); }
	public string FooterLeft { get => Card?.FooterLeft ?? ""; set => Set( FooterLeft, value ?? "", v => Card.FooterLeft = v ); }
	public string FooterRight { get => Card?.FooterRight ?? ""; set => Set( FooterRight, value ?? "", v => Card.FooterRight = v ); }
	public string Artwork => Card?.Artwork ?? "";
	public bool IsDev { get => Card?.IsDev ?? false; set => Set( IsDev, value, v => Card.IsDev = v ); }

	/// <summary>The printed text with its {BEG}…{END} paragraph marks. Typing here turns auto text off.</summary>
	public string Description
	{
		get => Card?.Description ?? "";
		set
		{
			if ( value == Description ) return;
			if ( !_loading ) { AutoText = false; TextNote = ""; }
			Set( Description, value ?? "", v => Card.Description = v );
		}
	}

	static List<string> SplitList( string text ) =>
		(text ?? "").Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ).ToList();
}

public sealed record KeywordInfo( string Name, string Meaning );
