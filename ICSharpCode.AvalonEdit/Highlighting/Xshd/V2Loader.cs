// Copyright (c) 2014 AlphaSierraPapa for the SharpDevelop Team
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Xml;
using System.Xml.Schema;

using ICSharpCode.AvalonEdit.Utils;

namespace ICSharpCode.AvalonEdit.Highlighting.Xshd
{
	/// <summary>
	/// Loads .xshd files, version 2.0.
	/// Version 2.0 files are recognized by the namespace.
	/// </summary>
	static class V2Loader
	{
		public const string Namespace = "http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008";

		static XmlSchemaSet schemaSet;

		static XmlSchemaSet SchemaSet {
			get {
				if (schemaSet == null) {
					schemaSet = HighlightingLoader.LoadSchemaSet(new XmlTextReader(
						Resources.OpenStream("ModeV2.xsd")));
				}
				return schemaSet;
			}
		}

		public static XshdSyntaxDefinition LoadDefinition(XmlReader reader, bool skipValidation)
		{
			reader = HighlightingLoader.GetValidatingReader(reader, true, skipValidation ? null : SchemaSet);
			reader.Read();
			return ParseDefinition(reader);
		}

		static XshdSyntaxDefinition ParseDefinition(XmlReader reader)
		{
			Debug.Assert(reader.LocalName == "SyntaxDefinition");
			XshdSyntaxDefinition def = new XshdSyntaxDefinition();
			def.Name = reader.GetAttribute("name");
			string extensions = reader.GetAttribute("extensions");
			if (extensions != null)
				def.Extensions.AddRange(extensions.Split(';'));
			ParseElements(def.Elements, reader);
			Debug.Assert(reader.NodeType == XmlNodeType.EndElement);
			Debug.Assert(reader.LocalName == "SyntaxDefinition");
			return def;
		}

		static void ParseElements(ICollection<XshdElement> c, XmlReader reader)
		{
			if (reader.IsEmptyElement)
				return;
			while (reader.Read() && reader.NodeType != XmlNodeType.EndElement) {
				Debug.Assert(reader.NodeType == XmlNodeType.Element);
				if (reader.NamespaceURI != Namespace) {
					if (!reader.IsEmptyElement)
						reader.Skip();
					continue;
				}
				switch (reader.Name) {
					case "RuleSet":
						c.Add(ParseRuleSet(reader));
						break;
					case "Property":
						c.Add(ParseProperty(reader));
						break;
					case "Color":
						c.Add(ParseNamedColor(reader));
						break;
					case "Keywords":
						c.Add(ParseKeywords(reader));
						break;
					case "Span":
						c.Add(ParseSpan(reader));
						break;
					case "Import":
						c.Add(ParseImport(reader));
						break;
					case "Rule":
						c.Add(ParseRule(reader));
						break;
					default:
						throw new NotSupportedException("Unknown element " + reader.Name);
				}
			}
		}

		static XshdElement ParseProperty(XmlReader reader)
		{
			XshdProperty property = new XshdProperty();
			SetPosition(property, reader);
			property.Name = reader.GetAttribute("name");
			property.Value = reader.GetAttribute("value");
			return property;
		}

		static XshdRuleSet ParseRuleSet(XmlReader reader)
		{
			XshdRuleSet ruleSet = new XshdRuleSet();
			SetPosition(ruleSet, reader);
			ruleSet.Name = reader.GetAttribute("name");
			ruleSet.IgnoreCase = reader.GetBoolAttribute("ignoreCase");

			CheckElementName(reader, ruleSet.Name);
			ParseElements(ruleSet.Elements, reader);
			return ruleSet;
		}

		static XshdRule ParseRule(XmlReader reader)
		{
			XshdRule rule = new XshdRule();
			SetPosition(rule, reader);
			rule.ColorReference = ParseColorReference(reader);
			if (!reader.IsEmptyElement) {
				reader.Read();
				if (reader.NodeType == XmlNodeType.Text) {
					rule.Regex = reader.ReadContentAsString();
					rule.RegexType = XshdRegexType.IgnorePatternWhitespace;
				}
			}
			return rule;
		}

		static XshdKeywords ParseKeywords(XmlReader reader)
		{
			XshdKeywords keywords = new XshdKeywords();
			SetPosition(keywords, reader);
			keywords.ColorReference = ParseColorReference(reader);
			reader.Read();
			while (reader.NodeType != XmlNodeType.EndElement) {
				Debug.Assert(reader.NodeType == XmlNodeType.Element);
				keywords.Words.Add(reader.ReadElementString());
			}
			return keywords;
		}

		static XshdImport ParseImport(XmlReader reader)
		{
			XshdImport import = new XshdImport();
			SetPosition(import, reader);
			import.RuleSetReference = ParseRuleSetReference(reader);
			if (!reader.IsEmptyElement)
				reader.Skip();
			return import;
		}

		static XshdSpan ParseSpan(XmlReader reader)
		{
			XshdSpan span = new XshdSpan();
			SetPosition(span, reader);
			span.BeginRegex = reader.GetAttribute("begin");
			span.EndRegex = reader.GetAttribute("end");
			span.Multiline = reader.GetBoolAttribute("multiline") ?? false;
			span.SpanColorReference = ParseColorReference(reader);
			span.RuleSetReference = ParseRuleSetReference(reader);
			if (!reader.IsEmptyElement) {
				reader.Read();
				while (reader.NodeType != XmlNodeType.EndElement) {
					Debug.Assert(reader.NodeType == XmlNodeType.Element);
					switch (reader.Name) {
						case "Begin":
							if (span.BeginRegex != null)
								throw Error(reader, "Duplicate Begin regex");
							span.BeginColorReference = ParseColorReference(reader);
							span.BeginRegex = reader.ReadElementString();
							span.BeginRegexType = XshdRegexType.IgnorePatternWhitespace;
							break;
						case "End":
							if (span.EndRegex != null)
								throw Error(reader, "Duplicate End regex");
							span.EndColorReference = ParseColorReference(reader);
							span.EndRegex = reader.ReadElementString();
							span.EndRegexType = XshdRegexType.IgnorePatternWhitespace;
							break;
						case "RuleSet":
							if (span.RuleSetReference.ReferencedElement != null)
								throw Error(reader, "Cannot specify both inline RuleSet and RuleSet reference");
							span.RuleSetReference = new XshdReference<XshdRuleSet>(ParseRuleSet(reader));
							reader.Read();
							break;
						default:
							throw new NotSupportedException("Unknown element " + reader.Name);
					}
				}
			}
			return span;
		}

		static Exception Error(XmlReader reader, string message)
		{
			return Error(reader as IXmlLineInfo, message);
		}

		static Exception Error(IXmlLineInfo lineInfo, string message)
		{
			if (lineInfo != null)
				return new HighlightingDefinitionInvalidException(HighlightingLoader.FormatExceptionMessage(message, lineInfo.LineNumber, lineInfo.LinePosition));
			else
				return new HighlightingDefinitionInvalidException(message);
		}

		/// <summary>
		/// Sets the element's position to the XmlReader's position.
		/// </summary>
		static void SetPosition(XshdElement element, XmlReader reader)
		{
			IXmlLineInfo lineInfo = reader as IXmlLineInfo;
			if (lineInfo != null) {
				element.LineNumber = lineInfo.LineNumber;
				element.ColumnNumber = lineInfo.LinePosition;
			}
		}

		static XshdReference<XshdRuleSet> ParseRuleSetReference(XmlReader reader)
		{
			string ruleSet = reader.GetAttribute("ruleSet");
			if (ruleSet != null) {
				// '/' is valid in highlighting definition names, so we need the last occurrence
				int pos = ruleSet.LastIndexOf('/');
				if (pos >= 0) {
					return new XshdReference<XshdRuleSet>(ruleSet.Substring(0, pos), ruleSet.Substring(pos + 1));
				} else {
					return new XshdReference<XshdRuleSet>(null, ruleSet);
				}
			} else {
				return new XshdReference<XshdRuleSet>();
			}
		}

		static void CheckElementName(XmlReader reader, string name)
		{
			if (name != null) {
				if (name.Length == 0)
					throw Error(reader, "The empty string is not a valid name.");
				if (name.IndexOf('/') >= 0)
					throw Error(reader, "Element names must not contain a slash.");
			}
		}

		#region ParseColor
		static XshdColor ParseNamedColor(XmlReader reader)
		{
			XshdColor color = ParseColorAttributes(reader);
			// check removed: invisible named colors may be useful now that apps can read highlighting data
			//if (color.Foreground == null && color.FontWeight == null && color.FontStyle == null)
			//	throw Error(reader, "A named color must have at least one element.");
			color.Name = reader.GetAttribute("name");
			CheckElementName(reader, color.Name);
			color.ExampleText = reader.GetAttribute("exampleText");
			return color;
		}

		static XshdReference<XshdColor> ParseColorReference(XmlReader reader)
		{
			string color = reader.GetAttribute("color");
			if (color != null) {
				int pos = color.LastIndexOf('/');
				if (pos >= 0) {
					return new XshdReference<XshdColor>(color.Substring(0, pos), color.Substring(pos + 1));
				} else {
					return new XshdReference<XshdColor>(null, color);
				}
			} else {
				return new XshdReference<XshdColor>(ParseColorAttributes(reader));
			}
		}

		static XshdColor ParseColorAttributes(XmlReader reader)
		{
			XshdColor color = new XshdColor();
			SetPosition(color, reader);
			IXmlLineInfo position = reader as IXmlLineInfo;
			color.Foreground = ParseColor(position, reader.GetAttribute("foreground"));
			color.Background = ParseColor(position, reader.GetAttribute("background"));
			color.FontWeight = ParseFontWeight(reader.GetAttribute("fontWeight"));
			color.FontStyle = ParseFontStyle(reader.GetAttribute("fontStyle"));
			color.Underline = reader.GetBoolAttribute("underline");
			color.Strikethrough = reader.GetBoolAttribute("strikethrough");
			color.FontFamily = ParseFontFamily(position, reader.GetAttribute("fontFamily"));
			color.FontSize = ParseFontSize(position, reader.GetAttribute("fontSize"));
			return color;
		}

	internal readonly static ColorConverter ColorConverter = new ColorConverter();
	internal readonly static FontWeightConverter FontWeightConverter = new FontWeightConverter();
	internal readonly static FontStyleConverter FontStyleConverter = new FontStyleConverter();
#if !WINDOWS_APP_SDK
	internal static readonly Dictionary<string, string> KnownNamedColors = new(StringComparer.OrdinalIgnoreCase)
	{
		{ "AliceBlue", "#FFF0F8FF" }, { "AntiqueWhite", "#FFFAEBD7" }, { "Aqua", "#FF00FFFF" },
		{ "Aquamarine", "#FF7FFFD4" }, { "Azure", "#FFF0FFFF" }, { "Beige", "#FFF5F5DC" },
		{ "Bisque", "#FFFFE4C4" }, { "Black", "#FF000000" }, { "BlanchedAlmond", "#FFFFEBCD" },
		{ "Blue", "#FF0000FF" }, { "BlueViolet", "#FF8A2BE2" }, { "Brown", "#FFA52A2A" },
		{ "BurlyWood", "#FFDEB887" }, { "CadetBlue", "#FF5F9EA0" }, { "Chartreuse", "#FF7FFF00" },
		{ "Chocolate", "#FFD2691E" }, { "Coral", "#FFFF7F50" }, { "CornflowerBlue", "#FF6495ED" },
		{ "Cornsilk", "#FFFFF8DC" }, { "Crimson", "#FFDC143C" }, { "Cyan", "#FF00FFFF" },
		{ "DarkBlue", "#FF00008B" }, { "DarkCyan", "#FF008B8B" }, { "DarkGoldenrod", "#FFB8860B" },
		{ "DarkGray", "#FFA9A9A9" }, { "DarkGreen", "#FF006400" }, { "DarkKhaki", "#FFBDB76B" },
		{ "DarkMagenta", "#FF8B008B" }, { "DarkOliveGreen", "#FF556B2F" }, { "DarkOrange", "#FFFF8C00" },
		{ "DarkOrchid", "#FF9932CC" }, { "DarkRed", "#FF8B0000" }, { "DarkSalmon", "#FFE9967A" },
		{ "DarkSeaGreen", "#FF8FBC8F" }, { "DarkSlateBlue", "#FF483D8B" }, { "DarkSlateGray", "#FF2F4F4F" },
		{ "DarkTurquoise", "#FF00CED1" }, { "DarkViolet", "#FF9400D3" }, { "DeepPink", "#FFFF1493" },
		{ "DeepSkyBlue", "#FF00BFFF" }, { "DimGray", "#FF696969" }, { "DodgerBlue", "#FF1E90FF" },
		{ "Firebrick", "#FFB22222" }, { "FloralWhite", "#FFFFFAF0" }, { "ForestGreen", "#FF228B22" },
		{ "Fuchsia", "#FFFF00FF" }, { "Gainsboro", "#FFDCDCDC" }, { "GhostWhite", "#FFF8F8FF" },
		{ "Gold", "#FFFFD700" }, { "Goldenrod", "#FFDAA520" }, { "Gray", "#FF808080" },
		{ "Green", "#FF008000" }, { "GreenYellow", "#FFADFF2F" }, { "Honeydew", "#FFF0FFF0" },
		{ "HotPink", "#FFFF69B4" }, { "IndianRed", "#FFCD5C5C" }, { "Indigo", "#FF4B0082" },
		{ "Ivory", "#FFFFFFF0" }, { "Khaki", "#FFF0E68C" }, { "Lavender", "#FFE6E6FA" },
		{ "LavenderBlush", "#FFFFF0F5" }, { "LawnGreen", "#FF7CFC00" }, { "LemonChiffon", "#FFFFFACD" },
		{ "LightBlue", "#FFADD8E6" }, { "LightCoral", "#FFF08080" }, { "LightCyan", "#FFE0FFFF" },
		{ "LightGoldenrodYellow", "#FFFAFAD2" }, { "LightGray", "#FFD3D3D3" }, { "LightGreen", "#FF90EE90" },
		{ "LightPink", "#FFFFB6C1" }, { "LightSalmon", "#FFFFA07A" }, { "LightSeaGreen", "#FF20B2AA" },
		{ "LightSkyBlue", "#FF87CEFA" }, { "LightSlateGray", "#FF778899" }, { "LightSteelBlue", "#FFB0C4DE" },
		{ "LightYellow", "#FFFFFEE0" }, { "Lime", "#FF00FF00" }, { "LimeGreen", "#FF32CD32" },
		{ "Linen", "#FFFAF0E6" }, { "Magenta", "#FFFF00FF" }, { "Maroon", "#FF800000" },
		{ "MediumAquamarine", "#FF66CDAA" }, { "MediumBlue", "#FF0000CD" }, { "MediumOrchid", "#FFBA55D3" },
		{ "MediumPurple", "#FF9370DB" }, { "MediumSeaGreen", "#FF3CB371" }, { "MediumSlateBlue", "#FF7B68EE" },
		{ "MediumSpringGreen", "#FF00FA9A" }, { "MediumTurquoise", "#FF48D1CC" }, { "MediumVioletRed", "#FFC71585" },
		{ "MidnightBlue", "#FF191970" }, { "MintCream", "#FFF5FFFA" }, { "MistyRose", "#FFFFE4E1" },
		{ "Moccasin", "#FFFFE4B5" }, { "NavajoWhite", "#FFFFDEAD" }, { "Navy", "#FF000080" },
		{ "OldLace", "#FFFDF5E6" }, { "Olive", "#FF808000" }, { "OliveDrab", "#FF6B8E23" },
		{ "Orange", "#FFFFA500" }, { "OrangeRed", "#FFFF4500" }, { "Orchid", "#FFDA70D6" },
		{ "PaleGoldenrod", "#FFEEE8AA" }, { "PaleGreen", "#FF98FB98" }, { "PaleTurquoise", "#FFAFEEEE" },
		{ "PaleVioletRed", "#FFDB7093" }, { "PapayaWhip", "#FFFFEFD5" }, { "PeachPuff", "#FFFFDAB9" },
		{ "Peru", "#FFCD853F" }, { "Pink", "#FFFFC0CB" }, { "Plum", "#FFDDA0DD" },
		{ "PowderBlue", "#FFB0E0E6" }, { "Purple", "#FF800080" }, { "Red", "#FFFF0000" },
		{ "RosyBrown", "#FFBC8F8F" }, { "RoyalBlue", "#FF4169E1" }, { "SaddleBrown", "#FF8B4513" },
		{ "Salmon", "#FFFA8072" }, { "SandyBrown", "#FFF4A460" }, { "SeaGreen", "#FF2E8B57" },
		{ "SeaShell", "#FFFFF5EE" }, { "Sienna", "#FFA0522D" }, { "Silver", "#FFC0C0C0" },
		{ "SkyBlue", "#FF87CEEB" }, { "SlateBlue", "#FF6A5ACD" }, { "SlateGray", "#FF708090" },
		{ "Snow", "#FFFFFAFA" }, { "SpringGreen", "#FF00FF7F" }, { "SteelBlue", "#FF4682B4" },
		{ "Tan", "#FFD2B48C" }, { "Teal", "#FF008080" }, { "Thistle", "#FFD8BFD8" },
		{ "Tomato", "#FFFF6347" }, { "Turquoise", "#FF40E0D0" }, { "Violet", "#FFEE82EE" },
		{ "Wheat", "#FFF5DEB3" }, { "White", "#FFFFFFFF" }, { "WhiteSmoke", "#FFF5F5F5" },
		{ "Yellow", "#FFFFFF00" }, { "YellowGreen", "#FF9ACD32" },
	};
#endif
	static HighlightingBrush ParseColor(IXmlLineInfo lineInfo, string color)
	{
		if (string.IsNullOrEmpty(color))
			return null;
		if (color.StartsWith("SystemColors.", StringComparison.Ordinal))
			return GetSystemColorBrush(lineInfo, color);

#if !WINDOWS_APP_SDK
		// Map WPF named colors to hex for Uno Platform (ColorConverter doesn't know named colors)
		if (KnownNamedColors.TryGetValue(color, out var hexColor))
			color = hexColor;
#endif

		return FixedColorHighlightingBrush((Color?)ColorConverter.ConvertFromInvariantString(color));
	}

		static int? ParseFontSize(IXmlLineInfo lineInfo, string size)
		{
			int value;
			return int.TryParse(size, out value)
				? value
				: (int?)null;
		}

		static FontFamily ParseFontFamily(IXmlLineInfo lineInfo, string family)
		{
			if (!string.IsNullOrEmpty(family)) {
				return new FontFamily(family);
			} else {
				return null;
			}
		}

		internal static SystemColorHighlightingBrush GetSystemColorBrush(IXmlLineInfo lineInfo, string name)
		{
			Debug.Assert(name.StartsWith("SystemColors.", StringComparison.Ordinal));
			string shortName = name.Substring(13);
			var property = typeof(SystemColors).GetProperty(shortName + "Brush");
			if (property == null)
				throw Error(lineInfo, "Cannot find '" + name + "'.");
			return new SystemColorHighlightingBrush(property);
		}

		static HighlightingBrush FixedColorHighlightingBrush(Color? color)
		{
			if (color == null)
				return null;
			return new SimpleHighlightingBrush(color.Value);
		}

		static FontWeight? ParseFontWeight(string fontWeight)
		{
			if (string.IsNullOrEmpty(fontWeight))
				return null;
			return (FontWeight?)FontWeightConverter.ConvertFromInvariantString(fontWeight);
		}

		static FontStyle? ParseFontStyle(string fontStyle)
		{
			if (string.IsNullOrEmpty(fontStyle))
				return null;
			return (FontStyle?)FontStyleConverter.ConvertFromInvariantString(fontStyle);
		}
		#endregion
	}
}
