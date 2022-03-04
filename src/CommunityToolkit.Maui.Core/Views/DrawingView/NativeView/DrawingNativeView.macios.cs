﻿using System.Collections.ObjectModel;
using CoreGraphics;
using Foundation;
using Microsoft.Maui.Platform;
using UIKit;

namespace CommunityToolkit.Maui.Core.Views;

/// <summary>
/// 
/// </summary>
public class DrawingNativeView : UIView
{
	bool disposed;
	UIBezierPath currentPath;
	UIColor? lineColor;
	CGPoint previousPoint;
	Line? currentLine;

	public IDrawingView VirtualView { get; }

	public DrawingNativeView(IDrawingView virtualView)
	{
		VirtualView = virtualView;
		currentPath = new UIBezierPath();
		BackgroundColor = GetBackgroundColor();
		currentPath.LineWidth = VirtualView.DefaultLineWidth;
		lineColor = VirtualView.DefaultLineColor.ToNative();
		VirtualView.Lines.CollectionChanged += OnLinesCollectionChanged;
	}

	void OnLinesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => LoadPoints();

	public override void TouchesBegan(NSSet touches, UIEvent? evt)
	{
		SetParentTouches(false);

		VirtualView.Lines.CollectionChanged -= OnLinesCollectionChanged;
		if (!VirtualView.MultiLineMode)
		{
			VirtualView.Lines.Clear();
			currentPath.RemoveAllPoints();
		}

		var touch = (UITouch)touches.AnyObject;
		previousPoint = touch.PreviousLocationInView(this);
		currentPath.MoveTo(previousPoint);
		currentLine = new Line()
		{
			Points = new ObservableCollection<Point>()
			{
				new Point(previousPoint.X, previousPoint.Y)
			}
		};

		SetNeedsDisplay();

		VirtualView.Lines.CollectionChanged += OnLinesCollectionChanged;
	}

	public override void TouchesMoved(NSSet touches, UIEvent? evt)
	{
		var touch = (UITouch)touches.AnyObject;
		var currentPoint = touch.LocationInView(this);
		AddPointToPath(currentPoint);
		currentLine?.Points.Add(currentPoint.ToPoint());
	}

	public override void TouchesEnded(NSSet touches, UIEvent? evt)
	{
		if (currentLine != null)
		{
			UpdatePath(currentLine);
			VirtualView.Lines.Add(currentLine);
			OnDrawingLineCompleted(currentLine);
		}

		if (VirtualView.ClearOnFinish)
		{
			VirtualView.Lines.Clear();
		}

		SetParentTouches(true);
	}

	public override void TouchesCancelled(NSSet touches, UIEvent? evt)
	{
		InvokeOnMainThread(SetNeedsDisplay);
		SetParentTouches(true);
	}

	public override void Draw(CGRect rect)
	{
		lineColor?.SetStroke();
		currentPath.Stroke();
	}

	void AddPointToPath(CGPoint currentPoint)
	{
		currentPath.AddLineTo(currentPoint);
		SetNeedsDisplay();
	}

	void LoadPoints()
	{
		currentPath.RemoveAllPoints();
		foreach (var line in VirtualView.Lines)
		{
			UpdatePath(line);
			var stylusPoints = line.Points.Select(point => new CGPoint(point.X, point.Y)).ToList();
			if (stylusPoints.Count > 0)
			{
				previousPoint = stylusPoints[0];
				currentPath.MoveTo(previousPoint);
				foreach (var point in stylusPoints)
				{
					AddPointToPath(point);
				}
			}
		}

		SetNeedsDisplay();
	}

	void UpdatePath(ILine line)
	{
		VirtualView.Lines.CollectionChanged -= OnLinesCollectionChanged;
		var smoothedPoints = line.EnableSmoothedPath
			? SmoothedPathWithGranularity(line.Points, line.Granularity)
			: new ObservableCollection<Point>(line.Points);

		line.Points.Clear();

		foreach (var point in smoothedPoints)
		{
			line.Points.Add(point);
		}

		VirtualView.Lines.CollectionChanged += OnLinesCollectionChanged;
	}

	ObservableCollection<Point> SmoothedPathWithGranularity(ObservableCollection<Point> currentPoints,
		int granularity)
	{
		// not enough points to smooth effectively, so return the original path and points.
		if (currentPoints.Count < granularity + 2)
		{
			return new ObservableCollection<Point>(currentPoints);
		}

		var smoothedPoints = new ObservableCollection<Point>();

		// duplicate the first and last points as control points.
		currentPoints.Insert(0, currentPoints[0]);
		currentPoints.Add(currentPoints[^1]);

		// add the first point
		smoothedPoints.Add(currentPoints[0]);

		var currentPointsCount = currentPoints.Count;
		for (var index = 1; index < currentPointsCount - 2; index++)
		{
			var p0 = currentPoints[index - 1];
			var p1 = currentPoints[index];
			var p2 = currentPoints[index + 1];
			var p3 = currentPoints[index + 2];

			// add n points starting at p1 + dx/dy up until p2 using Catmull-Rom splines
			for (var i = 1; i < granularity; i++)
			{
				var t = i * (1f / granularity);
				var tt = t * t;
				var ttt = tt * t;

				// intermediate point
				var mid = GetIntermediatePoint(p0, p1, p2, p3, t, tt, ttt);
				smoothedPoints.Add(mid);
			}

			// add p2
			smoothedPoints.Add(p2);
		}

		// add the last point
		var last = currentPoints[^1];
		smoothedPoints.Add(last);
		return smoothedPoints;
	}

	Point GetIntermediatePoint(Point p0, Point p1, Point p2, Point p3, in float t, in float tt, in float ttt) =>
		new Point
		{
			X = 0.5f *
				((2f * p1.X) +
				 ((p2.X - p0.X) * t) +
				 (((2f * p0.X) - (5f * p1.X) + (4f * p2.X) - p3.X) * tt) +
				 (((3f * p1.X) - p0.X - (3f * p2.X) + p3.X) * ttt)),
			Y = 0.5f *
				((2 * p1.Y) +
				 ((p2.Y - p0.Y) * t) +
				 (((2 * p0.Y) - (5 * p1.Y) + (4 * p2.Y) - p3.Y) * tt) +
				 (((3 * p1.Y) - p0.Y - (3 * p2.Y) + p3.Y) * ttt))
		};

	protected override void Dispose(bool disposing)
	{
		if (disposed)
		{
			return;
		}

		if (disposing)
		{
			currentPath.Dispose();
			VirtualView.Lines.CollectionChanged -= OnLinesCollectionChanged;
		}

		disposed = true;

		base.Dispose(disposing);
	}

	void SetParentTouches(bool enabled)
	{
		var parent = Superview;

		while (parent != null)
		{
			//if (parent.GetType() == typeof(ScrollViewRenderer))
			//	((ScrollViewRenderer)parent).ScrollEnabled = enabled;

			parent = parent.Superview;
		}
	}
	
	UIColor GetBackgroundColor()
	{
		var background = VirtualView.Background?.BackgroundColor ?? Colors.White;
		return background.ToNative();
	}

	/// <summary>
	/// Executes DrawingLineCompleted event and DrawingLineCompletedCommand
	/// </summary>
	/// <param name="lastDrawingLine">Last drawing line</param>
	void OnDrawingLineCompleted(ILine? lastDrawingLine)
	{
		if (VirtualView.DrawingLineCompletedCommand?.CanExecute(lastDrawingLine) ?? false)
		{
			VirtualView.DrawingLineCompletedCommand.Execute(lastDrawingLine);
		}
	}
}