﻿using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AdvanceMath;
using Multitouch.Framework.WPF.Input;
using Multitouch.Framework.WPF.Physics;
using Physics2DDotNet;
using Physics2DDotNet.Detectors;
using Physics2DDotNet.Joints;
using Physics2DDotNet.Shapes;
using Physics2DDotNet.Solvers;

namespace Multitouch.Framework.WPF.Controls
{
	/// <summary>
	/// Extends <see cref="System.Windows.Controls.ScrollViewer"/> to respond to Multitouch events. It also has similar scrolling behavior like iPhone.
	/// </summary>
	public class ScrollViewer : System.Windows.Controls.ScrollViewer
	{
		internal Body Body { get; private set; }
		Point startPoint;
		Point startOffset;
		readonly PhysicsEngine engine;
		readonly PhysicsTimer timer;
		FixedHingeJoint scrollJoint;
		int? firstContactId;

		FixedSlidingHingeJoint verticalJoint;
		FixedSlidingHingeJoint horizontalJoint;
		ContentDecorator decorator;
		double borderSoftness;

		/// <summary>
		/// Identifies <see cref="LinearDumpingProperty"/> dependency property.
		/// </summary>
		public static readonly DependencyProperty LinearDumpingProperty = DependencyProperty.Register("LinearDumping", typeof(double), typeof(ScrollViewer),
			new UIPropertyMetadata(0.98d, OnLinearDumpingChanged));

		/// <summary>
		/// Identifies <see cref="BorderSoftnessProperty"/> dependency property.
		/// </summary>
		public static readonly DependencyProperty BorderSoftnessProperty = DependencyProperty.Register("BorderSoftness", typeof(double), typeof(ScrollViewer),
			new UIPropertyMetadata(7d, OnBorderSoftnessChanged));

		static void OnBorderSoftnessChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			((ScrollViewer)d).OnBorderSoftnessChanged(e);
		}

		static void OnLinearDumpingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			((ScrollViewer)d).OnLinearDumpingChanged(e);
		}

		static ScrollViewer()
		{
			PropertyMetadata baseMetadata = ContentProperty.GetMetadata(typeof(System.Windows.Controls.ScrollViewer));
			ContentProperty.OverrideMetadata(typeof(ScrollViewer), new FrameworkPropertyMetadata(null, baseMetadata.PropertyChangedCallback, OnCoerceContentChanged));
		}

		static object OnCoerceContentChanged(DependencyObject d, object baseValue)
		{
			return ((ScrollViewer)d).OnCoerceContentChanged(baseValue);
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="ScrollViewer"/> class.
		/// </summary>
		public ScrollViewer()
		{
			borderSoftness = BorderSoftness;

			AddHandler(MultitouchScreen.NewContactEvent, (NewContactEventHandler)OnNewContact);
			AddHandler(MultitouchScreen.ContactMovedEvent, (ContactEventHandler)OnContactMoved);
			AddHandler(MultitouchScreen.ContactRemovedEvent, (ContactEventHandler)OnContactRemoved);
			AddHandler(MultitouchScreen.ContactLeaveEvent, (ContactEventHandler)OnContactLeave);	

			engine = new PhysicsEngine();
			engine.BroadPhase = new SweepAndPruneDetector();
			engine.Solver = new SequentialImpulsesSolver();
			engine.AddLogic(new BoundsConstrainLogic(this));
			timer = new PhysicsTimer(PhysicsTimerCallback, 0.01);

			Loaded += ScrollViewer_Loaded;
		}

		/// <summary>
		/// Gets or sets the linear dumping. Controls the friction of scrolling
		/// </summary>
		/// <value>The linear dumping.</value>
		public double LinearDumping
		{
			get { return (double)GetValue(LinearDumpingProperty); }
			set { SetValue(LinearDumpingProperty, value); }
		}

		/// <summary>
		/// Gets or sets the border softness. Controls the force that is used to bump the content of borders.
		/// </summary>
		/// <value>The border softness.</value>
		public double BorderSoftness
		{
			get { return (double)GetValue(BorderSoftnessProperty); }
			set { SetValue(BorderSoftnessProperty, value); }
		}
	
		object OnCoerceContentChanged(object baseValue)
		{
			decorator = new ContentDecorator(this);
			decorator.Child = (UIElement)baseValue;
			return decorator;
		}

		void OnNewContact(object sender, NewContactEventArgs e)
		{
			if (!firstContactId.HasValue)
			{
				firstContactId = e.Contact.Id;

				startPoint = e.GetPosition(this);
				startOffset.X = HorizontalOffset;
				startOffset.Y = VerticalOffset;

				Vector2D contactPoint = startPoint.ToVector2D();
				scrollJoint = new FixedHingeJoint(Body, contactPoint, new Lifespan());
				engine.AddJoint(scrollJoint);
			}
		}

		void OnContactMoved(object sender, ContactEventArgs e)
		{
			if (e.Contact.Id == firstContactId)
			{
				Point currentPoint = e.GetPosition(this);
                scrollJoint.Anchor = currentPoint.ToVector2D();
			}
		}

		void OnContactRemoved(object sender, ContactEventArgs e)
		{
			if (e.Contact.Id == firstContactId)
			{
				scrollJoint.Lifetime.IsExpired = true;
				scrollJoint = null;
				firstContactId = null;
			}
		}

        void OnContactLeave(object sender, ContactEventArgs e)
        {
            HitTestResult htr = VisualTreeHelper.HitTest(this, e.GetPosition(this));
            if (htr != null) return;

            OnContactRemoved(sender, e);
        }

		void PhysicsTimerCallback(double dt, double trueDt)
		{
			engine.Update(dt, trueDt);

			if (Body != null)
			{

				Vector2D position = -Body.State.Position.Linear;
				Dispatcher.Invoke(DispatcherPriority.Normal, (Action)(() =>
				{
					ScrollToHorizontalOffset(position.X);
					ScrollToVerticalOffset(position.Y);
				}));

				if (position.X < ContentDecorator.BORDER)
				{
					if (horizontalJoint == null)
					{
						Body.State.Position.Linear = new Vector2D(-ContentDecorator.BORDER, -position.Y);
						horizontalJoint = CreateBorderJoint(Orientation.Horizontal);
					}
				}
				else if (position.X > ScrollableWidth - ContentDecorator.BORDER)
				{
					if (horizontalJoint == null)
					{
						Body.State.Position.Linear = new Vector2D(-ScrollableWidth + ContentDecorator.BORDER, -position.Y);
						horizontalJoint = CreateBorderJoint(Orientation.Horizontal);
					}
				}
				else if (firstContactId.HasValue && horizontalJoint != null)
				{
					horizontalJoint.Lifetime.IsExpired = true;
					horizontalJoint = null;
				}

				if (position.Y < ContentDecorator.BORDER)
				{
					if (verticalJoint == null)
					{
						Body.State.Position.Linear = new Vector2D(-position.X, -ContentDecorator.BORDER);
						verticalJoint = CreateBorderJoint(Orientation.Vertical);
					}
				}
				else if (position.Y > ScrollableHeight - ContentDecorator.BORDER)
				{
					if (verticalJoint == null)
					{
						Body.State.Position.Linear = new Vector2D(-position.X, -ScrollableHeight + ContentDecorator.BORDER);
						verticalJoint = CreateBorderJoint(Orientation.Vertical);
					}
				}
				else if (firstContactId.HasValue && verticalJoint != null)
				{
					verticalJoint.Lifetime.IsExpired = true;
					verticalJoint = null;
				}
			}
		}

		FixedSlidingHingeJoint CreateBorderJoint(Orientation orientation)
		{
			FixedSlidingHingeJoint joint = new FixedSlidingHingeJoint(Body, new Vector2D(0, 0), new Lifespan(), orientation);
			joint.Softness = borderSoftness;
			engine.AddJoint(joint);
			return joint;
		}

		/// <summary>
		/// Called when the <see cref="P:System.Windows.Controls.ContentControl.Content"/> property changes.
		/// </summary>
		/// <param name="oldContent">The old value of the <see cref="P:System.Windows.Controls.ContentControl.Content"/> property.</param>
		/// <param name="newContent">The new value of the <see cref="P:System.Windows.Controls.ContentControl.Content"/> property.</param>
		protected override void OnContentChanged(object oldContent, object newContent)
		{
			timer.IsRunning = false;

			if (Body != null)
				Body.Lifetime.IsExpired = true;

			base.OnContentChanged(oldContent, newContent);

			PhysicsState state = new PhysicsState(new ALVector2D(0, 0, 0));
			IShape shape = new PolygonShape(VertexHelper.CreateRectangle(5, 5), 1);
			MassInfo mass = MassInfo.FromPolygon(shape.Vertexes, 1);
			Body = new Body(state, shape, mass, new Coefficients(0, 1), new Lifespan());
			Body.LinearDamping = LinearDumping;
			Body.Mass.MomentOfInertia = double.PositiveInfinity;
			Body.Tag = newContent;
			engine.AddBody(Body);

			if (!DesignerProperties.GetIsInDesignMode(this))
				timer.IsRunning = true;
		}

		void ScrollViewer_Loaded(object sender, RoutedEventArgs e)
		{
			if (decorator != null)
				decorator.InvalidateMeasure();

			ScrollToHorizontalOffset(ContentDecorator.BORDER);
			ScrollToVerticalOffset(ContentDecorator.BORDER);
		}

		void OnLinearDumpingChanged(DependencyPropertyChangedEventArgs e)
		{
			Body.LinearDamping = (double)e.NewValue;
		}

		void OnBorderSoftnessChanged(DependencyPropertyChangedEventArgs e)
		{
			borderSoftness = (double)e.NewValue;
		}
	}
}
