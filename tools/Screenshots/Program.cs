using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CsCardCrafter.App;
using CsCardCrafter.App.ViewModels;
using CsCardCrafter.App.Views;
using CsCardCrafter.Core.Cards;

var output = args.Length > 0 ? args[0] : "screenshots";
Directory.CreateDirectory( output );

AppBuilder.Configure<App>()
	.UseSkia()
	.UseHeadless( new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false } )
	.SetupWithoutStarting();

void Pump()
{
	for ( var i = 0; i < 5; i++ )
		Dispatcher.UIThread.RunJobs();
}

void Save( Window window, string name )
{
	Pump();
	window.CaptureRenderedFrame()?.Save( Path.Combine( output, name + ".png" ) );
	Console.WriteLine( "saved " + name );
}

var vm = new MainViewModel();
var main = new MainWindow { DataContext = vm, Width = 1500, Height = 920 };
main.Show();
var tabs = main.GetVisualDescendantsOfType<TabControl>().First();
string[] names = { "card", "effects", "text", "data", "browse" };
for ( var i = 0; i < names.Length; i++ )
{
	tabs.SelectedIndex = i;
	if ( names[i] == "browse" && vm.Browse.Results.Count > 0 )
		vm.Browse.Selected = vm.Browse.Results.FirstOrDefault( r => r.Name == "Glacier Heart" ) ?? vm.Browse.Results[0];
	Save( main, "window-" + names[i] );
}

// The card picker: choose a game card from the toolbar's list of the whole cards folder.
var picker = main.GetVisualDescendantsOfType<ComboBox>().First( c => c.Name == "CardPicker" );
tabs.SelectedIndex = 0;
picker.SelectedItem = vm.Browse.All.FirstOrDefault( i => i.Name == "Kitharas Moon" ) ?? vm.Browse.All.FirstOrDefault();
Save( main, "window-picked" );
Console.WriteLine( $"picker: {vm.Browse.All.Count} cards, editing {vm.Name}, picked {vm.Picked?.Name}" );

// Opening a card must never change it: pick every game card in turn and compare it with its file.
var changed = new List<string>();
foreach ( var item in vm.Browse.All.ToList() )
{
	picker.SelectedItem = vm.Browse.All.First( i => i.Game.Path == item.Game.Path );
	Pump();
	if ( CardFile.Write( vm.Card ) != CardFile.Write( CardFile.Load( item.Game.Path ) ) || vm.Dirty )
		changed.Add( item.Name );
}
Console.WriteLine( changed.Count == 0 ? "every card opens unchanged" : $"{changed.Count} cards changed on opening: {string.Join( ", ", changed.Take( 20 ) )}" );
picker.SelectedItem = vm.Browse.All.FirstOrDefault( i => i.Name == "Kitharas Moon" );
Save( main, "window-picked" );

// Card faces of a few game cards, next to each other.
var wanted = args.Skip( 1 ).DefaultIfEmpty( "Glacier Heart,Kitharas Moon,Luminous the Curious,Borealis the Creation Dragon of Ice,Healing Blade" ).First().Split( ',' );
var game = vm.Game;
var cards = game.LoadCards().Where( c => wanted.Contains( c.Card.Name ) ).ToList();
var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12, Margin = new Thickness( 12 ), Background = Avalonia.Media.Brushes.DimGray };
foreach ( var c in cards )
	row.Children.Add( new CardFaceView { Card = c.Card, Width = 381, Height = 531 } );
var faces = new Window { Content = row, Width = 12 + cards.Count * 393, Height = 555 };
faces.Show();
Save( faces, "faces" );

// A new card built in the crafter, with its auto-written text.
var fresh = new Window { Content = new CardFaceView { Card = CardParts.NewCard(), Width = 381, Height = 531 }, Width = 381, Height = 531 };
fresh.Show();
Save( fresh, "face-new-card" );

static class Visuals
{
	public static IEnumerable<T> GetVisualDescendantsOfType<T>( this Avalonia.Visual root ) =>
		Avalonia.VisualTree.VisualExtensions.GetVisualDescendants( root ).OfType<T>();
}
