// The s&box types the game's engine-free code mentions, so it compiles as plain .NET. None of them do anything here.
global using Sandbox;

namespace Sandbox;

public abstract class GameResource { }

[AttributeUsage( AttributeTargets.Class )]
public sealed class AssetTypeAttribute : Attribute
{
	public string Name { get; set; }
	public string Extension { get; set; }
	public string Category { get; set; }
}

public sealed class CategoryAttribute( string name ) : Attribute { public string Name { get; } = name; }
public sealed class ImageAssetPathAttribute : Attribute { }
public sealed class TextAreaAttribute : Attribute { }
public sealed class HideAttribute : Attribute { }
