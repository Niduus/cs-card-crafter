using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CsCardCrafter.App.ViewModels;
using CsCardCrafter.Core.Cards;

namespace CsCardCrafter.App.Views;

public partial class MainWindow : Window
{
	public MainWindow() => InitializeComponent();

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
		var folders = await StorageProvider.OpenFolderPickerAsync( new FolderPickerOpenOptions { Title = "Choose your chaos_spells_sbox folder" } );
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

	void Browse_DoubleTapped( object sender, TappedEventArgs e ) => Vm.OpenSelectedCommand.Execute( null );
}
