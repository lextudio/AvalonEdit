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

using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Navigation;

namespace ICSharpCode.AvalonEdit.Rendering
{
	public partial class VisualLineLinkText
	{
		/// <summary>
		/// Gets whether the link is currently clickable.
		/// </summary>
		/// <remarks>Returns true when control is pressed; or when
		/// <see cref="RequireControlModifierForClick"/> is disabled.</remarks>
		protected virtual partial bool LinkIsClickable()
		{
			if (NavigateUri == null)
				return false;
			if (RequireControlModifierForClick)
				return (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
			else
				return true;
		}

		partial void ApplyLinkTextDecorations()
		{
			this.TextRunProperties.SetTextDecorations(TextDecorations.Underline);
		}

		/// <inheritdoc/>
		protected internal override void OnQueryCursor(QueryCursorEventArgs e)
		{
			if (LinkIsClickable()) {
				e.Handled = true;
				e.Cursor = Cursors.Hand;
			}
		}

		/// <inheritdoc/>
		[System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes",
														 Justification = "I've seen Process.Start throw undocumented exceptions when the mail client / web browser is installed incorrectly")]
		protected internal override void OnMouseDown(MouseButtonEventArgs e)
		{
			if (e.ChangedButton == MouseButton.Left && !e.Handled && LinkIsClickable()) {
				RequestNavigateEventArgs args = new RequestNavigateEventArgs(this.NavigateUri, this.TargetName);
				args.RoutedEvent = Hyperlink.RequestNavigateEvent;
				FrameworkElement element = e.Source as FrameworkElement;
				if (element != null) {
					// allow user code to handle the navigation request
					element.RaiseEvent(args);
				}
				if (!args.Handled) {
					try {
						Process.Start(new ProcessStartInfo { FileName = this.NavigateUri.ToString(), UseShellExecute = true });
					} catch {
						// ignore all kinds of errors during web browser start
					}
				}
				e.Handled = true;
			}
		}
	}
}
