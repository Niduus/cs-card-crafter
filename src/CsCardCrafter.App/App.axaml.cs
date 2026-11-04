using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CsCardCrafter.App.ViewModels;
using CsCardCrafter.App.Views;

namespace CsCardCrafter.App;

public partial class App : Application
{
	public override void Initialize() => AvaloniaXamlLoader.Load( this );

	public override void OnFrameworkInitializationCompleted()
	{
		if ( ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop )
		{
			var vm = new MainViewModel();
			// A slip in one card or one click is written to the log and shown, instead of closing the crafter.
			Avalonia.Threading.Dispatcher.UIThread.UnhandledException += ( _, e ) =>
			{
				Services.Log.Write( "Error: " + e.Exception );
				vm.Status = $"Something went wrong: {e.Exception.Message} (details in {Services.Log.File})";
				e.Handled = true;
			};
			if ( desktop.Args is { Length: > 0 } args && File.Exists( args[0] ) )
				vm.OpenFile( args[0] );
			desktop.MainWindow = new MainWindow { DataContext = vm };
		}
		base.OnFrameworkInitializationCompleted();
	}
}
