using Avalonia.Data.Converters;
using Avalonia.Media;
using CsCardCrafter.Core.Checks;

namespace CsCardCrafter.App.Views;

public static class Converters
{
	/// <summary>Problems in red, warnings in amber.</summary>
	public static readonly IValueConverter CheckBrush = new FuncValueConverter<CheckLevel, IBrush>( level =>
		level == CheckLevel.Problem ? new SolidColorBrush( Color.Parse( "#FF6B6B" ) ) : new SolidColorBrush( Color.Parse( "#F2C14E" ) ) );
}
