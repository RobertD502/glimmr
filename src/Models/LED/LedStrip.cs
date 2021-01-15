using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using Glimmr.Models.Util;
using Glimmr.Services;
using rpi_ws281x;
using Serilog;

namespace Glimmr.Models.LED {
	public sealed class LedStrip : IDisposable {
		public bool Testing { get; set; }
		public float CurrentMilliamps { get; set; }
		private Controller _controller;
		private LedData _ld;
		private int _ledCount;
		private WS281x _strip;

		public LedStrip(LedData ld, ColorService colorService) {
			colorService.ColorSendEvent += UpdateAll;
			Initialize(ld);
		}


		public void Dispose() {
			_strip?.Dispose();
		}

		public void Reload(LedData ld) {
			Log.Debug("Setting brightness to " + ld.Brightness);
			_controller.Brightness = (byte) ld.Brightness;
			if (_ledCount != ld.LedCount) {
				_strip?.Dispose();
				Initialize(ld);
			}
		}

		private void Initialize(LedData ld) {
			_ld = ld ?? throw new ArgumentException("Invalid LED Data.");
			Log.Debug("Initializing LED Strip, type is " + ld.StripType);
			_ledCount = ld.LeftCount + ld.RightCount + ld.TopCount + ld.BottomCount;
			var stripType = ld.StripType switch {
				1 => StripType.SK6812W_STRIP,
				2 => StripType.WS2811_STRIP_RBG,
				0 => StripType.WS2812_STRIP,
				_ => StripType.WS2812_STRIP
			};
			// 18 = PWM0, 13 = PWM1, 21 = PCM, 10 = SPI0/MOSI
			var pin = ld.GpioNumber switch {
				13 => Pin.Gpio13,
				10 => Pin.Gpio10,
				21 => Pin.Gpio21,
				_ => Pin.Gpio18
			};
			Log.Debug($@"Count, pin, type: {_ledCount}, {ld.GpioNumber}, {(int) stripType}");
			var settings = Settings.CreateDefaultSettings();
			// Hey, look, this is built natively into the LED app
			if (ld.FixGamma) {
				settings.SetGammaCorrection(2.8f, 255, 255);
			}

			_controller = settings.AddController(_ledCount, pin, stripType);
			try {
				_strip = new WS281x(settings);
				Log.Debug($@"Strip created using {_ledCount} LEDs.");
			} catch (DllNotFoundException) {
				Log.Debug("Unable to initialize strips, we're not running on a pi!");
			}
		}


		public void StartTest(int len) {
			Testing = true;
			var lc = len;
			if (len < _ledCount) {
				lc = _ledCount;
			}

			var colors = new Color[lc];
			var black = new Color[lc];
			colors = ColorUtil.EmptyColors(colors);
			black = ColorUtil.EmptyColors(black);
			colors[len] = Color.FromArgb(255, 255, 0, 0);
			Testing = true;
			UpdateAll(colors.ToList(), true);
			Thread.Sleep(500);
			UpdateAll(black.ToList(), true);
			Thread.Sleep(500);
			UpdateAll(colors.ToList(), true);
			Thread.Sleep(1000);
			UpdateAll(black.ToList(), true);
			Testing = false;
		}


		public void StopTest() {
			Testing = false;
			var mt = ColorUtil.EmptyColors(new Color[_ld.LedCount]);
			UpdateAll(mt.ToList(), true);
		}

		private void UpdateAll(List<Color> colors, List<Color> sectors, double fadeTime) {
			UpdateAll(colors);
		}

		public void UpdateAll(List<Color> colors, bool force = false) {
			//Log.Debug("NOT UPDATING.");
			if (colors == null) {
				throw new ArgumentException("Invalid color input.");
			}

			if (Testing && !force) {
				return;
			}

			// Thanks, WLED!
			if (true) {
				colors = VoltAdjust(colors);
			}

			var iSource = 0;
			for (var i = 0; i < _ledCount; i++) {
				if (iSource >= colors.Count) {
					iSource = 0; // reset if at end of source
				}

				var tCol = colors[iSource];

				if (_ld.StripType == 1) {
					tCol = ColorUtil.ClampAlpha(tCol);
				}

				_controller.SetLED(i, tCol);
				iSource++;
			}

			_strip?.Render();
		}

		public void StopLights() {
			Log.Debug("Stopping LED Strip.");

			_strip?.Reset();
			Log.Debug("LED Strips stopped.");
		}

		private List<Color> VoltAdjust(List<Color> input) {
			//power limit calculation
			//each LED can draw up 195075 "power units" (approx. 53mA)
			//one PU is the power it takes to have 1 channel 1 step brighter per brightness step
			//so A=2,R=255,G=0,B=0 would use 510 PU per LED (1mA is about 3700 PU)
			var actualMilliampsPerLed = 25;
			var defaultBrightness = _ld.Brightness;
			var ablMaxMilliamps = 3200;
			var length = input.Count;
			var output = input;
			if (ablMaxMilliamps > 149 && actualMilliampsPerLed > 0) {
				//0 mA per LED and too low numbers turn off calculation

				var puPerMilliamp = 195075 / actualMilliampsPerLed;
				var powerBudget = ablMaxMilliamps * puPerMilliamp; //100mA for ESP power
				if (powerBudget > puPerMilliamp * length) {
					//each LED uses about 1mA in standby, exclude that from power budget
					powerBudget -= puPerMilliamp * length;
				} else {
					powerBudget = 0;
				}

				var powerSum = 0;

				for (var i = 0; i < length; i++) {
					//sum up the usage of each LED
					var c = input[i];
					powerSum += c.R + c.G + c.B + c.A;
				}

				if (_ld.StripType == 1) {
					//RGBW led total output with white LEDs enabled is still 50mA, so each channel uses less
					powerSum *= 3;
					powerSum >>= 2; //same as /= 4
				}

				var powerSum0 = powerSum;
				powerSum *= defaultBrightness;

				if (powerSum > powerBudget) {
					//scale brightness down to stay in current limit
					var scale = powerBudget / (float) powerSum;
					var scaleI = scale * 255;
					var scaleB = scaleI > 255 ? 255 : scaleI;
					var newBri = scale8(defaultBrightness, scaleB);
					//_strip.SetBrightness((int)newBri);
					//Log.Debug($"Scaling brightness to {newBri / 255}.");
					CurrentMilliamps = powerSum0 * newBri / puPerMilliamp;
					if (newBri < defaultBrightness) {
						output = ColorUtil.ClampBrightness(input, newBri);
					}
				} else {
					CurrentMilliamps = (float) powerSum / puPerMilliamp;
					if (defaultBrightness < 255) {
						output = ColorUtil.ClampBrightness(input, defaultBrightness);
					}
				}

				CurrentMilliamps += length; //add standby power back to estimate
			} else {
				CurrentMilliamps = 0;
				if (defaultBrightness < 255) {
					output = ColorUtil.ClampBrightness(input, defaultBrightness);
				}
			}

			return output;
		}

		private float scale8(float i, float scale) {
			return i * (scale / 256);
		}
	}
}