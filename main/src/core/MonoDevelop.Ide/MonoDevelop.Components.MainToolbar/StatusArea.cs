// 
// StatusArea.cs
//  
// Author:
//       Mike Krüger <mkrueger@xamarin.com>
// 
// Copyright (c) 2012 Xamarin Inc. (http://xamarin.com)
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
using System;
using Gtk;
using MonoDevelop.Components;
using Cairo;
using MonoDevelop.Ide;
using MonoDevelop.Ide.Gui;
using MonoDevelop.Ide.Tasks;
using System.Collections.Generic;
using MonoDevelop.Ide.CodeCompletion;
using MonoDevelop.Core;
using MonoDevelop.Ide.Gui.Components;

namespace MonoDevelop.Components.MainToolbar
{
	class StatusArea : EventBox, StatusBar
	{
		const int PaddingLeft = 10;
		int PaddingTop = 4;

		HBox contentBox = new HBox (false, 8);

		StatusAreaSeparator statusIconSeparator;
		Gtk.Widget buildResultWidget;

		readonly HBox messageBox = new HBox ();
		internal readonly HBox statusIconBox = new HBox ();

		string currentText;
		bool textIsMarkup;
		AnimatedIcon iconAnimation;
		IconId currentIcon;
		Gdk.Pixbuf currentPixbuf;
		static Pad sourcePad;
		IDisposable currentIconAnimation;
		double progressFraction;
		bool showingProgress;

		IDisposable progressFadeAnimation;
		double progressDisplayAlpha;

		MainStatusBarContextImpl mainContext;
		StatusBarContextImpl activeContext;
		
		public StatusBar MainContext {
			get { return mainContext; }
		}

		public StatusArea ()
		{
			mainContext = new MainStatusBarContextImpl (this);
			activeContext = mainContext;
			contexts.Add (mainContext);

			VisibleWindow = false;
			WidgetFlags |= Gtk.WidgetFlags.AppPaintable;

			statusIconBox.BorderWidth = 0;
			statusIconBox.Spacing = 3;
			
			ProgressBegin += delegate {
				showingProgress = true;
				progressFraction = 0;
				QueueDraw ();
			};
			
			ProgressEnd += delegate {
				showingProgress = false;
				if (progressFadeAnimation != null)
					progressFadeAnimation.Dispose ();
				progressFadeAnimation = DispatchService.RunAnimation (delegate {
					progressDisplayAlpha -= 0.2;
					QueueDraw ();
					if (progressDisplayAlpha <= 0) {
						progressDisplayAlpha = 0;
						return 0;
					}
					else
						return 100;
				});
				QueueDraw ();
			};
			
			ProgressFraction += delegate(object sender, FractionEventArgs e) {
				progressFraction = e.Work;
				if (progressFadeAnimation != null)
					progressFadeAnimation.Dispose ();
				progressFadeAnimation = DispatchService.RunAnimation (delegate {
					progressDisplayAlpha += 0.2;
					QueueDraw ();
					if (progressDisplayAlpha >= 1) {
						progressDisplayAlpha = 1;
						return 0;
					}
					else
						return 100;
				});
				QueueDraw ();
			};

			contentBox.PackStart (messageBox, true, true, 0);
			contentBox.PackEnd (statusIconBox, false, false, 0);
			contentBox.PackEnd (statusIconSeparator = new StatusAreaSeparator (), false, false, 0);
			contentBox.PackEnd (buildResultWidget = CreateBuildResultsWidget (Orientation.Horizontal), false, false, 0);

			var align = new Alignment (0, 0.5f, 1, 0);
			align.LeftPadding = 4;
			align.RightPadding = 8;
			align.Add (contentBox);
			Add (align);

			this.ButtonPressEvent += delegate {
				if (sourcePad != null)
					sourcePad.BringToFront (true);
			};

			statusIconBox.Shown += delegate {
				UpdateSeparators ();
			};

			statusIconBox.Hidden += delegate {
				UpdateSeparators ();
			};



			// todo: Move this to the CompletionWindowManager when it's possible.
			StatusBarContext completionStatus = null;
			CompletionWindowManager.WindowShown += delegate {
				CompletionListWindow wnd = CompletionWindowManager.Wnd;
				if (wnd != null && wnd.List != null && wnd.List.CategoryCount > 1) {
					if (completionStatus == null)
						completionStatus = CreateContext ();
					completionStatus.ShowMessage (string.Format (GettextCatalog.GetString ("To toggle categorized completion mode press {0}."), IdeApp.CommandService.GetCommandInfo (MonoDevelop.Ide.Commands.TextEditorCommands.ShowCompletionWindow).AccelKey));
				}
			};
			
			CompletionWindowManager.WindowClosed += delegate {
				if (completionStatus != null) {
					completionStatus.Dispose ();
					completionStatus = null;
				}
			};
		}

		void UpdateSeparators ()
		{
			statusIconSeparator.Visible = statusIconBox.Visible && buildResultWidget.Visible;
		}

		public Widget CreateBuildResultsWidget (Orientation orientation)
		{
			Gtk.Box box;
			if (orientation == Orientation.Horizontal)
				box = new HBox ();
			else
				box = new VBox ();
			box.Spacing = 3;
			
			Gdk.Pixbuf errorIcon = ImageService.GetPixbuf (MonoDevelop.Ide.Gui.Stock.Error, IconSize.Menu);
			Gdk.Pixbuf noErrorIcon = ImageService.MakeGrayscale (errorIcon); // creates a new pixbuf instance
			Gdk.Pixbuf warningIcon = ImageService.GetPixbuf (MonoDevelop.Ide.Gui.Stock.Warning, IconSize.Menu);
			Gdk.Pixbuf noWarningIcon = ImageService.MakeGrayscale (warningIcon); // creates a new pixbuf instance
			
			Gtk.Image errorImage = new Gtk.Image (errorIcon);
			Gtk.Image warningImage = new Gtk.Image (warningIcon);
			
			box.PackStart (errorImage, false, false, 0);
			Label errors = new Gtk.Label ();
			box.PackStart (errors, false, false, 0);
			
			box.PackStart (warningImage, false, false, 0);
			Label warnings = new Gtk.Label ();
			box.PackStart (warnings, false, false, 0);
			box.NoShowAll = true;
			box.Show ();
			
			TaskEventHandler updateHandler = delegate {
				int ec=0, wc=0;
				foreach (Task t in TaskService.Errors) {
					if (t.Severity == TaskSeverity.Error)
						ec++;
					else if (t.Severity == TaskSeverity.Warning)
						wc++;
				}
				errors.Visible = ec > 0;
				errors.Text = ec.ToString ();
				errorImage.Visible = ec > 0;

				warnings.Visible = wc > 0;
				warnings.Text = wc.ToString ();
				warningImage.Visible = wc > 0;
			};
			
			updateHandler (null, null);
			
			TaskService.Errors.TasksAdded += updateHandler;
			TaskService.Errors.TasksRemoved += updateHandler;
			
			box.Destroyed += delegate {
				noErrorIcon.Dispose ();
				noWarningIcon.Dispose ();
				TaskService.Errors.TasksAdded -= updateHandler;
				TaskService.Errors.TasksRemoved -= updateHandler;
			};

			EventBox ebox = new EventBox ();
			ebox.VisibleWindow = false;
			ebox.Add (box);
			ebox.ShowAll ();
			ebox.ButtonReleaseEvent += delegate {
				var pad = IdeApp.Workbench.GetPad<MonoDevelop.Ide.Gui.Pads.ErrorListPad> ();
				pad.BringToFront ();
			};

			errors.Visible = false;
			errorImage.Visible = false;
			warnings.Visible = false;
			warningImage.Visible = false;

			return ebox;
		}

		protected override void OnRealized ()
		{
			base.OnRealized ();
			ModifyText (StateType.Normal, Styles.StatusBarTextColor.ToGdkColor ());
			ModifyFg (StateType.Normal, Styles.StatusBarTextColor.ToGdkColor ());
		}

		protected override void OnSizeRequested (ref Requisition requisition)
		{
			requisition.Height = 22;
			base.OnSizeRequested (ref requisition);
		}

		protected override bool OnExposeEvent (Gdk.EventExpose evnt)
		{
			using (var context = Gdk.CairoHelper.Create (evnt.Window)) {
				CairoExtensions.RoundedRectangle (context, Allocation.X + 0.5, Allocation.Y + 0.5, Allocation.Width - 1, Allocation.Height - 1, 3);
				using (LinearGradient lg = new LinearGradient (Allocation.X, Allocation.Y, Allocation.X, Allocation.Height)) {
					lg.AddColorStop (0, Styles.StatusBarFill1Color);
					lg.AddColorStop (1, Styles.StatusBarFill2Color);
					context.Pattern = lg;
				}
				context.Fill ();

				CairoExtensions.RoundedRectangle (context, Allocation.X + 1.5, Allocation.Y + 1.5, Allocation.Width - 2.5, Allocation.Height - 2.5, 3);
				context.LineWidth = 1;
				context.Color = Styles.StatusBarInnerColor;
				context.Stroke ();

				CairoExtensions.RoundedRectangle (context, Allocation.X + 0.5, Allocation.Y + 0.5, Allocation.Width - 1, Allocation.Height - 1, 3);
				context.LineWidth = 1;
				context.Color = Styles.StatusBarBorderColor;
				context.Stroke ();

				int x = Allocation.X + PaddingLeft;

				if (currentPixbuf != null) {
					int y = Allocation.Y + (Allocation.Height - currentPixbuf.Height) / 2;
					Gdk.CairoHelper.SetSourcePixbuf (context, currentPixbuf, x, y);
					context.Paint ();
					x += currentPixbuf.Width + 4;
				}

				if (currentText != null) {
					Pango.Layout pl = new Pango.Layout (this.PangoContext);
					if (textIsMarkup)
						pl.SetMarkup (currentText);
					else
						pl.SetText (currentText);
					int w, h;
					pl.GetPixelSize (out w, out h);
					int y = Allocation.Y + (Allocation.Height - h) / 2;
					context.MoveTo (x, y);
					context.Color = Styles.StatusBarTextColor;
					Pango.CairoHelper.ShowLayout (context, pl);
					pl.Dispose ();

					if (showingProgress || progressDisplayAlpha > 0) {
						double py = y + h + 1.5;
						double pw = messageBox.Allocation.Width - (x - Allocation.X);
						context.LineWidth = 1;
						context.MoveTo (x + 0.5, py);
						context.RelLineTo (pw, 0);
						var c = Styles.StatusBarProgressBackgroundColor;
						c.A *= progressDisplayAlpha;
						context.Color = c;
						context.Stroke ();

						context.MoveTo (x + 0.5, py);
						context.RelLineTo ((double)pw * progressFraction, 0);
						c = Styles.StatusBarProgressColor;
						c.A *= progressDisplayAlpha;
						context.Color = c;
						context.Stroke ();
					}
				}
			}
			return true;
		}

		#region StatusBar implementation

		public void ShowCaretState (int line, int column, int selectedChars, bool isInInsertMode)
		{
			throw new NotImplementedException ();
		}

		public void ClearCaretState ()
		{
			throw new NotImplementedException ();
		}

		public StatusBarIcon ShowStatusIcon (Gdk.Pixbuf pixbuf)
		{
			DispatchService.AssertGuiThread ();
			StatusIcon icon = new StatusIcon (this, pixbuf);
			statusIconBox.PackEnd (icon.box);
			statusIconBox.ShowAll ();
			return icon;
		}
		
		void HideStatusIcon (StatusIcon icon)
		{
			statusIconBox.Remove (icon.EventBox);
			if (statusIconBox.Children.Length == 0)
				statusIconBox.Hide ();
			icon.EventBox.Destroy ();
		}

		List<StatusBarContextImpl> contexts = new List<StatusBarContextImpl> ();
		public StatusBarContext CreateContext ()
		{
			StatusBarContextImpl ctx = new StatusBarContextImpl (this);
			contexts.Add (ctx);
			return ctx;
		}

		public void ShowReady ()
		{
			ShowMessage ("");	
		}

		public void SetMessageSourcePad (Pad pad)
		{
			sourcePad = pad;
		}

		public bool HasResizeGrip {
			get;
			set;
		}

		public class StatusIcon : StatusBarIcon
		{
			StatusArea statusBar;
			internal EventBox box;
			string tip;
			DateTime alertEnd;
			Gdk.Pixbuf icon;
			uint animation;
			Gtk.Image image;
			
			int astep;
			Gdk.Pixbuf[] images;
			TooltipPopoverWindow tooltipWindow;
			bool mouseOver;
			
			public StatusIcon (StatusArea statusBar, Gdk.Pixbuf icon)
			{
				this.statusBar = statusBar;
				this.icon = icon;
				box = new EventBox ();
				box.VisibleWindow = false;
				image = new Image (icon);
				image.SetPadding (0, 0);
				box.Child = image;
				box.Events |= Gdk.EventMask.EnterNotifyMask | Gdk.EventMask.LeaveNotifyMask;
				box.EnterNotifyEvent += HandleEnterNotifyEvent;
				box.LeaveNotifyEvent += HandleLeaveNotifyEvent;
			}
			
			[GLib.ConnectBefore]
			void HandleLeaveNotifyEvent (object o, LeaveNotifyEventArgs args)
			{
				mouseOver = false;
				HideTooltip ();
			}
			
			[GLib.ConnectBefore]
			void HandleEnterNotifyEvent (object o, EnterNotifyEventArgs args)
			{
				mouseOver = true;
				ShowTooltip ();
			}
			
			void ShowTooltip ()
			{
				if (!string.IsNullOrEmpty (tip)) {
					HideTooltip ();
					tooltipWindow = new TooltipPopoverWindow ();
					tooltipWindow.ShowArrow = true;
					tooltipWindow.Text = tip;
					tooltipWindow.ShowPopup (box, PopupPosition.Top);
				}
			}
			
			void HideTooltip ()
			{
				if (tooltipWindow != null) {
					tooltipWindow.Destroy ();
					tooltipWindow = null;
				}
			}
			
			public void Dispose ()
			{
				HideTooltip ();
				statusBar.HideStatusIcon (this);
				if (images != null) {
					foreach (Gdk.Pixbuf img in images) {
						img.Dispose ();
					}
				}
				if (animation != 0) {
					GLib.Source.Remove (animation);
					animation = 0;
				}
			}
			
			public string ToolTip {
				get { return tip; }
				set {
					tip = value;
					if (tooltipWindow != null) {
						if (!string.IsNullOrEmpty (tip))
							tooltipWindow.Text = value;
						else
							HideTooltip ();
					} else if (!string.IsNullOrEmpty (tip) && mouseOver)
						ShowTooltip ();
				}
			}
			
			public EventBox EventBox {
				get { return box; }
			}
			
			public Gdk.Pixbuf Image {
				get { return icon; }
				set {
					icon = value;
					image.Pixbuf = icon;
				}
			}
			
			public void SetAlertMode (int seconds)
			{
				astep = 0;
				alertEnd = DateTime.Now.AddSeconds (seconds);
				
				if (animation != 0)
					GLib.Source.Remove (animation);
				
				animation = GLib.Timeout.Add (60, new GLib.TimeoutHandler (AnimateIcon));
				
				if (images == null) {
					images = new Gdk.Pixbuf [10];
					for (int n=0; n<10; n++)
						images [n] = ImageService.MakeTransparent (icon, ((double)(9-n))/10.0);
				}
			}
			
			bool AnimateIcon ()
			{
				if (DateTime.Now >= alertEnd && astep == 0) {
					image.Pixbuf = icon;
					animation = 0;
					return false;
				}
				if (astep < 10)
					image.Pixbuf = images [astep];
				else
					image.Pixbuf = images [20 - astep - 1];
				
				astep = (astep + 1) % 20;
				return true;
			}
		}
		
		#endregion

		#region StatusBarContextBase implementation

		public void ShowError (string error)
		{
			ShowMessage (MonoDevelop.Ide.Gui.Stock.Error, error);
		}

		public void ShowWarning (string warning)
		{
			DispatchService.AssertGuiThread ();
			ShowMessage (MonoDevelop.Ide.Gui.Stock.Warning, warning);
		}

		public void ShowMessage (string message)
		{
			ShowMessage (null, message, false);
		}

		public void ShowMessage (string message, bool isMarkup)
		{
			ShowMessage (null, message, isMarkup);
		}

		public void ShowMessage (IconId image, string message)
		{
			ShowMessage (image, message, false);
		}

		public void ShowMessage (IconId image, string message, bool isMarkup)
		{
			DispatchService.AssertGuiThread ();
			string txt = !String.IsNullOrEmpty (message) ? " " + message.Replace ("\n", " ") : "";
			currentText = txt != null ? txt.Trim () : null;
			textIsMarkup = isMarkup;
			if (currentIcon != image) {
				currentIcon = image;
				iconAnimation = null;
				if (currentIconAnimation != null) {
					currentIconAnimation.Dispose ();
					currentIconAnimation = null;
				}
				if (image != IconId.Null && ImageService.IsAnimation (image, Gtk.IconSize.Menu)) {
					iconAnimation = ImageService.GetAnimatedIcon (image, Gtk.IconSize.Menu);
					currentPixbuf = iconAnimation.FirstFrame;
					currentIconAnimation = iconAnimation.StartAnimation (delegate (Gdk.Pixbuf p) {
						currentPixbuf = p;
						QueueDraw ();
					});
				} else {
					currentPixbuf = currentIcon != IconId.Null ? ImageService.GetPixbuf (image) : null;
				}
			}
			QueueDraw ();
		}
		#endregion


		#region Progress Monitor implementation
		public static event EventHandler ProgressBegin, ProgressEnd, ProgressPulse;
		public static event EventHandler<FractionEventArgs> ProgressFraction;
		
		public sealed class FractionEventArgs : EventArgs
		{
			public double Work { get; private set; }
			
			public FractionEventArgs (double work)
			{
				this.Work = work;
			}
		}
		
		static void OnProgressBegin (EventArgs e)
		{
			var handler = ProgressBegin;
			if (handler != null)
				handler (null, e);
		}
		
		static void OnProgressEnd (EventArgs e)
		{
			var handler = ProgressEnd;
			if (handler != null)
				handler (null, e);
		}
		
		static void OnProgressPulse (EventArgs e)
		{
			var handler = ProgressPulse;
			if (handler != null)
				handler (null, e);
		}
		
		static void OnProgressFraction (FractionEventArgs e)
		{
			var handler = ProgressFraction;
			if (handler != null)
				handler (null, e);
		}
		
		public void BeginProgress (string name)
		{
			ShowMessage (name);
			OnProgressBegin (EventArgs.Empty);
		}
		
		public void BeginProgress (IconId image, string name)
		{
			ShowMessage (image, name);
			OnProgressBegin (EventArgs.Empty);
		}
		
		public void SetProgressFraction (double work)
		{
			DispatchService.AssertGuiThread ();
			OnProgressFraction (new FractionEventArgs (work));
		}
		
		public void EndProgress ()
		{
			ShowMessage ("");
			OnProgressEnd (EventArgs.Empty);
			AutoPulse = false;
		}
		
		public void Pulse ()
		{
			DispatchService.AssertGuiThread ();
			OnProgressPulse (EventArgs.Empty);
		}
		
		uint autoPulseTimeoutId;
		public bool AutoPulse {
			get { return autoPulseTimeoutId != 0; }
			set {
				DispatchService.AssertGuiThread ();
				if (value) {
					if (autoPulseTimeoutId == 0) {
						autoPulseTimeoutId = GLib.Timeout.Add (100, delegate {
							Pulse ();
							return true;
						});
					}
				} else {
					if (autoPulseTimeoutId != 0) {
						GLib.Source.Remove (autoPulseTimeoutId);
						autoPulseTimeoutId = 0;
					}
				}
			}
		}
		#endregion
	
		internal bool IsCurrentContext (StatusBarContextImpl ctx)
		{
			return ctx == activeContext;
		}
		
		internal void Remove (StatusBarContextImpl ctx)
		{
			if (ctx == mainContext)
				return;
			
			StatusBarContextImpl oldActive = activeContext;
			contexts.Remove (ctx);
			UpdateActiveContext ();
			if (oldActive != activeContext) {
				// Removed the active context. Update the status bar.
				activeContext.Update ();
			}
		}
		
		internal void UpdateActiveContext ()
		{
			for (int n = contexts.Count - 1; n >= 0; n--) {
				StatusBarContextImpl ctx = contexts [n];
				if (ctx.StatusChanged) {
					if (ctx != activeContext) {
						activeContext = ctx;
						activeContext.Update ();
					}
					return;
				}
			}
			throw new InvalidOperationException (); // There must be at least the main context
		}
	}

	class StatusAreaSeparator: HBox
	{
		protected override bool OnExposeEvent (Gdk.EventExpose evnt)
		{
			using (var ctx = Gdk.CairoHelper.Create (this.GdkWindow)) {
				var alloc = Allocation;
				//alloc.Inflate (0, -2);
				ctx.Rectangle (alloc.X, alloc.Y, 1, alloc.Height);
				Cairo.LinearGradient gr = new LinearGradient (alloc.X, alloc.Y, alloc.X, alloc.Y + alloc.Height);
				gr.AddColorStop (0, new Cairo.Color (0, 0, 0, 0));
				gr.AddColorStop (0.5, new Cairo.Color (0, 0, 0, 0.2));
				gr.AddColorStop (1, new Cairo.Color (0, 0, 0, 0));
				ctx.Pattern = gr;
				ctx.Fill ();
			}
			return true;
		}

		protected override void OnSizeRequested (ref Requisition requisition)
		{
			base.OnSizeRequested (ref requisition);
			requisition.Width = 1;
		}
	}
}

