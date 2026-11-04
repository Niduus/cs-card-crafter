using Avalonia;

namespace CsCardCrafter.App;

internal static class Program
{
	[STAThread]
	public static void Main( string[] args )
	{
		AppDomain.CurrentDomain.UnhandledException += ( _, e ) => Services.Log.Write( "Crash: " + e.ExceptionObject );
		Services.Log.Write( $"Started from {AppContext.BaseDirectory}" );
		BuildAvaloniaApp().StartWithClassicDesktopLifetime( args );
	}

	/// <summary>
	/// CSCRAFTER_SOFTWARE=1 draws without the graphics card (for PCs or emulators where the window stays black).
	/// </summary>
	public static AppBuilder BuildAvaloniaApp()
	{
		var builder = AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
		if ( Environment.GetEnvironmentVariable( "CSCRAFTER_SOFTWARE" ) == "1" )
			builder = builder.With( new Win32PlatformOptions { RenderingMode = new[] { Win32RenderingMode.Software } } );
		return builder;
	}
}
