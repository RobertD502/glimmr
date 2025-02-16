﻿#region

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using Glimmr.Enums;
using Glimmr.Models.Util;
using Glimmr.Services;
using Newtonsoft.Json;
using Serilog;
using static Glimmr.Models.GlimmrConstants;
using Color = System.Drawing.Color;

#endregion

namespace Glimmr.Models;

public class FrameSplitter {
	// Do we send our frame data to the UI?
	public bool DoSend { get; set; }
	public bool Updating { get; set; }

	public bool SourceActive { get; set; }
	private ColorService ColorService { get; }
	private readonly float _borderHeight;

	// The width of the border to crop from for LEDs
	private readonly float _borderWidth;
	private readonly List<VectorOfPoint> _targets;
	private readonly bool _useCrop;
	private bool _allBlack;
	private int _blackLevel;
	private int _bottomCount;

	// Loaded data
	private CaptureMode _captureMode;

	// The current crop mode?
	private Color[] _colorsLed;
	private Color[] _colorsLedIn;
	private Color[] _colorsSectors;
	private Color[] _colorsSectorsIn;
	private int _cropBlackLevel;
	private int _cropCount;
	private int _cropDelay;
	private int _frameCount;

	// Loaded settings
	private bool _cropLetter;
	private bool _cropPillar;

	private bool _doSave;

	private Rectangle[] _fullCoords;
	private Rectangle[] _fullSectors;
	private int _hCropCheck;
	private int _hSectors;

	// Are we cropping right now?
	private bool _lCrop;

	// Current crop settings
	private int _lCropPixels;
	private CropCounter _lCropCounter;

	private int _ledCount;
	private int _leftCount;
	private VectorOfPointF? _lockTarget;
	private bool _merge;
	private DeviceMode _mode;

	private bool _pCrop;
	private int _pCropPixels;
	private CropCounter _pillarCropCounter;

	private int _previewMode;
	private int _rightCount;
	private Size _scaleSize;

	// Set this when the sector changes
	private bool _sectorChanged;
	private int _sectorCount;
	private int _srcArea;
	private int _topCount;
	private bool _useCenter;
	private Color[] _empty;

	// Where we save the potential new value between checks
	private int _vCropCheck;

	// Source stuff
	private PointF[] _vectors;
	private int _vSectors;
	private bool _warned;
	private Color[] _emptySectors;

	public FrameSplitter(ColorService cs, bool crop = false) {
		Updating = false;
		_vectors = Array.Empty<PointF>();
		_targets = new List<VectorOfPoint>();
		_useCrop = crop;
		_colorsLed = Array.Empty<Color>();
		_colorsSectors = Array.Empty<Color>();
		_colorsLedIn = _colorsLed;
		_colorsSectorsIn = _colorsSectors;
		ColorService = cs;
		_empty = Array.Empty<Color>();
		_emptySectors = _empty;
		var sd = DataUtil.GetSystemData();
		_cropDelay = sd.CropDelay;
		cs.ControlService.RefreshSystemEvent += RefreshSystem;
		cs.FrameSaveEvent += TriggerSave;
		_pillarCropCounter = new CropCounter(_cropDelay);
		_lCropCounter = new CropCounter(_cropDelay);
		RefreshSystem();
		// Set desired width of capture region to 15% total image
		_borderWidth = 10;
		_borderHeight = 10;
		// Get sectors
		_fullCoords = DrawGrid();
		_fullSectors = DrawSectors();
		
	}


	private void RefreshSystem() {
		var sd = DataUtil.GetSystemData();
		_blackLevel = sd.BlackLevel;
		_cropBlackLevel = sd.CropBlackLevel;
		_leftCount = sd.LeftCount;
		_topCount = sd.TopCount;
		_rightCount = sd.RightCount;
		_bottomCount = sd.BottomCount;
		_hSectors = sd.HSectors;
		_vSectors = sd.VSectors;
		_cropDelay = sd.CropDelay;
		_pillarCropCounter = new CropCounter(_cropDelay);
		_lCropCounter = new CropCounter(_cropDelay);
		_cropLetter = sd.EnableLetterBox;
		_cropPillar = sd.EnablePillarBox;
		_mode = sd.DeviceMode;
		if (!_cropLetter || !_useCrop) {
			_lCrop = false;
			_vCropCheck = 0;
			_lCropPixels = 0;
			_cropLetter = false;
		}

		if (!_cropPillar || !_useCrop) {
			_pCrop = false;
			_pCropPixels = 0;
			_cropPillar = false;
		}

		_useCenter = sd.UseCenter;
		_ledCount = sd.LedCount;
		_empty = ColorUtil.EmptyColors(_ledCount);

		_sectorCount = sd.SectorCount;
		_emptySectors = ColorUtil.EmptyColors(_sectorCount);

		if (_ledCount == 0) {
			_ledCount = 200;
		}

		if (_sectorCount == 0) {
			_sectorCount = 12;
		}

		_colorsLed = ColorUtil.EmptyColors(_ledCount);
		_colorsSectors = ColorUtil.EmptyColors(_sectorCount);

		_previewMode = sd.PreviewMode;
		_captureMode = sd.CaptureMode;
		_srcArea = ScaleWidth * ScaleHeight;
		_scaleSize = new Size(ScaleWidth, ScaleHeight);

		if (_captureMode == CaptureMode.Camera) {
			try {
				var lt = DataUtil.GetItem<PointF[]>("LockTarget");
				if (lt != null) {
					_lockTarget = new VectorOfPointF(lt);
					var lC = 0;
					while (lC < 20) {
						_targets.Add(VPointFToVPoint(_lockTarget));
						lC++;
					}
				}
			} catch (Exception e) {
				Log.Warning("Video Capture Exception: " + e.Message + " at " + e.StackTrace);
			}
		}

		_vectors = new PointF[] {
			new Point(0, 0), new Point(ScaleWidth, 0), new Point(ScaleWidth, ScaleHeight),
			new Point(0, ScaleHeight)
		};

		_fullCoords = DrawGrid();
		_fullSectors = DrawSectors();
		_doSave = true;
	}

	private void TriggerSave() {
		_doSave = true;
	}

	private void SaveFrames(Mat inMat, Mat outMat) {
		_doSave = false;
		var cols = _colorsLed;
		var secs = _colorsSectors;
		if (_merge) {
			cols = _colorsLedIn;
			secs = _colorsSectorsIn;
		}

		if (inMat is { IsEmpty: false }) {
			ColorService.ControlService.SendImage("inputImage", inMat).ConfigureAwait(false);
		}

		if (outMat == null || outMat.IsEmpty) {
			return;
		}

		var colBlack = new Bgr(Color.FromArgb(0, 0, 0, 0)).MCvScalar;
		switch (_previewMode) {
			case 1: {
				for (var i = 0; i < _fullCoords.Length; i++) {
					var color = cols[i];
					if (color.R < _blackLevel && color.G < _blackLevel && color.B < _blackLevel) {
						continue;
					}

					var col = new Bgr(cols[i]).MCvScalar;
					CvInvoke.Rectangle(outMat, _fullCoords[i], col, -1, LineType.AntiAlias);
					CvInvoke.Rectangle(outMat, _fullCoords[i], colBlack, 1, LineType.AntiAlias);
				}

				break;
			}
			case 2: {
				for (var i = 0; i < _fullSectors.Length; i++) {
					var s = _fullSectors[i];
					var color = secs[i];
					if (color.R < _blackLevel && color.G < _blackLevel && color.B < _blackLevel) {
						continue;
					}

					var col = new Bgr(secs[i]).MCvScalar;
					CvInvoke.Rectangle(outMat, s, col, -1, LineType.AntiAlias);
					CvInvoke.Rectangle(outMat, s, colBlack, 1, LineType.AntiAlias);
					var cInt = i + 1;
					var tPoint = new Point(s.X, s.Y + 30);
					CvInvoke.PutText(outMat, cInt.ToString(), tPoint, FontFace.HersheySimplex, 0.75, colBlack);
				}

				break;
			}
		}

		if (DoSend) {
			ColorService.ControlService.SendImage("outputImage", outMat).ConfigureAwait(false);
		}

		inMat.Dispose();
		outMat.Dispose();
	}

	public void MergeFrame(Color[] leds, Color[] sectors) {
		_colorsLedIn = leds;
		_colorsSectorsIn = sectors;
		_merge = true;
	}

	public async Task Update(Mat? frame) {
		if (frame == null || frame.IsEmpty) {
			SourceActive = false;
			if (!_warned) {
				Log.Warning("Frame is null.");
			}

			_warned = true;
			return;
		}

		if (frame.Cols == 0) {
			SourceActive = false;
			if (!_warned) {
				Log.Warning("Frame has no columns.");
			}

			_warned = true;
			return;
		}

		if (frame.Rows == 0) {
			SourceActive = false;
			if (!_warned) {
				Log.Warning("Frame has no rows.");
			}

			_warned = true;
			return;
		}

		var clone = frame;

		if (frame.Width != ScaleWidth || frame.Height != ScaleWidth) {
			CvInvoke.Resize(frame, clone, new Size(ScaleWidth, ScaleHeight));
		}

		if (_captureMode == CaptureMode.Camera && _mode == DeviceMode.Video) {
			clone = CheckCamera(frame);
			if (clone == null || clone.IsEmpty || clone.Cols == 0) {
				Log.Warning("Invalid input frame.");
				SourceActive = false;
				return;
			}
		}

		// Don't do anything if there's no frame.
		if (clone == null || clone.IsEmpty) {
			Log.Warning("Null/Empty input!");
			// Dispose frame
			frame.Dispose();
			clone?.Dispose();
			return;
		}

		if (_useCrop) {
			_frameCount++;
			// Check sectors once per second
			if (_frameCount >= 30) {
				await CheckCrop(clone).ConfigureAwait(false);
				_frameCount = 0;
			}
		}

		SourceActive = !_allBlack;
		Updating = true;
		var ledColors = _empty;
		for (var i = 0; i < _fullCoords.Length; i++) {
			var sub = new Mat(clone, _fullCoords[i]);
			ledColors[i] = GetAverage(sub);
			sub.Dispose();
		}


		var sectorColors = _emptySectors;
		for (var i = 0; i < _fullSectors.Length; i++) {
			var sub = new Mat(clone, _fullSectors[i]);
			sectorColors[i] = GetAverage(sub);
		}
		
		_colorsLed = ledColors;
		_colorsSectors = sectorColors;
		if (DoSend) {
			await ColorService.SendColors(ledColors, sectorColors);
		}

		if (_doSave) {
			if (DoSend && ColorService.ControlService.SendPreview) {
				SaveFrames(frame, clone);
			}

			_doSave = false;
		}

		// Dispose
		frame.Dispose();
		clone.Dispose();
		Updating = false;
	}

	private Mat? CheckCamera(Mat input) {
		var scaled = input.Clone();

		Mat? output = null;

		// If we don't have a target, find one
		if (_lockTarget == null) {
			_lockTarget = FindTarget(scaled);
			if (_lockTarget != null) {
				Log.Debug("Target hit.");
				DataUtil.SetItem("LockTarget", _lockTarget.ToArray());
			} else {
				Log.Debug("No target.");
			}
		}

		// If we do or we found one...crop it out
		if (_lockTarget != null) {
			var dPoints = _lockTarget.ToArray();
			var warpMat = CvInvoke.GetPerspectiveTransform(dPoints, _vectors);
			output = new Mat();
			CvInvoke.WarpPerspective(scaled, output, warpMat, _scaleSize);
			warpMat.Dispose();
		}

		scaled.Dispose();
		// Once we have a warped frame, we need to do a check every N seconds for letterboxing...
		return output;
	}

	private VectorOfPointF? FindTarget(IInputArray input) {
		var cannyEdges = new Mat();
		var uImage = new Mat();
		var gray = new Mat();
		var blurred = new Mat();

		// Convert to greyscale
		CvInvoke.CvtColor(input, uImage, ColorConversion.Bgr2Gray);
		CvInvoke.BilateralFilter(uImage, gray, 11, 17, 17);
		uImage.Dispose();
		CvInvoke.MedianBlur(gray, blurred, 11);
		gray.Dispose();
		// Get edged version
		const double cannyThreshold = 0.0;
		const double cannyThresholdLinking = 200.0;
		CvInvoke.Canny(blurred, cannyEdges, cannyThreshold, cannyThresholdLinking);
		blurred.Dispose();

		// Get contours
		using (var contours = new VectorOfVectorOfPoint()) {
			CvInvoke.FindContours(cannyEdges, contours, null, RetrType.List,
				ChainApproxMethod.ChainApproxSimple);
			var count = contours.Size;
			// Looping contours
			for (var i = 0; i < count; i++) {
				var approxContour = new VectorOfPoint();
				using var contour = contours[i];
				CvInvoke.ApproxPolyDP(contour, approxContour, CvInvoke.ArcLength(contour, true) * 0.02,
					true);
				if (approxContour.Size != 4) {
					continue;
				}

				var cntArea = CvInvoke.ContourArea(approxContour);
				if (!(cntArea / _srcArea > .15)) {
					continue;
				}

				var pointOut = new VectorOfPointF(SortPoints(approxContour));
				_targets.Add(VPointFToVPoint(pointOut));
			}
		}

		var output = CountTargets(_targets);
		cannyEdges.Dispose();
		return output;
	}

	private VectorOfPointF? CountTargets(IReadOnlyCollection<VectorOfPoint> inputT) {
		VectorOfPointF? output = null;
		var x1 = 0;
		var x2 = 0;
		var x3 = 0;
		var x4 = 0;
		var y1 = 0;
		var y2 = 0;
		var y3 = 0;
		var y4 = 0;
		var iCount = inputT.Count;
		foreach (var point in inputT) {
			x1 += point[0].X;
			y1 += point[0].Y;
			x2 += point[1].X;
			y2 += point[1].Y;
			x3 += point[2].X;
			y3 += point[2].Y;
			x4 += point[3].X;
			y4 += point[3].Y;
		}

		if (iCount > 10) {
			x1 /= iCount;
			x2 /= iCount;
			x3 /= iCount;
			x4 /= iCount;
			y1 /= iCount;
			y2 /= iCount;
			y3 /= iCount;
			y4 /= iCount;

			PointF[] avgPoints = { new(x1, y1), new(x2, y2), new(x3, y3), new(x4, y4) };
			var avgVector = new VectorOfPointF(avgPoints);
			if (iCount > 20) {
				output = avgVector;
			}
		}

		if (iCount > 200) {
			_targets.RemoveRange(0, 150);
		}

		return output;
	}

	private static VectorOfPoint VPointFToVPoint(VectorOfPointF input) {
		var ta = input.ToArray();
		var pIn = new Point[input.Size];
		for (var i = 0; i < ta.Length; i++) {
			pIn[i] = new Point((int)ta[i].X, (int)ta[i].Y);
		}

		return new VectorOfPoint(pIn);
	}

	private static PointF[] SortPoints(VectorOfPoint wTarget) {
		var ta = wTarget.ToArray();
		var pIn = new PointF[wTarget.Size];
		for (var i = 0; i < ta.Length; i++) {
			pIn[i] = ta[i];
		}

		// Order points?
		var tPoints = pIn.OrderBy(p => p.Y);
		var vPoints = pIn.OrderByDescending(p => p.Y);
		var vtPoints = tPoints.Take(2);
		var vvPoints = vPoints.Take(2);
		vtPoints = vtPoints.OrderBy(p => p.X);
		vvPoints = vvPoints.OrderByDescending(p => p.X);
		var pointFs = vtPoints as PointF[] ?? vtPoints.ToArray();
		var tl = pointFs[0];
		var tr = pointFs[1];
		var enumerable = vvPoints as PointF[] ?? vvPoints.ToArray();
		var br = enumerable[0];
		var bl = enumerable[1];
		PointF[] outPut = { tl, tr, br, bl };
		return outPut;
	}

	private Color GetAverage(IInputArray sInput) {
		var foo = CvInvoke.Mean(sInput);
		var red = (int)foo.V2;
		var green = (int)foo.V1;
		var blue = (int)foo.V0;
		if (red < _blackLevel && green < _blackLevel && blue < _blackLevel) {
			return Color.FromArgb(0, 0, 0, 0); }

		return Color.FromArgb(red, green, blue);
	}

	public Color[] GetColors() {
		return _colorsLed;
	}

	public Color[] GetSectors() {
		return _colorsSectors;
	}

	private async Task CheckCrop(Mat image) {
		// Set our tolerances
		_sectorChanged = false;
		var width = ScaleWidth;
		var height = ScaleHeight;
		var wMax = width / 3;
		var hMax = height / 3;
		// How many non-black pixels can be in a given row
		var lPixels = 0;
		var pPixels = 0;

		width--;
		height--;
		var raw = image.GetRawData();
		var unique = raw.Distinct().ToArray();

		var count = Sum(raw);
		var noImage = count == 0 || width == 0 || height == 0 || unique.Length == 1 && unique[0] <= _cropBlackLevel;
		// If it is, we can stop here
		if (noImage) {
			_allBlack = true;
			return;
		}

		// Return here, because otherwise, "no input" detection won't work.
		if (!_useCrop) return;
		// Convert image to greyscale
		var gr = new Mat();
		CvInvoke.CvtColor(image, gr, ColorConversion.Bgr2Gray);

		_allBlack = false;
		// Check letterboxing
		if (_cropLetter) {
			for (var y = 0; y < hMax; y++) {
				var c1 = gr.Row(height - y);
				var c2 = gr.Row(y);
				var b1 = c1.GetRawData().SkipLast(8).Skip(8).ToArray();
				var b2 = c1.GetRawData().SkipLast(8).Skip(8).ToArray();
				var l1 = Sum(b1) / b1.Length;
				var l2 = Sum(b2) / b1.Length;
				c1.Dispose();
				c2.Dispose();
				if (l1 <= _cropBlackLevel && l2 <= _cropBlackLevel && l1==l2) {
					lPixels = y;
				} else {
					break;
				}
			}

			_cropCount++;
			if (_cropCount > 10) {
				_cropCount = 0;
			}
		}

		// Check pillarboxing
		if (_cropPillar) {
			for (var x = 0; x < wMax; x++) {
				var c1 = gr.Col(width - x);
				var c2 = gr.Col(x);
				var b1 = c1.GetRawData().SkipLast(8).Skip(8).ToArray();
				var b2 = c1.GetRawData().SkipLast(8).Skip(8).ToArray();
				var l1 = Sum(b1) / b1.Length;
				var l2 = Sum(b2) / b1.Length;
				c1.Dispose();
				c2.Dispose();
				if (l1 <= _cropBlackLevel && l2 <= _cropBlackLevel && l1 == l2) {
					pPixels = x;
				} else {
					break;
				}
			}
		}

		// Cleanup mat
		gr.Dispose();

		if (_cropPillar) {
			if (pPixels == 0) {
				if (_pillarCropCounter.Triggered || _pCrop) {
					_pillarCropCounter.Clear();
					_pCropPixels = 0;	
				}
				
			} else {
				_pillarCropCounter.Tick(Math.Abs(pPixels - _hCropCheck) < 4);
			}
		}

		if (_pillarCropCounter.Triggered && !_pCrop) {
			_pCrop = true;
			_sectorChanged = true;
			_pCropPixels = pPixels;
		}

		_hCropCheck = pPixels;


		if (_cropLetter) {
			if (lPixels == 0) {
				if (_lCropCounter.Triggered || _lCrop) {
					_lCropCounter.Clear();
					_lCrop = false;
					_sectorChanged = true;
					_lCropPixels = 0;	
				}
			} else {
				// Tick our counter - up if the difference between current and new crop values are lt 4, down otherwise.
				_lCropCounter.Tick(Math.Abs(lPixels - _vCropCheck) < 4);
			}
		}
		
		if (_lCropCounter.Triggered && !_lCrop) {
			_lCrop = true;
			_sectorChanged = true;
			_lCropPixels = lPixels;
		}

		_vCropCheck = lPixels;
		// Only calculate new sectors if the value has changed
		if (_sectorChanged) {
			Log.Debug($"Crop changed, redrawing {_lCropPixels} and {_pCropPixels}...");
			_sectorChanged = false;
			_fullCoords = DrawGrid();
			_fullSectors = DrawSectors();
		}

		await Task.FromResult(true);
	}

	private static int Sum(IEnumerable<byte> bytes) {
		return bytes.Aggregate(0, (current, b) => current + b);
	}

	private Rectangle[] DrawGrid() {
		var lOffset = _lCropPixels;
		var pOffset = _pCropPixels;
		var output = new Rectangle[_ledCount];

		// Bottom Region
		var bBottom = ScaleHeight - lOffset;
		var bTop = bBottom - _borderHeight;

		// Right Column Border
		var rRight = ScaleWidth - pOffset;
		var rLeft = rRight - _borderWidth;
		const float w = ScaleWidth;
		const float h = ScaleHeight;

		// Steps
		var widthTop = (int)Math.Ceiling(w / _topCount);
		var widthBottom = (int)Math.Ceiling(w / _bottomCount);
		var heightLeft = (int)Math.Ceiling(h / _leftCount);
		var heightRight = (int)Math.Ceiling(h / _rightCount);
		// Calc right regions, bottom to top
		var idx = 0;
		var pos = ScaleHeight - heightRight;

		for (var i = 0; i < _rightCount; i++) {
			if (pos < 0) {
				pos = 0;
			}

			output[idx] = new Rectangle((int)rLeft, pos, (int)_borderWidth, heightRight);
			pos -= heightRight;
			idx++;
		}

		// Calc top regions, from right to left
		pos = ScaleWidth - widthTop;

		for (var i = 0; i < _topCount; i++) {
			if (pos < 0) {
				pos = 0;
			}

			output[idx] = new Rectangle(pos, lOffset, widthTop, (int)_borderHeight);
			idx++;
			pos -= widthTop;
		}


		// Calc left regions (top to bottom)
		pos = 0;

		for (var i = 0; i < _leftCount; i++) {
			if (pos > ScaleHeight - heightLeft) {
				pos = ScaleHeight - heightLeft;
			}

			output[idx] = new Rectangle(pOffset, pos, (int)_borderWidth, heightLeft);
			pos += heightLeft;
			idx++;
		}

		// Calc bottom regions (L-R)
		pos = 0;
		for (var i = 0; i < _bottomCount; i++) {
			if (idx >= _ledCount) {
				Log.Warning($"Index is {idx}, but count is {_ledCount}");
				continue;
			}

			if (pos > ScaleWidth - widthBottom) {
				pos = ScaleWidth - widthBottom;
			}

			output[idx] = new Rectangle(pos, (int)bTop, widthBottom, (int)_borderHeight);
			pos += widthBottom;
			idx++;
		}

		if (idx != _ledCount) {
			Log.Warning($"Warning: Led count is {idx - 1}, but should be {_ledCount}");
		}

		return output;
	}

	private Rectangle[] DrawCenterSectors() {
		var pOffset = _pCropPixels;
		var lOffset = _lCropPixels;
		if (_hSectors == 0) {
			_hSectors = 10;
		}

		if (_vSectors == 0) {
			_vSectors = 6;
		}

		// This is where we're saving our output
		var fs = new Rectangle[_sectorCount];
		// Calculate heights, minus offset for boxing
		// Individual segment sizes
		var sectorWidth = (ScaleWidth - pOffset * 2) / _hSectors;
		var sectorHeight = (ScaleHeight - lOffset * 2) / _vSectors;
		// These are based on the border/strip values
		// Minimum limits for top, bottom, left, right            
		var top = ScaleHeight - lOffset - sectorHeight;
		var idx = 0;
		for (var v = _vSectors; v > 0; v--) {
			var left = ScaleWidth - pOffset - sectorWidth;
			for (var h = _hSectors; h > 0; h--) {
				fs[idx] = new Rectangle(left, top, sectorWidth, sectorHeight);
				idx++;
				left -= sectorWidth;
			}

			top -= sectorHeight;
		}

		return fs;
	}

	private Rectangle[] DrawSectors() {
		if (_useCenter) {
			return DrawCenterSectors();
		}

		// How many sectors does each region have?
		var pOffset = _pCropPixels;
		var lOffset = _lCropPixels;
		if (_hSectors == 0) {
			_hSectors = 10;
		}

		if (_vSectors == 0) {
			_vSectors = 6;
		}

		// This is where we're saving our output
		var fs = new Rectangle[_sectorCount];
		// Individual segment sizes
		const int squareSize = 40;
		var sectorWidth = (ScaleWidth - pOffset * 2) / _hSectors;
		var sectorHeight = (ScaleHeight - lOffset * 2) / _vSectors;
		// These are based on the border/strip values
		// Minimum limits for top, bottom, left, right            
		var minBot = ScaleHeight - lOffset - squareSize;
		// Calc right regions, bottom to top
		var idx = 0;
		var step = _vSectors - 1;
		while (step >= 0) {
			var size = step == _vSectors - 1 || step == 0 ? sectorWidth : squareSize;
			var x = ScaleWidth - pOffset - size;
			var ord = step * sectorHeight + lOffset;
			fs[idx] = new Rectangle(x, ord, size, sectorHeight);
			idx++;
			step--;
		}

		// Calc top regions, from right to left, skipping top-right corner (total horizontal sectors minus one)
		step = _hSectors - 2;
		while (step > 0) {
			var ord = step * sectorWidth + pOffset;
			fs[idx] = new Rectangle(ord, lOffset, sectorWidth, squareSize);
			idx++;
			step--;
		}

		step = 0;
		// Calc left regions (top to bottom), skipping top-left
		while (step <= _vSectors - 1) {
			var ord = step * sectorHeight + lOffset;
			var size = step == _vSectors - 1 || step == 0 ? sectorWidth : squareSize;
			fs[idx] = new Rectangle(pOffset, ord, size, sectorHeight);
			idx++;
			step++;
		}

		step = 1;
		// Calc bottom center regions (L-R)
		while (step <= _hSectors - 2) {
			var ord = step * sectorWidth + pOffset;
			fs[idx] = new Rectangle(ord, minBot, sectorWidth, squareSize);
			idx++;
			step += 1;
		}

		return fs;
	}
}

public class CropCounter {
	[JsonProperty]
	private readonly int _max;
	[JsonProperty]
	private int _count;

	public CropCounter(int max) {
		_max = max;
		_count = 0;
	}

	public void Clear() {
		_count = 0;
	}


	public void Tick(bool b) {
		if (b) {
			_count++;
			if (_count >= _max) {
				_count = _max;
			}
		} else {
			_count--;
			if (_count <= 0) {
				_count = 0;
			}
		}
	}

	[JsonProperty]
	public bool Triggered => _count >= _max;
}