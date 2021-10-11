﻿#region

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Tasks;
using Glimmr.Enums;
using Glimmr.Models.Util;
using Glimmr.Services;
using Serilog;

#endregion

namespace Glimmr.Models {
	public class FrameCounter : IDisposable {
		public ConcurrentDictionary<string, int> Rates { get; private set; }
		private readonly Stopwatch _stopwatch;
		private ConcurrentDictionary<string, int> _ticks;
		private DeviceMode _mode;

		public FrameCounter(ColorService cs) {
			_ticks = new ConcurrentDictionary<string, int> {["source"] = 0};
			Rates = _ticks;
			_stopwatch = new Stopwatch();
			cs.ControlService.SetModeEvent += Mode;
			cs.ControlService.RefreshSystemEvent += RefreshSystem;
			var sd = DataUtil.GetSystemData();
			_mode = sd.DeviceMode;
			if (sd.AutoDisabled) _mode = DeviceMode.Off;
		}

		private void RefreshSystem() {
			var sd = DataUtil.GetSystemData();
			
			_mode = sd.DeviceMode;
			if (_mode != DeviceMode.Off) {
				_stopwatch.Restart();
			} else {
				_stopwatch.Stop();
			}

			_ticks = new ConcurrentDictionary<string, int> {["source"] = 0};
			Rates = _ticks;
		}


		private Task Mode(object arg1, DynamicEventArgs arg2) {
			var newMode = (DeviceMode) arg2.Arg0;
			_mode = newMode;
			if (newMode != DeviceMode.Off) {
				_stopwatch.Restart();
			} else {
				_stopwatch.Stop();
			}

			_ticks = new ConcurrentDictionary<string, int> {["source"] = 0};
			Rates = _ticks;
			return Task.CompletedTask;
		}

		public void Tick(string id) {
			// Make sure watch is running
			if (_mode == DeviceMode.Off) {
				Log.Debug("Device is off, no ticky ticky.");
				_ticks = new ConcurrentDictionary<string, int> {["source"] = 0};
				return;
			}
			if (!_stopwatch.IsRunning) {
				_stopwatch.Start();
			}

			// Clear our cache every minute so we don't wind up with massive stored values over time
			if (_stopwatch.Elapsed >= TimeSpan.FromSeconds(1)) {
				_stopwatch.Restart();
				Rates = _ticks;
				_ticks = new ConcurrentDictionary<string, int> {["source"] = 0};
			}

			if (_ticks.Keys.Contains(id)) {
				_ticks[id]++;
			} else {
				_ticks[id] = 0;
			}
		}

		public void Reset() {
			_ticks = new ConcurrentDictionary<string, int> {["source"] = 0};
			Rates = _ticks;
		}
		
		public void Dispose() {
			_stopwatch.Stop();
			GC.SuppressFinalize(this);
		}

	}
}