﻿#region

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using Newtonsoft.Json;
using Serilog;
using static Glimmr.Models.GlimmrConstants;

#endregion

namespace Glimmr.Models;

public class FrameBuilder {
	private readonly int _bottomCount;

	// This will store the coords of input values
	private readonly VectorOfVectorOfPoint _inputCoords;
	private readonly int _ledCount;
	private readonly int _leftCount;
	private readonly int _rightCount;
	private readonly int _topCount;

	public FrameBuilder(IReadOnlyList<int> inputDimensions, bool sectors = false) {
		_leftCount = inputDimensions[0];
		_rightCount = inputDimensions[1];
		_topCount = inputDimensions[2];
		_bottomCount = inputDimensions[3];
		_ledCount = _leftCount + _rightCount + _topCount + _bottomCount;
		if (sectors) {
			_ledCount -= 4;
			_inputCoords = DrawSectors();
		} else {
			_inputCoords = DrawLeds();
		}
		Log.Debug("FrameBuilder created, we have " + _inputCoords.Size + " coordinates.");
	}


	public Mat? Build(IEnumerable<Color> colors) {
		var enumerable = colors as Color[] ?? colors.ToArray();
		if (enumerable.Length != _ledCount) {
			throw new ArgumentOutOfRangeException(
				$"Color length should be {_ledCount} versus {enumerable.Length}.");
		}
		var idx = 0;

		try {
			var gMat = new Mat(new Size(ScaleWidth, ScaleHeight), DepthType.Cv8U, 3);
			for (var i = 0; i < enumerable.Length; i++) {
				idx = i;
				var color = enumerable[i];
				var col = new MCvScalar(color.B, color.G, color.R);
				CvInvoke.FillPoly(gMat,_inputCoords[i],col,LineType.AntiAlias);
			}
			//CvInvoke.GaussianBlur(gMat, gMat, new Size(29,29), 0);
			return gMat;
		} catch (Exception e) {
			Log.Debug($"Exception, input coords are {_inputCoords.Size} but requested {idx}: " + e.Message + " at " + e.StackTrace + " " + JsonConvert.SerializeObject(e));
		}

		return null;

	}


	private VectorOfVectorOfPoint DrawLeds() {
		// This is where we're saving our output
		var polly = new VectorOfVectorOfPoint();
		var center = new Point(ScaleWidth / 2, ScaleHeight / 2);
		// Individual segment sizes
		var tWidth = (int)Math.Round((float)ScaleWidth / _topCount, MidpointRounding.AwayFromZero);
		var lHeight = (int)Math.Round((float)ScaleHeight / _leftCount, MidpointRounding.AwayFromZero);
		var bWidth = (int)Math.Round((float)ScaleWidth / _bottomCount, MidpointRounding.AwayFromZero);
		var rHeight = (int)Math.Round((float)ScaleHeight / _rightCount, MidpointRounding.AwayFromZero);
		
		// These are based on the border/strip values
		// Minimum limits for top, bottom, left, right            
		const int minTop = 0;
		const int minLeft = 0;
		// Calc right regions, bottom to top
		var step = _rightCount - 1;
		var c2 = 0;
		while (step >= 0) {
			var ord = step * rHeight;
			if (step == _rightCount - 1) {
				ord = ScaleHeight - rHeight;
				c2 = ScaleHeight;
			}

			if (step == 0) {
				ord = minTop;
				c2 = minTop + rHeight;
			}
			
			var pts = new Point[3];
			pts[0] = new Point(ScaleWidth, ord);
			pts[1] = new Point(ScaleWidth, c2);
			pts[2] = center;
			polly.Push(new VectorOfPoint(pts));
			Log.Debug("Pcount (R): " + polly.Size);
			step--;
			c2 = ord;
		}

		// Calc top regions, from right to left, skipping top-right corner (total horizontal sectors minus one)
		step = _topCount - 1;
		while (step >= 0) {
			var ord = step * tWidth;
			if (step == _topCount - 1) {
				ord = ScaleWidth - tWidth;
				c2 = ScaleWidth;
			}

			if (step == 0) {
				c2 = tWidth;
				ord = minLeft;
			}
			var pts = new Point[3];
			pts[0] = new Point(ord, minTop);
			pts[1] = new Point(c2, minTop);
			pts[2] = center;
			polly.Push(new VectorOfPoint(pts));
			Log.Debug("Pcount (T): " + polly.Size);
			step--;
			c2 = ord;
		}

		step = 0;
		// Calc left regions (top to bottom), skipping top-left
		while (step < _leftCount) {
			var ord = step * lHeight;
			c2 = ord + lHeight;
			if (step == 0) {
				ord = minTop;
				c2 = minTop + lHeight;
			}

			if (step == _leftCount - 1) {
				ord = ScaleHeight - lHeight;
				c2 = ScaleHeight;
			}
			
			var pts = new Point[3];
			pts[0] = new Point(minLeft, ord);
			pts[1] = new Point(minLeft, c2);
			pts[2] = center;
			polly.Push(new VectorOfPoint(pts));
			Log.Debug("Pcount (L): " + polly.Size);
			step++;
		}

		step = 0;
		// Calc bottom center regions (L-R)
		while (step < _bottomCount) {
			var ord = step * bWidth;
			c2 = ord + bWidth;
			if (step == 0) {
				ord = minLeft;
				c2 = minLeft + bWidth;
			}

			if (step == _bottomCount) {
				ord = ScaleWidth - bWidth;
				c2 = ScaleWidth;
			}
			var pts = new Point[3];
			pts[0] = new Point(ord, ScaleHeight);
			pts[1] = new Point(c2, ScaleHeight);
			pts[2] = center;
			polly.Push(new VectorOfPoint(pts));
			Log.Debug("Pcount (B): " + polly.Size);
			step += 1;
		}
		
		return polly;
	}

	private VectorOfVectorOfPoint DrawSectors() {
		// This is where we're saving our output
		var polly = new VectorOfVectorOfPoint();
		var center = new Point(ScaleWidth / 2, ScaleHeight / 2);
		// Individual segment sizes
		var vWidth = ScaleWidth / _topCount;
		var hHeight = ScaleHeight / _leftCount;
		// These are based on the border/strip values
		// Minimum limits for top, bottom, left, right            
		const int minTop = 0;
		const int minLeft = 0;
		// Calc right regions, bottom to top
		var step = _rightCount - 1;
		while (step >= 0) {
			var ord = step * hHeight;
			if (step == _rightCount - 1) ord = ScaleHeight - hHeight;
			if (step == 0) ord = minLeft;
			var pts = new Point[3];
			pts[0] = new Point(ScaleWidth, ord);
			pts[1] = new Point(ScaleWidth, ord + hHeight > ScaleHeight ? ScaleHeight : ord + hHeight);
			pts[2] = center;
			polly.Push(new VectorOfPoint(pts));
			step--;
		}

		// Calc top regions, from right to left, skipping top-right corner (total horizontal sectors minus one)
		step = _topCount - 2;
		while (step >= 1) {
			var ord = step * vWidth;
			if (step == 1) ord = 0;
			if (step == _topCount - 2) ord = ScaleWidth - vWidth;
			var pts = new Point[3];
			pts[0] = new Point(ord, minTop);
			pts[1] = new Point(ord + vWidth > ScaleWidth ? ScaleWidth : ord + vWidth, minTop);
			pts[2] = center;
			polly.Push(new VectorOfPoint(pts));
			step--;
		}

		step = 0;
		// Calc left regions (top to bottom), skipping top-left
		while (step <= _leftCount - 1) {
			var ord = step * hHeight;
			if (step == 0) ord = minTop;
			if (step == _leftCount - 1) ord = ScaleHeight - hHeight;
			var pts = new Point[3];
			pts[0] = new Point(minLeft, ord);
			pts[1] = new Point(minLeft, ord + hHeight > ScaleHeight - 3 ? ScaleHeight : ord + hHeight);
			pts[2] = center;
			polly.Push(new VectorOfPoint(pts));
			step++;
		}

		step = 1;
		// Calc bottom center regions (L-R)
		while (step <= _bottomCount - 2) {
			var ord = step * vWidth;
			if (step == 1) ord = minLeft;
			if (step == _bottomCount - 2) ord = ScaleWidth - vWidth;
			var pts = new Point[3];
			pts[0] = new Point(ord, ScaleHeight);
			pts[1] = new Point(ord + vWidth > ScaleWidth ? ScaleWidth : ord + vWidth, ScaleHeight);
			pts[2] = center;
			polly.Push(new VectorOfPoint(pts));
			step += 1;
		}

		return polly;
	}
}