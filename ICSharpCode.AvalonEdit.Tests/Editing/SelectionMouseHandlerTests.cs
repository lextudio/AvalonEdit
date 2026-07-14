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

using System.Linq;
using System.Reflection;
using System.Windows.Documents;
using System.Windows.Input;

using ICSharpCode.AvalonEdit.Document;
using NUnit.Framework;

namespace ICSharpCode.AvalonEdit.Editing
{
	[TestFixture]
	public class SelectionMouseHandlerTests
	{
		[Test]
		public void EnterParagraphBreakIsBoundToEnterAndReturn()
		{
			TextArea textArea = new TextArea();

			Assert.That(HasKeyBinding(textArea, EditingCommands.EnterParagraphBreak, ModifierKeys.None, Key.Enter), Is.True);
			Assert.That(HasKeyBinding(textArea, EditingCommands.EnterParagraphBreak, ModifierKeys.None, Key.Return), Is.True);
		}

		[Test]
		public void EnterLineBreakIsBoundToShiftEnterAndShiftReturn()
		{
			TextArea textArea = new TextArea();

			Assert.That(HasKeyBinding(textArea, EditingCommands.EnterLineBreak, ModifierKeys.Shift, Key.Enter), Is.True);
			Assert.That(HasKeyBinding(textArea, EditingCommands.EnterLineBreak, ModifierKeys.Shift, Key.Return), Is.True);
		}

		[Test]
		public void DocumentChangeCancelsProgrammaticSelectionMode()
		{
			TextArea textArea = new TextArea();
			textArea.Document = new TextDocument("first line\nsecond line");
			SelectionMouseHandler handler = (SelectionMouseHandler)textArea.DefaultInputHandler.MouseSelection;

			SetMouseSelectionMode(handler, MouseSelectionMode.Normal);
			Assert.That(handler.MouseSelectionMode, Is.EqualTo(MouseSelectionMode.Normal));

			textArea.Document = new TextDocument("replacement");

			Assert.That(handler.MouseSelectionMode, Is.EqualTo(MouseSelectionMode.None));
		}

		[TestCase(MouseSelectionMode.Normal)]
		[TestCase(MouseSelectionMode.WholeWord)]
		[TestCase(MouseSelectionMode.WholeLine)]
		[TestCase(MouseSelectionMode.Rectangular)]
		[TestCase(MouseSelectionMode.PossibleDragStart)]
		public void MouseMoveCancelsSelectionModesWhenLeftButtonIsReleased(MouseSelectionMode mode)
		{
			Assert.That(SelectionMouseHandler.ShouldCancelSelectionOnMouseMove(mode, MouseButtonState.Released), Is.True);
		}

		[TestCase(MouseSelectionMode.None)]
		[TestCase(MouseSelectionMode.Drag)]
		public void MouseMoveDoesNotCancelInactiveOrDragModeWhenLeftButtonIsReleased(MouseSelectionMode mode)
		{
			Assert.That(SelectionMouseHandler.ShouldCancelSelectionOnMouseMove(mode, MouseButtonState.Released), Is.False);
		}

		[TestCase(MouseSelectionMode.Normal)]
		[TestCase(MouseSelectionMode.WholeWord)]
		[TestCase(MouseSelectionMode.WholeLine)]
		[TestCase(MouseSelectionMode.Rectangular)]
		[TestCase(MouseSelectionMode.PossibleDragStart)]
		public void MouseMoveKeepsSelectionModesWhenLeftButtonIsPressed(MouseSelectionMode mode)
		{
			Assert.That(SelectionMouseHandler.ShouldCancelSelectionOnMouseMove(mode, MouseButtonState.Pressed), Is.False);
		}

		[Test]
		public void SettingMouseSelectionModeToNoneIsIdempotent()
		{
			TextArea textArea = new TextArea();
			SelectionMouseHandler handler = (SelectionMouseHandler)textArea.DefaultInputHandler.MouseSelection;

			handler.MouseSelectionMode = MouseSelectionMode.None;
			handler.MouseSelectionMode = MouseSelectionMode.None;

			Assert.That(handler.MouseSelectionMode, Is.EqualTo(MouseSelectionMode.None));
		}

		static bool HasKeyBinding(TextArea textArea, ICommand command, ModifierKeys modifiers, Key key)
		{
			return textArea.DefaultInputHandler.Editing.InputBindings
				.OfType<KeyBinding>()
				.Any(binding => binding.Command == command &&
					binding.Modifiers == modifiers &&
					binding.Key == key);
		}

		static void SetMouseSelectionMode(SelectionMouseHandler handler, MouseSelectionMode mode)
		{
			typeof(SelectionMouseHandler)
				.GetField("mode", BindingFlags.Instance | BindingFlags.NonPublic)
				.SetValue(handler, mode);
		}
	}
}
