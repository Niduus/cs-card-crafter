using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CsCardCrafter.App.ViewModels;
using CsCardCrafter.Core.Cards;

namespace CsCardCrafter.App.Views;

public partial class MainWindow : Window
{
	public MainWindow()
	{
		InitializeComponent();
		CardSearchBox.AddHandler( KeyDownEvent, CardSearch_PreviewKeyDown, RoutingStrategies.Tunnel );
	}

	MainViewModel Vm => (MainViewModel)DataContext!;

	static readonly FilePickerFileType CardFiles = new( "Chaos Spells card" ) { Patterns = new[] { "*" + CardFile.Extension } };
	static readonly FilePickerFileType ImageFiles = new( "Images" ) { Patterns = new[] { "*.png", "*.jpg", "*.jpeg" } };

	async Task<IStorageFolder> StartFolder( string path ) =>
		string.IsNullOrEmpty( path ) || !Directory.Exists( path ) ? null : await StorageProvider.TryGetFolderFromPathAsync( path );

	async void Open_Click( object sender, RoutedEventArgs e )
	{
		var files = await StorageProvider.OpenFilePickerAsync( new FilePickerOpenOptions
		{
			Title = "Open a card",
			FileTypeFilter = new[] { CardFiles },
			SuggestedStartLocation = await StartFolder( Vm.Game?.CardsFolder ),
		} );
		if ( files.Count > 0 && files[0].TryGetLocalPath() is { } path )
			Vm.OpenFile( path );
	}

	void Save_Click( object sender, RoutedEventArgs e )
	{
		if ( string.IsNullOrEmpty( Vm.FilePath ) )
			SaveAs_Click( sender, e );
		else
			Vm.Save( Vm.FilePath );
	}

	async void SaveAs_Click( object sender, RoutedEventArgs e )
	{
		var file = await StorageProvider.SaveFilePickerAsync( new FilePickerSaveOptions
		{
			Title = "Save the card",
			SuggestedFileName = CardFile.FileName( Vm.Name ),
			DefaultExtension = CardFile.Extension,
			FileTypeChoices = new[] { CardFiles },
		} );
		if ( file?.TryGetLocalPath() is { } path )
			Vm.Save( path );
	}

	async void Artwork_Click( object sender, RoutedEventArgs e )
	{
		var files = await StorageProvider.OpenFilePickerAsync( new FilePickerOpenOptions
		{
			Title = "Choose the card's artwork",
			FileTypeFilter = new[] { ImageFiles },
			SuggestedStartLocation = await StartFolder( Vm.Game?.ArtFolder ),
		} );
		if ( files.Count > 0 && files[0].TryGetLocalPath() is { } path )
			Vm.SetArtwork( path );
	}

	async void GameFolder_Click( object sender, RoutedEventArgs e )
	{
		var folders = await StorageProvider.OpenFolderPickerAsync( new FolderPickerOpenOptions { Title = "Choose your chaos_spells_sbox folder (or its Assets/cards folder)" } );
		if ( folders.Count > 0 && folders[0].TryGetLocalPath() is { } path )
			Vm.TrySetGame( path );
	}

	/// <summary>"+ Add" under a column: a menu of that column's blocks, grouped as the game groups them.</summary>
	void AddBlock_Click( object sender, RoutedEventArgs e )
	{
		if ( sender is not Button { DataContext: SlotViewModel slot } button )
			return;
		var flyout = new MenuFlyout();
		foreach ( var group in slot.Groups )
		{
			var item = new MenuItem { Header = group.Name };
			foreach ( var block in group.Blocks )
			{
				var entry = new MenuItem { Header = block.Name };
				ToolTip.SetTip( entry, block.Sentence );
				entry.Click += ( _, _ ) => slot.Add( block );
				item.Items.Add( entry );
			}
			flyout.Items.Add( item );
		}
		flyout.ShowAt( button );
	}

	/// <summary>The card picker: opens the chosen game card, asking first when the current card has unsaved changes.</summary>
	async void CardPicker_SelectionChanged( object sender, SelectionChangedEventArgs e )
	{
		if ( sender is not ComboBox picker || picker.SelectedItem is not BrowseItem item || item == Vm.Picked )
			return;
		if ( Vm.Dirty )
		{
			var answer = await Ask( $"{(string.IsNullOrEmpty( Vm.Name ) ? "This card" : Vm.Name)} has unsaved changes.", "Save", "Don't save", "Cancel" );
			if ( answer == "Cancel" || answer is null )
			{
				picker.SelectedItem = Vm.Picked;
				return;
			}
			if ( answer == "Save" )
			{
				if ( string.IsNullOrEmpty( Vm.FilePath ) || !Vm.Save( Vm.FilePath ) )
				{
					picker.SelectedItem = Vm.Picked;
					if ( string.IsNullOrEmpty( Vm.FilePath ) )
						Vm.Status = "This card has no file yet: use Save as… or Add to game first.";
					return;
				}
				// Saving re-reads the card list, so find the chosen card again.
				item = Vm.Browse.All.FirstOrDefault( i => i.Game.Path == item.Game.Path ) ?? item;
			}
		}
		Vm.OpenFile( item.Game.Path );
	}

	/// <summary>The search bar's list: the game cards the typed words match, best names first.</summary>
	void CardSearch_Populating( object sender, PopulatingEventArgs e )
	{
		if ( sender is not AutoCompleteBox box )
			return;
		e.Cancel = true;
		try
		{
			var results = Vm.Browse.Search( e.Parameter );
			box.ItemsSource = results;
			if ( results.Count == 0 )
				Vm.Status = Vm.Browse.All.Count == 0 ? "No cards to search: the game folder's Assets/cards has no card files the crafter could read." : $"No card matches \"{e.Parameter}\".";
		}
		catch ( Exception ex )
		{
			CsCardCrafter.App.Services.Log.Write( $"Search \"{e.Parameter}\" failed: {ex}" );
			Vm.Status = $"Search failed: {ex.Message} (details in {CsCardCrafter.App.Services.Log.File})";
		}
		box.PopulateComplete();
	}

	/// <summary>A card picked from the search bar's list (clicked, or highlighted and Enter) opens for editing.</summary>
	void CardSearch_DropDownClosed( object sender, EventArgs e )
	{
		if ( sender is AutoCompleteBox { SelectedItem: BrowseItem item } box )
			OpenFromSearch( box, item );
	}

	/// <summary>Enter with nothing highlighted opens the best match. Runs before the search bar's own keys.</summary>
	void CardSearch_PreviewKeyDown( object sender, KeyEventArgs e )
	{
		if ( e.Key != Key.Enter || sender is not AutoCompleteBox box || box.SelectedItem is BrowseItem )
			return;
		if ( Vm.Browse.Search( box.Text ).FirstOrDefault() is { } best )
		{
			box.IsDropDownOpen = false;
			OpenFromSearch( box, best );
		}
		e.Handled = true;
	}

	bool _openingFromSearch;

	/// <summary>Opens a game card from the search bar, asking first when the current card has unsaved changes.</summary>
	async void OpenFromSearch( AutoCompleteBox box, BrowseItem item )
	{
		if ( _openingFromSearch )
			return;
		_openingFromSearch = true;
		try
		{
			if ( item.Game.Path == Vm.GamePath && !Vm.Dirty )
				return;
			if ( Vm.Dirty )
			{
				var answer = await Ask( $"{(string.IsNullOrEmpty( Vm.Name ) ? "This card" : Vm.Name)} has unsaved changes.", "Save", "Don't save", "Cancel" );
				if ( answer == "Cancel" || answer is null )
					return;
				if ( answer == "Save" )
				{
					if ( string.IsNullOrEmpty( Vm.FilePath ) || !Vm.Save( Vm.FilePath ) )
					{
						if ( string.IsNullOrEmpty( Vm.FilePath ) )
							Vm.Status = "This card has no file yet: use Save as… or Add to game first.";
						return;
					}
					// Saving re-reads the card list, so find the chosen card again.
					item = Vm.Browse.All.FirstOrDefault( i => i.Game.Path == item.Game.Path ) ?? item;
				}
			}
			Vm.OpenFile( item.Game.Path );
		}
		finally
		{
			// Ready for the next search; the card's name is in the title bar and on the face.
			// (after the search bar finishes its own closing, which would put the text back).
			Avalonia.Threading.Dispatcher.UIThread.Post( () =>
			{
				// Clearing the text also clears the chosen item (setting the item to null would put the typed words back).
				box.Text = "";
				_openingFromSearch = false;
			} );
		}
	}

	/// <summary>A small question box; returns the pressed button's text, or null if it was closed.</summary>
	Task<string> Ask( string question, params string[] answers )
	{
		var dialog = new Window
		{
			Title = "CS Card Crafter",
			SizeToContent = SizeToContent.WidthAndHeight,
			CanResize = false,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
		};
		var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
		foreach ( var answer in answers )
		{
			var button = new Button { Content = answer, MinWidth = 90 };
			button.Click += ( _, _ ) => dialog.Close( answer );
			buttons.Children.Add( button );
		}
		dialog.Content = new StackPanel
		{
			Margin = new Avalonia.Thickness( 20 ),
			Spacing = 16,
			Children = { new TextBlock { Text = question, MaxWidth = 420, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, buttons },
		};
		return dialog.ShowDialog<string>( this );
	}

	void Browse_DoubleTapped( object sender, TappedEventArgs e ) => Vm.OpenSelectedCommand.Execute( null );
}
