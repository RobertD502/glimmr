﻿#region

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Glimmr.Enums;
using Glimmr.Models.Util;
using Glimmr.Services;
using LifxNetPlus;
using Serilog;

#endregion

namespace Glimmr.Models.ColorTarget.Lifx {
	public class LifxDevice : ColorTarget, IColorTarget {
		public LifxData Data { get; set; }
		private LightBulb B { get; }

		private readonly LifxClient _client;
		private BeamLayout _beamLayout;
		private ColorConverter _conv;
		private int[] _gammaTableBG;
		private int[] _gammaTableR;
		private bool _hasMulti;
		private int _multizoneCount;
		private double _scaledBrightness;

		private int _targetSector;


		public LifxDevice(LifxData d, ColorService colorService) : base(colorService) {
			DataUtil.GetItem<int>("captureMode");
			Data = d ?? throw new ArgumentException("Invalid Data");
			Brightness = Data.Brightness;
			_client = colorService.ControlService.GetAgent("LifxAgent");
			colorService.ColorSendEvent += SetColor;
			colorService.ControlService.RefreshSystemEvent += LoadData;
			B = new LightBulb(d.HostName, d.MacAddress, d.Service, (uint) d.Port);
			LoadData();
		}

		public bool Streaming { get; set; }
		public bool Testing { get; set; }
		public int Brightness { get; set; }
		public string Id { get; set; }
		public string IpAddress { get; set; }
		public string Tag { get; set; }

		public bool Enable { get; set; }

		IColorTargetData IColorTarget.Data {
			get => Data;
			set => Data = (LifxData) value;
		}


		public async Task StartStream(CancellationToken ct) {
			if (!Enable) {
				return;
			}

			Log.Information($"{Data.Tag}::Starting stream: {Data.Id}...");
			// Recalculate target sector before starting stream, just in case.
			var col = new LifxColor(0, 0, 0);
			//var col = new LifxColor {R = 0, B = 0, G = 0};
			_client.SetLightPowerAsync(B, true);
			_client.SetColorAsync(B, col, 2700);
			Streaming = true;
			await Task.FromResult(Streaming);
			Log.Information($"{Data.Tag}::Stream started: {Data.Id}.");
		}

		public async Task FlashColor(Color color) {
			var nC = new LifxColor(color);
			//var nC = new LifxColor {R = color.R, B = color.B, G = color.G};
			await _client.SetColorAsync(B, nC).ConfigureAwait(false);
		}


		public async Task StopStream() {
			if (!Streaming) {
				return;
			}

			Streaming = false;
			if (_client == null) {
				return;
			}

			Log.Information($"{Data.Tag}::Stopping stream.: {Data.Id}...");
			_client.SetColorAsync(B, new LifxColor(Color.FromArgb(0, 0, 0))).ConfigureAwait(false);
			// Awaiting this breaks things, leave it alone...
			_client.SetLightPowerAsync(B, false).ConfigureAwait(false);
			_client.SetLightPowerAsync(B, false).ConfigureAwait(false);
			Log.Information($"{Data.Tag}::Stream stopped: {Data.Id}.");
		}


		public Task ReloadData() {
			var newData = DataUtil.GetDevice<LifxData>(Id);
			Data = newData;
			LoadData();
			return Task.CompletedTask;
		}

		public void Dispose() {
		}

		public void SetColor(List<Color> colors, List<Color> list, int arg3, bool force = false) {
			if (!Streaming || !Enable || Testing && !force) {
				return;
			}

			if (_hasMulti) {
				SetColorMulti(colors);
			} else {
				SetColorSingle(list);
			}

			ColorService.Counter.Tick(Id);
		}


		public bool IsEnabled() {
			return Enable;
		}

		private int[] GenerateGammaTable(double gamma = 2.3) {
			const int maxIn = 255;
			const int maxOut = 255; // Top end of OUTPUT range
			var output = new int[256];
			for (var i = 0; i <= maxIn; i++) {
				output[i] = (int) (Math.Pow((float) i / maxIn, gamma) * maxOut + 0.5);
			}

			return output;
		}

		private void LoadData() {
			var sd = DataUtil.GetSystemData();

			DataUtil.GetItem<int>("captureMode");

			_hasMulti = Data.HasMultiZone;
			if (_hasMulti) {
				_multizoneCount = Data.LedCount;
				_beamLayout = Data.BeamLayout;
				_gammaTableR = GenerateGammaTable(1.8);
				_gammaTableBG = GenerateGammaTable(1.4);
				if (_beamLayout == null && _multizoneCount != 0) {
					Data.GenerateBeamLayout();
					_beamLayout = Data.BeamLayout;
				}
			} else {
				var target = Data.TargetSector;
				if ((CaptureMode) sd.CaptureMode == CaptureMode.DreamScreen) {
					target = ColorUtil.CheckDsSectors(target);
				}

				if (sd.UseCenter) {
					target = ColorUtil.FindEdge(target + 1);
				}

				_targetSector = target;
			}

			IpAddress = Data.IpAddress;
			var oldBrightness = Brightness;
			Brightness = Data.Brightness;
			_scaledBrightness = Brightness / 100d;
			if (oldBrightness != Brightness) {
				var bri = Brightness / 100 * 255;
				_client.SetBrightnessAsync(B, (ushort) bri).ConfigureAwait(false);
			}

			Id = Data.Id;
			Enable = Data.Enable;
		}

		private void SetColorMulti(List<Color> colors) {
			if (_client == null || _beamLayout == null) {
				Log.Warning("Null client or no layout!");
				return;
			}

			var output = new List<Color>();
			foreach (var segment in _beamLayout.Segments) {
				var len = segment.LedCount;
				var segColors = ColorUtil.TruncateColors(colors, segment.Offset, len * 2);
				if (segment.Repeat) {
					var col = segColors[0];
					for (var c = 0; c < len * 2; c++) {
						segColors[c] = col;
					}
				}

				if (segment.Reverse && !segment.Repeat) {
					segColors = segColors.Reverse().ToArray();
				}

				output.AddRange(segColors);
			}

			var i = 0;

			var cols = new List<LifxColor>();
			foreach (var col in output) {
				if (i == 0) {
					var ar = _gammaTableR[col.R];
					var ag = _gammaTableBG[col.G];
					var ab = _gammaTableBG[col.B];
					cols.Add(new LifxColor(Color.FromArgb(ar, ag, ab), _scaledBrightness));
					i = 1;
				} else {
					i = 0;
				}
			}

			_client.SetExtendedColorZonesAsync(B, cols, 5).ConfigureAwait(false);
		}


		private void SetColorSingle(List<Color> list) {
			var sectors = list;
			if (sectors == null || _client == null) {
				return;
			}

			if (_targetSector >= sectors.Count) {
				return;
			}

			var input = sectors[_targetSector];

			var nC = new LifxColor(input);
			//var nC = new LifxColor {R = input.R, B = input.B, G = input.G};

			_client.SetColorAsync(B, nC).ConfigureAwait(false);
			ColorService.Counter.Tick(Id);
		}
	}
}