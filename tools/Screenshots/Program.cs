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
	AvaloniaHeadlessPlatform.ForceRenderTimerTick();
	Pump();
	window.CaptureRenderedFrame()?.Save( Path.Combine( output, name + ".png" ) );
	Console.WriteLine( "saved " + name );
}

var vm = new MainViewModel();
// The game is found on its own, off the window's thread.
for ( var wait = 0; vm.FindingGame && wait < 400; wait++ ) { Thread.Sleep( 50 ); Pump(); }
Console.WriteLine( $"found game: {vm.Game?.Root ?? "none"} ({vm.Status})" );
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

// The search bar: type part of a name (or the game's search words), pick a card, it opens.
var search = main.GetVisualDescendantsOfType<AutoCompleteBox>().First( c => c.Name == "CardSearchBox" );
tabs.SelectedIndex = 0;
search.MinimumPopulateDelay = TimeSpan.Zero;
search.GetVisualDescendantsOfType<TextBox>().First().Focus();
Pump();
main.KeyTextInput( "kitharas" );
for ( var wait = 0; !search.IsDropDownOpen && wait < 40; wait++ ) { Thread.Sleep( 50 ); Pump(); }
Console.WriteLine( $"search 'kitharas': open {search.IsDropDownOpen}, {vm.Browse.Search( "kitharas" ).Count} results, first {vm.Browse.Search( "kitharas" ).FirstOrDefault()?.Name}" );
Console.WriteLine( $"search 'cost:2 fire': {vm.Browse.Search( "cost:2 fire" ).Count} results; 'snare': {string.Join( ", ", vm.Browse.Search( "snare" ).Take( 4 ).Select( i => i.Name ) )}" );
Save( main, "window-search" );
// Arrow down to Kitharas Moon and press Enter, as a person would.
var moonAt = vm.Browse.Search( "kitharas" ).FindIndex( i => i.Name == "Kitharas Moon" );
for ( var k = 0; k <= moonAt; k++ ) { main.KeyPress( Avalonia.Input.Key.Down, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.ArrowDown, null ); Pump(); }
main.KeyPress( Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Enter, "\r" );
Pump();
Save( main, "window-picked" );
Console.WriteLine( $"search picked: editing {vm.Name}, bar text '{search.Text}'" );

// The dropdown next to it lists every card too.
var picker = main.GetVisualDescendantsOfType<ComboBox>().First( c => c.Name == "CardPicker" );
picker.SelectedItem = vm.Browse.All.FirstOrDefault( i => i.Name == "Glacier Heart" );
Pump();
Console.WriteLine( $"dropdown: {vm.Browse.All.Count} cards, picked Glacier Heart, editing {vm.Name}" );

// Opening a card must never change it: open every game card in turn and compare it with its file.
var changed = new List<string>();
foreach ( var item in vm.Browse.All.ToList() )
{
	vm.OpenFile( item.Game.Path );
	Pump();
	if ( CardFile.Write( vm.Card ) != CardFile.Write( CardFile.Load( item.Game.Path ) ) || vm.Dirty )
		changed.Add( item.Name );
}
Console.WriteLine( changed.Count == 0 ? "every card opens unchanged" : $"{changed.Count} cards changed on opening: {string.Join( ", ", changed.Take( 20 ) )}" );
vm.OpenFile( vm.Browse.All.First( i => i.Name == "Kitharas Moon" ).Game.Path );

// Enter with nothing highlighted opens the best match.
search.GetVisualDescendantsOfType<TextBox>().First().Focus();
main.KeyTextInput( "ash snare" );
Pump();
search.IsDropDownOpen = false;
main.KeyPress( Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Enter, "\r" );
Pump();
Console.WriteLine( $"typed 'ash snare' + Enter: editing {vm.Name}" );
vm.OpenFile( vm.Browse.All.First( i => i.Name == "Kitharas Moon" ).Game.Path );

// Keywords come from a dropdown of the game's keywords and show as chips.
var withKeywords = vm.Browse.All.First( i => i.Card.Keywords.Count >= 2 );
Pump();
Console.WriteLine( $"before keywords: editing {vm.Name}, bar '{search.Text}' sel {search.SelectedItem} open {search.IsDropDownOpen}" );
vm.OpenFile( withKeywords.Game.Path );
Pump();
Console.WriteLine( $"opened: editing {vm.Name}" );
var offered = vm.KeywordsToAdd.Count();
vm.KeywordToAdd = vm.KeywordsToAdd.First( k => k.Name == "Final Will" );
Pump();
Console.WriteLine( "keyword line now: " + vm.Description.Split( "{END}" )[0] );
Console.WriteLine( $"keywords: {MainViewModel.AllKeywords.Length} in the dropdown ({string.Join( ", ", MainViewModel.AllKeywords.Select( k => k.Name ) )}); {withKeywords.Name} had {offered} left to add, now [{string.Join( ", ", vm.Card.Keywords )}], dirty {vm.Dirty}" );
tabs.SelectedIndex = 0;
main.GetVisualDescendantsOfType<ScrollViewer>().First().Offset = new Avalonia.Vector( 0, 260 );
Save( main, "window-keywords" );
vm.RemoveKeywordCommand.Execute( vm.CardKeywordChips.First( k => k.Name == "Final Will" ) );
Console.WriteLine( $"removed Final Will: [{string.Join( ", ", vm.Card.Keywords )}], line: {vm.Description.Split( "{END}" )[0]}" );
vm.OpenFile( vm.Browse.All.First( i => i.Name == "Kitharas Moon" ).Game.Path );

// Edits redraw the face straight away.
var faceView = main.GetVisualDescendantsOfType<CardFaceView>().First();
var before = faceView.Revision;
vm.Name = "Kitharas Moon Edited";
vm.Combat = "999";
Pump();
Console.WriteLine( $"face revision {before} -> {faceView.Revision}, card name on face {faceView.Card?.Name}, combat in text: {vm.Description.Contains( "Combat: 999" )}, note: {vm.TextNote}" );
Save( main, "window-edited" );

// No game folder yet: the picker asks for it; opening a card from the game's folder finds the game.
var gameRoot = vm.Game.Root;
vm.SetGame( null );
Save( main, "window-no-game" );
vm.OpenFile( Path.Combine( gameRoot, "Assets", "cards", "series", "kitharas_moon.cscard" ) );
Console.WriteLine( $"after Open: game {(vm.Game is null ? "none" : "found")}, {vm.Browse.All.Count} cards, picked {vm.Picked?.Name}" );
vm.SetGame( null );
vm.TrySetGame( Path.Combine( gameRoot, "Assets", "cards" ) );
Console.WriteLine( $"cards folder as game folder: {(vm.Game is null ? "none" : "found")}, {vm.Browse.All.Count} cards" );

// "If this card is tributed from your hand or field and is in your Abyss, Evade Cast this card", built from blocks.
vm.TrySetGame( gameRoot );
vm.NewCommand.Execute( null );
vm.Name = "Ashen Phoenix";
vm.AddEffectCommand.Execute( "Persistent" );
var tribEffect = vm.Effects[^1];
tribEffect.Slots[0].Add( ChaosSpells.Rules.BlockRegistry.Get( "OP_WhenTributed" ) );
var tribWhen = tribEffect.Slots[0].Blocks[^1];
foreach ( var place in tribWhen.Params.First( p => p.IsPlaces ).PlaceOptions ) place.IsChecked = true;
tribEffect.Slots[1].Add( ChaosSpells.Rules.BlockRegistry.Get( "OP_IfCardIsIn" ) );
tribEffect.Slots[3].Add( ChaosSpells.Rules.BlockRegistry.Get( "OP_EvadeCastSelf" ) );
Pump();
Console.WriteLine( $"tributed effect suggests: {tribEffect.Suggested}" );
Console.WriteLine( $"tributed WHEN stores From = '{tribWhen.Op.Params["From"]}'" );
tabs.SelectedIndex = 1;
Pump();
foreach ( var sv in main.GetVisualDescendantsOfType<ScrollViewer>().Where( sv => sv.IsEffectivelyVisible && sv.Extent.Height > sv.Viewport.Height ) )
	sv.Offset = new Avalonia.Vector( 0, sv.Extent.Height - sv.Viewport.Height );
Save( main, "window-tributed" );

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

// On/off options are tick boxes and never print "true"/"false" (Combat per card, Non-Fusion only).
var perCard = vm.Browse.All.First( i => i.Card.Effects.Any( e => e.Always.Any( o => o.Op == "OP_CombatPerCount" ) ) && i.Name.Contains( "Kitharas" ) );
vm.OpenFile( perCard.Game.Path );
main.Width = 1500;
tabs.SelectedIndex = 1;
Save( main, "window-effects-flags" );
Console.WriteLine( $"flags: {perCard.Name}: {CsCardCrafter.Core.Text.CardWriter.Description( vm.Card, keepTyped: false ).Contains( "false" )}" );

// Every image the game's cards use loads.
var failed = new List<string>();
var checkedImages = 0;
foreach ( var g in vm.Game.LoadCards() )
	foreach ( var asset in new[] { g.Card.Artwork, g.Card.Background, g.Card.ClassIcon, g.Card.TypeIcon, g.Card.DeckIcon, g.Card.OnlyIcon, g.Card.ReshuffleIcon, g.Card.FusionIcon }.Concat( g.Card.Costs.Select( c => c.Icon ) ) )
	{
		if ( string.IsNullOrWhiteSpace( asset ) ) continue;
		checkedImages++;
		if ( CsCardCrafter.App.Services.Images.Get( asset ) is null ) failed.Add( asset );
	}
Console.WriteLine( $"images: {checkedImages} checked, {failed.Distinct().Count()} failed: {string.Join( ", ", failed.Distinct().Take( 15 ) )}" );

static class Visuals
{
	public static IEnumerable<T> GetVisualDescendantsOfType<T>( this Avalonia.Visual root ) =>
		Avalonia.VisualTree.VisualExtensions.GetVisualDescendants( root ).OfType<T>();
}
