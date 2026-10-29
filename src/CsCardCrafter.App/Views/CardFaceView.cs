using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ChaosSpells.Cards;
using CsCardCrafter.App.Services;

namespace CsCardCrafter.App.Views;

/// <summary>
/// The live card face. It draws the game's own layout (Code/UI/CardFace.razor in chaos_spells_sbox): every box is the
/// same percentage of the 254 x 354 card, the name is Sherlock Vintage squeezed sideways to fill the banner (never
/// shrunk), costs and the copy limit are white numbers with a black outline (0 still shows), and the effect text is
/// the game's <see cref="CardText"/> runs in bold Liberation Sans, sized down only until it fits its box. Lines of
/// one effect sit close together; a wider gap shows where the next effect starts.
/// </summary>
public sealed class CardFaceView : Control
{
	public const double BaseWidth = 254;
	public const double BaseHeight = 354;

	public static readonly StyledProperty<CardDefinition> CardProperty = AvaloniaProperty.Register<CardFaceView, CardDefinition>( nameof( Card ) );

	/// <summary>Bumped by the editor on every change, as the card itself is a plain object that can't announce edits.</summary>
	public static readonly StyledProperty<int> RevisionProperty = AvaloniaProperty.Register<CardFaceView, int>( nameof( Revision ) );

	public CardDefinition Card { get => GetValue( CardProperty ); set => SetValue( CardProperty, value ); }
	public int Revision { get => GetValue( RevisionProperty ); set => SetValue( RevisionProperty, value ); }

	static readonly FontFamily TitleFont = new( "avares://CSCardCrafter/Assets/Fonts#Sherlock Vintage" );
	static readonly FontFamily BodyFont = new( "avares://CSCardCrafter/Assets/Fonts#Liberation Sans" );

	static CardFaceView()
	{
		AffectsRender<CardFaceView>( CardProperty, RevisionProperty );
	}

	protected override Size MeasureOverride( Size available )
	{
		var w = double.IsInfinity( available.Width ) ? BaseWidth * 1.5 : available.Width;
		var h = double.IsInfinity( available.Height ) ? w * BaseHeight / BaseWidth : available.Height;
		var scale = Math.Min( w / BaseWidth, h / BaseHeight );
		return new Size( BaseWidth * scale, BaseHeight * scale );
	}

	/// <summary>A box given as the game's percentages of the card (left, top, width, height).</summary>
	static Rect Box( double left, double top, double width, double height ) =>
		new( left * BaseWidth / 100, top * BaseHeight / 100, width * BaseWidth / 100, height * BaseHeight / 100 );

	public override void Render( DrawingContext ctx )
	{
		var scale = Math.Min( Bounds.Width / BaseWidth, Bounds.Height / BaseHeight );
		if ( scale <= 0 )
			return;
		var offset = new Point( (Bounds.Width - BaseWidth * scale) / 2, (Bounds.Height - BaseHeight * scale) / 2 );
		using var _ = ctx.PushTransform( Matrix.CreateScale( scale, scale ) * Matrix.CreateTranslation( offset.X, offset.Y ) );
		using var clip = ctx.PushClip( new Rect( 0, 0, BaseWidth, BaseHeight ) );

		var card = Card;
		ctx.FillRectangle( Brushes.Black, new Rect( 0, 0, BaseWidth, BaseHeight ) );
		if ( card is null )
		{
			DrawImage( ctx, Images.Get( "ui/card/backgrounds/cardbacking.png" ), new Rect( 0, 0, BaseWidth, BaseHeight ) );
			return;
		}

		var background = Box( 2.27, 1.68, 95.45, 93.46 );
		ctx.FillRectangle( new SolidColorBrush( Color.Parse( "#c9a440" ) ), background );
		DrawImage( ctx, Images.Get( card.Background ), background );

		DrawBanner( ctx, card );

		ctx.FillRectangle( Brushes.Black, Box( 2.97, 10.96, 94.06, 47.46 ) );
		DrawImage( ctx, Images.Get( "ui/card/layout/frame_outline.png" ), Box( 3.43, 11.19, 93.15, 46.89 ) );
		var art = Box( 5.51, 12.57, 88.98, 44.07 );
		ctx.FillRectangle( Brushes.White, art );
		DrawImage( ctx, Images.Get( card.Artwork ), art );

		DrawContain( ctx, Images.Get( card.ClassIcon ), Box( 3.66, 11.30, 12.6, 9.04 ) );
		double[] costTops = { 19.32, 26.02, 32.60 };
		for ( var i = 0; i < card.Costs.Count && i < 3; i++ )
		{
			var rect = Box( 4.84, costTops[i], 10.24, 7.34 );
			DrawContain( ctx, Images.Get( card.Costs[i].Icon ), rect );
			DrawBadgeText( ctx, (card.Costs[i].Amount ?? "").ToUpperInvariant(), rect, 0.085 );
		}
		DrawContain( ctx, Images.Get( card.TypeIcon ), Box( 83.35, 11.30, 12.6, 9.04 ) );
		if ( !string.IsNullOrEmpty( card.FusionIcon ) )
			DrawContain( ctx, Images.Get( card.FusionIcon ), Box( 3.15, 43.50, 10.24, 7.34 ) );
		if ( !string.IsNullOrEmpty( card.ReshuffleIcon ) )
			DrawContain( ctx, Images.Get( card.ReshuffleIcon ), Box( 3.15, 50.85, 10.24, 7.34 ) );
		if ( !string.IsNullOrEmpty( card.DeckIcon ) )
		{
			var rect = Box( 86.61, 50.85, 10.24, 7.34 );
			DrawContain( ctx, Images.Get( card.DeckIcon ), rect );
			if ( card.MaxCopies > 0 && !card.IsToken )
				DrawBadgeText( ctx, card.MaxCopies.ToString( CultureInfo.InvariantCulture ), rect, 0.09 );
		}

		ctx.FillRectangle( Brushes.Black, Box( 2.27, 58.9, 95.83, 0.85 ) );
		if ( !string.IsNullOrEmpty( card.OnlyIcon ) )
			DrawContain( ctx, Images.Get( card.OnlyIcon ), Box( 44.88, 53.09, 10.24, 7.34 ) );
		ctx.FillRectangle( new SolidColorBrush( Color.FromArgb( 77, 255, 255, 255 ) ), Box( 2.27, 59.75, 95.45, 35.4 ) );
		DrawEffectText( ctx, card, Box( 2.97, 59.75, 94.06, 35.4 ) );

		DrawCredit( ctx, card.FooterLeft, Box( 2.27, 95.17, 39.05, 2.85 ), TextAlignment.Left );
		DrawCredit( ctx, card.FooterRight, Box( 58.68, 95.17, 39.05, 2.85 ), TextAlignment.Right );
		DrawContain( ctx, Images.Get( "ui/card/icons/spirit.png" ), Box( 46.85, 95.32, 6.3, 4.52 ) );
	}

	/// <summary>The parchment banner over its dark ragged shadow, and the name squeezed sideways to fit it.</summary>
	static void DrawBanner( DrawingContext ctx, CardDefinition card )
	{
		var plate = Images.Get( "ui/card/layout/card_nameplace.png" );
		if ( plate is not null )
		{
			// Two black copies of the parchment sit just behind it (Unity's CardNameIMG Fade).
			var shadow = Box( 2.27, 1.38, 95.45, 9.6 );
			using ( ctx.PushOpacity( 0.96 ) )
			using ( ctx.PushOpacityMask( new ImageBrush( plate ) { Stretch = Stretch.Fill }, shadow ) )
				ctx.FillRectangle( Brushes.Black, shadow );
			DrawImage( ctx, plate, Box( 2.27, 1.66, 95.45, 8.84 ) );
		}

		var box = Box( 2.97, 2.2, 94.06, 8.3 );
		var text = new FormattedText( card.Name ?? "", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
			new Typeface( TitleFont ), BaseWidth * CardFit.TitleSize, Brushes.Black );
		var room = box.Width * 0.97;
		var squeeze = text.Width > room ? room / text.Width : 1;
		var origin = new Point( box.Center.X - text.Width / 2, box.Center.Y - text.Height / 2 );
		using ( ctx.PushTransform( Matrix.CreateTranslation( -box.Center.X, 0 ) * Matrix.CreateScale( squeeze, 1 ) * Matrix.CreateTranslation( box.Center.X, 0 ) ) )
			ctx.DrawText( text, origin );
	}

	/// <summary>Cost and copy-limit numbers: white, thin black outline, shrunk only when long ("180", "6%") so they stay in the badge.</summary>
	static void DrawBadgeText( DrawingContext ctx, string text, Rect badge, double share )
	{
		if ( string.IsNullOrEmpty( text ) )
			return;
		var em = text.Sum( c => c switch { '%' => 0.9, '1' => 0.45, _ => char.IsDigit( c ) ? 0.56 : 0.62 } );
		var size = Math.Min( BaseWidth * share, BaseWidth * 0.115 / Math.Max( 0.56, em ) );
		var formatted = new FormattedText( text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface( BodyFont ), size, Brushes.White );
		var origin = new Point( badge.Center.X - formatted.Width / 2, badge.Center.Y - formatted.Height / 2 );
		DrawOutlined( ctx, formatted, origin, Brushes.White, size * 0.07 );
	}

	static void DrawCredit( DrawingContext ctx, string text, Rect box, TextAlignment align )
	{
		if ( string.IsNullOrEmpty( text ) )
			return;
		var formatted = new FormattedText( text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface( BodyFont ), BaseWidth * 0.019, Brushes.White )
		{
			TextAlignment = align,
			MaxTextWidth = box.Width,
			LineHeight = BaseWidth * 0.019 * 1.05,
		};
		ctx.DrawText( formatted, box.TopLeft );
	}

	// ---- Effect text ---------------------------------------------------------------------------------------------

	/// <summary>One laid-out word: its runs, measured at the current size.</summary>
	sealed record Word( List<(TextRun Run, FormattedText Text)> Pieces, double Width );

	/// <summary>
	/// The effect text: each {BEG}…{END} paragraph centred line by line, words wrapping one at a time, the whole block
	/// centred in the box. It starts at the game's size for this card (CardFit) and steps down until it fits.
	/// </summary>
	static void DrawEffectText( DrawingContext ctx, CardDefinition card, Rect box )
	{
		var paragraphs = CardText.Format( card.Description, card.Name );
		if ( paragraphs.Count == 0 )
			return;
		var size = BaseWidth * CardFit.MaxBodySize * Math.Clamp( CardFit.For( card ).BodyFit, CardFit.MinBodyFit, 1f );
		List<List<List<Word>>> lines = null;
		double height = 0;
		for ( var attempt = 0; attempt < 60; attempt++ )
		{
			lines = Layout( paragraphs, size, box.Width, out height, out var overflowsWidth );
			if ( height <= box.Height && !overflowsWidth )
				break;
			size *= 0.96;
		}

		var y = box.Y + Math.Max( 0, (box.Height - height) / 2 );
		var lineHeight = size * CardFit.LineHeight;
		var gap = size * CardFit.WordGap;
		using var _ = ctx.PushClip( box );
		for ( var p = 0; p < lines.Count; p++ )
		{
			foreach ( var line in lines[p] )
			{
				var width = line.Sum( w => w.Width ) + gap * Math.Max( 0, line.Count - 1 );
				var x = box.X + (box.Width - width) / 2;
				foreach ( var word in line )
				{
					foreach ( var (run, text) in word.Pieces )
					{
						// Lines sit at 84% of the font size, as the game draws them; centre the glyphs in that line.
						var top = y + (lineHeight - text.Height) / 2;
						var brush = new SolidColorBrush( ParseColor( run.Color ) );
						DrawOutlined( ctx, text, new Point( x, top ), brush, size * 0.015 );
						if ( run.Underline )
							ctx.DrawLine( new Pen( brush, size * 0.07 ), new Point( x, top + text.Baseline + size * 0.1 ), new Point( x + text.Width, top + text.Baseline + size * 0.1 ) );
						x += text.Width;
					}
					x += gap;
				}
				y += lineHeight;
			}
			y += size * CardFit.ParagraphGap;
		}
	}

	static List<List<List<Word>>> Layout( List<List<TextRun>> paragraphs, double size, double maxWidth, out double height, out bool overflowsWidth )
	{
		var gap = size * CardFit.WordGap;
		var result = new List<List<List<Word>>>();
		overflowsWidth = false;
		var lineCount = 0;
		foreach ( var paragraph in paragraphs )
		{
			var lines = new List<List<Word>>();
			var line = new List<Word>();
			double width = 0;
			foreach ( var runs in CardText.Words( paragraph ) )
			{
				var pieces = runs.Select( r => (r, Measure( r, size )) ).ToList();
				var word = new Word( pieces, pieces.Sum( p => p.Item2.Width ) );
				if ( word.Width > maxWidth )
					overflowsWidth = true;
				var needed = line.Count == 0 ? word.Width : width + gap + word.Width;
				if ( line.Count > 0 && needed > maxWidth )
				{
					lines.Add( line );
					line = new List<Word>();
					needed = word.Width;
				}
				line.Add( word );
				width = needed;
			}
			if ( line.Count > 0 )
				lines.Add( line );
			lineCount += lines.Count;
			result.Add( lines );
		}
		height = lineCount * size * CardFit.LineHeight + Math.Max( 0, result.Count - 1 ) * size * CardFit.ParagraphGap;
		return result;
	}

	static FormattedText Measure( TextRun run, double size ) =>
		new( run.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
			new Typeface( BodyFont, run.Italic ? FontStyle.Italic : FontStyle.Normal, FontWeight.Bold ), size, Brushes.Black );

	// ---- Drawing helpers -----------------------------------------------------------------------------------------

	/// <summary>Text with a black outline of <paramref name="outline"/> around each letter, as Unity's TMP outline draws it.</summary>
	static void DrawOutlined( DrawingContext ctx, FormattedText text, Point origin, IBrush fill, double outline )
	{
		var geometry = text.BuildGeometry( origin );
		if ( geometry is null )
			return;
		ctx.DrawGeometry( null, new Pen( Brushes.Black, outline * 2, lineJoin: PenLineJoin.Round ), geometry );
		ctx.DrawGeometry( fill, null, geometry );
	}

	static void DrawImage( DrawingContext ctx, Bitmap image, Rect rect )
	{
		if ( image is not null )
			ctx.DrawImage( image, new Rect( image.Size ), rect );
	}

	/// <summary>An icon fitted inside its box, keeping its shape (background-size: contain).</summary>
	static void DrawContain( DrawingContext ctx, Bitmap image, Rect rect )
	{
		if ( image is null )
			return;
		var s = Math.Min( rect.Width / image.Size.Width, rect.Height / image.Size.Height );
		var size = new Size( image.Size.Width * s, image.Size.Height * s );
		ctx.DrawImage( image, new Rect( image.Size ), new Rect( rect.Center.X - size.Width / 2, rect.Center.Y - size.Height / 2, size.Width, size.Height ) );
	}

	static Color ParseColor( string color ) =>
		!string.IsNullOrEmpty( color ) && Color.TryParse( color, out var c ) ? c : Colors.Black;
}
